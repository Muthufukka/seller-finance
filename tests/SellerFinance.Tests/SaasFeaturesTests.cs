using System.Net;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SellerFinance.Api;

namespace SellerFinance.Tests;

public sealed class SaasFeaturesTests
{
    [Theory]
    [InlineData(SubscriptionPlan.Trial,2)]
    [InlineData(SubscriptionPlan.Start,3)]
    [InlineData(SubscriptionPlan.Pro,10)]
    [InlineData(SubscriptionPlan.Business,30)]
    public void Member_Limits_Match_Tariff(SubscriptionPlan plan,int expected)=>Assert.Equal(expected,PlanLimits.MaxMembers(plan));

    [Fact]
    public async Task ExportBuilder_Creates_Utf8_Csv_Without_Missing_Cost_As_Zero()
    {
        await using var db=CreateDb();db.Products.Add(new(){Id="p1",OrganizationId="org",Sku="SKU-1",Name="Product"});await db.SaveChangesAsync();
        var artifact=await new ExportBuilder(db).BuildAsync(Job("csv","MissingCosts"),CancellationToken.None);var text=Encoding.UTF8.GetString(artifact.Content);
        Assert.StartsWith("\uFEFF",text);Assert.Contains("SKU-1",text);Assert.Equal(1,artifact.RowCount);
    }

    [Fact]
    public async Task ExportBuilder_Creates_Valid_Xlsx()
    {
        await using var db=CreateDb();db.Products.Add(new(){Id="p1",OrganizationId="org",Sku="SKU-1",Name="Product"});await db.SaveChangesAsync();var artifact=await new ExportBuilder(db).BuildAsync(Job("xlsx","Products"),CancellationToken.None);
        using var stream=new MemoryStream(artifact.Content);using var workbook=new XLWorkbook(stream);Assert.Equal("SKU",workbook.Worksheet(1).Cell("A1").GetString());Assert.Equal("SKU-1",workbook.Worksheet(1).Cell("A2").GetString());
    }

    [Fact]
    public async Task ExportBuilder_Exports_Paginated_Order_Source()
    {
        await using var db=CreateDb();db.Products.Add(new(){Id="p1",OrganizationId="org",Sku="SKU-1",Name="Product"});db.Orders.Add(new(){Id="order-1",ExternalId="KSP-1",OrganizationId="org",Date=new(2026,8,12),CompletionDate=new(2026,8,12),Status=SellerFinance.Domain.OrderStatus.Completed,Lines=[new(){Id=Guid.NewGuid(),OrderId="order-1",ProductId="p1",Revenue=1500m,Quantity=1,UnitCost=500m}]});await db.SaveChangesAsync();
        var artifact=await new ExportBuilder(db).BuildAsync(Job("csv","Orders"),CancellationToken.None);var text=Encoding.UTF8.GetString(artifact.Content);
        Assert.Contains("KSP-1",text);Assert.Equal(1,artifact.RowCount);
    }

    [Fact]
    public async Task Telegram_Start_Code_Links_Only_Matching_Pending_Organization()
    {
        await using var db=CreateDb();const string code="link-code";db.TelegramConnections.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",LinkCodeHash=TokenTools.Hash(code),LinkCodeExpiresAt=DateTimeOffset.UtcNow.AddMinutes(5)});await db.SaveChangesAsync();var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"TELEGRAM_BOT_TOKEN","test"}}).Build();var client=new TelegramClient(new HttpClient(new OkHandler()),config);using var update=JsonDocument.Parse("""{"message":{"text":"/start link-code","chat":{"id":12345}}}""");
        await TelegramWebhook.ProcessAsync(update.RootElement,db,client,CancellationToken.None);
        var connection=await db.TelegramConnections.SingleAsync();Assert.Equal("Active",connection.Status);Assert.Equal(12345,connection.ChatId);
    }

    [Fact]
    public void Telegram_Webhook_Secret_Is_Validated_From_Supplied_Value()
    {
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"TELEGRAM_WEBHOOK_SECRET","expected-secret"}}).Build();Assert.True(TelegramWebhook.ValidSecret("expected-secret",config));Assert.False(TelegramWebhook.ValidSecret("wrong-secret",config));Assert.False(TelegramWebhook.ValidSecret("",config));
    }

    [Fact]
    public async Task Notification_Queue_Deduplicates_And_Worker_Delivers()
    {
        var services=new ServiceCollection();var database=Guid.NewGuid().ToString();services.AddDbContext<SellerFinanceDbContext>(x=>x.UseInMemoryDatabase(database));await using var provider=services.BuildServiceProvider();await using(var scope=provider.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<SellerFinanceDbContext>();db.TelegramConnections.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",Status="Active",ChatId=123});db.NotificationRules.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",EventType=NotificationEventType.MissingCost,Enabled=true});await db.SaveChangesAsync();}
        var dispatcher=new NotificationDispatcher(provider.GetRequiredService<IServiceScopeFactory>());Assert.True(await dispatcher.QueueAsync("org",NotificationEventType.MissingCost,"No cost",2,"missing:day",CancellationToken.None));Assert.False(await dispatcher.QueueAsync("org",NotificationEventType.MissingCost,"Duplicate",2,"missing:day",CancellationToken.None));
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"TELEGRAM_BOT_TOKEN","test"}}).Build();var worker=new NotificationDeliveryWorker(provider.GetRequiredService<IServiceScopeFactory>(),new TelegramClient(new HttpClient(new OkHandler()),config),NullLogger<NotificationDeliveryWorker>.Instance);await worker.ProcessOneAsync(CancellationToken.None);
        await using var verify=provider.CreateAsyncScope();var delivery=await verify.ServiceProvider.GetRequiredService<SellerFinanceDbContext>().NotificationDeliveries.SingleAsync();Assert.Equal(NotificationDeliveryStatus.Sent,delivery.Status);Assert.Equal(1,delivery.Attempt);
    }

    [Theory]
    [InlineData(-0.1,true)]
    [InlineData(0,false)]
    [InlineData(2,false)]
    public void Negative_Margin_Threshold_Uses_Less_Than(decimal margin,bool expected)=>Assert.Equal(expected,NotificationDispatcher.MatchesThreshold(NotificationEventType.NegativeMargin,margin,0));

    [Fact]
    public async Task Successful_Sync_Queues_Missing_Cost_And_Negative_Margin_Digests()
    {
        var services=new ServiceCollection();var database=Guid.NewGuid().ToString();services.AddDbContext<SellerFinanceDbContext>(x=>x.UseInMemoryDatabase(database));await using var provider=services.BuildServiceProvider();await using var scope=provider.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<SellerFinanceDbContext>();db.TelegramConnections.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",Status="Active",ChatId=123});db.NotificationRules.AddRange(new(){Id=Guid.NewGuid(),OrganizationId="org",EventType=NotificationEventType.MissingCost,Enabled=true},new(){Id=Guid.NewGuid(),OrganizationId="org",EventType=NotificationEventType.NegativeMargin,Enabled=true,Threshold=0});db.Products.AddRange(new(){Id="missing",OrganizationId="org",Sku="M",Name="Missing"},new(){Id="loss",OrganizationId="org",Sku="L",Name="Loss"});db.ProductCostHistory.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",ProductId="loss",CostAmount=2000,EffectiveFrom=new(2026,8,1),CreatedByUserId="user"});db.Orders.Add(new(){Id="o",ExternalId="o",OrganizationId="org",Date=new(2026,8,12),CompletionDate=new(2026,8,12),Status=SellerFinance.Domain.OrderStatus.Completed,Lines=[new(){Id=Guid.NewGuid(),OrderId="o",ProductId="missing",Revenue=1000,Quantity=1},new(){Id=Guid.NewGuid(),OrderId="o",ProductId="loss",Revenue=1000,Quantity=1}]});await db.SaveChangesAsync();
        await KaspiSyncWorker.QueueFinancialAlertsAsync(db,new NotificationDispatcher(provider.GetRequiredService<IServiceScopeFactory>()),"org","https://example.test",CancellationToken.None);
        var types=await db.NotificationDeliveries.Select(x=>x.EventType).ToArrayAsync();Assert.Contains(NotificationEventType.MissingCost,types);Assert.Contains(NotificationEventType.NegativeMargin,types);
    }

    [Fact]
    public async Task Notification_Worker_Schedules_Retry_When_Telegram_Rejects()
    {
        var services=new ServiceCollection();var database=Guid.NewGuid().ToString();services.AddDbContext<SellerFinanceDbContext>(x=>x.UseInMemoryDatabase(database));await using var provider=services.BuildServiceProvider();await using(var scope=provider.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<SellerFinanceDbContext>();db.TelegramConnections.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",Status="Active",ChatId=123});db.NotificationDeliveries.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",EventType=NotificationEventType.MissingCost,DeduplicationKey="retry",Message="Safe message"});await db.SaveChangesAsync();}
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"TELEGRAM_BOT_TOKEN","test"}}).Build();var worker=new NotificationDeliveryWorker(provider.GetRequiredService<IServiceScopeFactory>(),new TelegramClient(new HttpClient(new StatusHandler(HttpStatusCode.InternalServerError)),config),NullLogger<NotificationDeliveryWorker>.Instance);await worker.ProcessOneAsync(CancellationToken.None);
        await using var verify=provider.CreateAsyncScope();var delivery=await verify.ServiceProvider.GetRequiredService<SellerFinanceDbContext>().NotificationDeliveries.SingleAsync();Assert.Equal(NotificationDeliveryStatus.RetryScheduled,delivery.Status);Assert.Equal("TELEGRAM_REJECTED",delivery.ErrorCode);Assert.True(delivery.NextAttemptAt>DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Saas_Admin_Retry_Creates_New_Safe_Job_Only_For_Attention_State()
    {
        await using var db=CreateDb();var connection=Guid.NewGuid();var source=Guid.NewGuid();db.Organizations.Add(new(){Id="org",Name="Org",Status="Active"});db.MarketplaceConnections.Add(new(){Id=connection,OrganizationId="org",Provider="Kaspi",TokenCiphertext=[1],TokenNonce=[1],TokenTag=[1]});db.SyncJobs.Add(new(){Id=source,OrganizationId="org",MarketplaceConnectionId=connection,Status=SyncJobStatus.RequiresAttention,WindowFrom=DateTimeOffset.UtcNow.AddDays(-14),WindowTo=DateTimeOffset.UtcNow.AddDays(-1)});await db.SaveChangesAsync();
        var result=await SaasAdminOperations.RetrySyncAsync(db,source);
        Assert.Equal(SyncRetryFailure.None,result.Failure);Assert.NotNull(result.Job);Assert.NotEqual(source,result.Job!.Id);Assert.Equal(SyncJobStatus.Queued,result.Job.Status);Assert.Equal(connection,result.Job.MarketplaceConnectionId);
    }

    [Fact]
    public async Task Saas_Admin_Retry_Is_Blocked_By_Disabled_Feature()
    {
        await using var db=CreateDb();var connection=Guid.NewGuid();var source=Guid.NewGuid();db.Organizations.Add(new(){Id="org",Name="Org",Status="Active"});db.OrganizationFeatureFlags.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",Key="KaspiSync",Enabled=false,UpdatedByUserId="admin"});db.MarketplaceConnections.Add(new(){Id=connection,OrganizationId="org",Provider="Kaspi",TokenCiphertext=[1],TokenNonce=[1],TokenTag=[1]});db.SyncJobs.Add(new(){Id=source,OrganizationId="org",MarketplaceConnectionId=connection,Status=SyncJobStatus.RequiresAttention,WindowFrom=DateTimeOffset.UtcNow.AddDays(-14),WindowTo=DateTimeOffset.UtcNow});await db.SaveChangesAsync();
        Assert.Equal(SyncRetryFailure.OrganizationDisabled,(await SaasAdminOperations.RetrySyncAsync(db,source)).Failure);
    }

    private static ExportJobEntity Job(string format,string report)=>new(){Id=Guid.NewGuid(),OrganizationId="org",CreatedByUserId="user",Format=format,ReportType=report,DownloadTokenHash="hash"};
    private static SellerFinanceDbContext CreateDb()=>new(new DbContextOptionsBuilder<SellerFinanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed class OkHandler:HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));}
    private sealed class StatusHandler(HttpStatusCode status):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>Task.FromResult(new HttpResponseMessage(status));}
}
