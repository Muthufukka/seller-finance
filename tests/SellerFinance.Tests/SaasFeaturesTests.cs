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

    [Theory]
    [InlineData(SubscriptionPlan.Trial,1,90)]
    [InlineData(SubscriptionPlan.Start,1,365)]
    [InlineData(SubscriptionPlan.Pro,3,null)]
    [InlineData(SubscriptionPlan.Business,10,null)]
    public void Store_And_History_Limits_Match_Tariff(SubscriptionPlan plan,int stores,int? days){var now=new DateTimeOffset(2026,8,12,0,0,0,TimeSpan.Zero);Assert.Equal(stores,PlanLimits.MaxStores(plan));Assert.Equal(days.HasValue?now.AddDays(-days.Value):DateTimeOffset.UnixEpoch,PlanLimits.InitialHistoryFrom(plan,now));}

    [Fact]
    public async Task ExportBuilder_Creates_Utf8_Csv_Without_Missing_Cost_As_Zero()
    {
        await using var db=CreateDb();db.Products.Add(new(){Id="p1",OrganizationId="org",Sku="SKU-1",Name="Product"});await db.SaveChangesAsync();
        var artifact=await new ExportBuilder(db).BuildAsync(Job("csv","MissingCosts"),CancellationToken.None);var text=Encoding.UTF8.GetString(artifact.Content);
        Assert.StartsWith("\uFEFF",text);Assert.Contains("SKU-1",text);Assert.Equal(1,artifact.RowCount);
    }

    [Fact]
    public async Task Missing_Cost_Export_Includes_Partial_Historical_Coverage()
    {
        await using var db=CreateDb();db.Products.Add(new(){Id="p1",OrganizationId="org",Sku="PARTIAL",Name="Partial"});db.ProductCostHistory.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",ProductId="p1",CostAmount=400,EffectiveFrom=new(2026,8,10),CreatedByUserId="test"});db.Orders.AddRange(new(){Id="before",ExternalId="before",OrganizationId="org",Date=new(2026,8,5),CompletionDate=new(2026,8,5),Status=SellerFinance.Domain.OrderStatus.Completed,Lines=[new(){Id=Guid.NewGuid(),OrderId="before",ProductId="p1",Revenue=1000,Quantity=1}]},new(){Id="after",ExternalId="after",OrganizationId="org",Date=new(2026,8,12),CompletionDate=new(2026,8,12),Status=SellerFinance.Domain.OrderStatus.Completed,Lines=[new(){Id=Guid.NewGuid(),OrderId="after",ProductId="p1",Revenue=1000,Quantity=1}]});await db.SaveChangesAsync();
        var artifact=await new ExportBuilder(db).BuildAsync(Job("csv","MissingCosts"),CancellationToken.None);var text=Encoding.UTF8.GetString(artifact.Content);Assert.Equal(1,artifact.RowCount);Assert.Contains("PARTIAL",text);Assert.Contains("50",text);
    }

    [Fact]
    public async Task Spreadsheet_Exports_Do_Not_Treat_Text_As_Formulas()
    {
        await using var db=CreateDb();db.Products.Add(new(){Id="p1",OrganizationId="org",Sku="=2+2",Name="@SUM(1,1)"});await db.SaveChangesAsync();
        var csv=Encoding.UTF8.GetString((await new ExportBuilder(db).BuildAsync(Job("csv","Products"),CancellationToken.None)).Content);Assert.Contains("\"'=2+2\"",csv);Assert.Contains("\"'@SUM(1,1)\"",csv);Assert.DoesNotContain("\"=2+2\"",csv);
        var xlsx=await new ExportBuilder(db).BuildAsync(Job("xlsx","Products"),CancellationToken.None);using var stream=new MemoryStream(xlsx.Content);using var workbook=new XLWorkbook(stream);var sheet=workbook.Worksheet(1);Assert.False(sheet.Cell("A2").HasFormula);Assert.False(sheet.Cell("B2").HasFormula);Assert.Equal("=2+2",sheet.Cell("A2").GetString());Assert.Equal("@SUM(1,1)",sheet.Cell("B2").GetString());
    }

    [Fact]
    public async Task ExportBuilder_Creates_Valid_Xlsx()
    {
        await using var db=CreateDb();db.Products.Add(new(){Id="p1",OrganizationId="org",Sku="SKU-1",Name="Product"});db.ProductCostHistory.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",ProductId="p1",CostAmount=400,EffectiveFrom=new(2026,1,1),CreatedByUserId="test"});db.Orders.Add(new(){Id="order",ExternalId="order",OrganizationId="org",Date=new(2026,8,12),CompletionDate=new(2026,8,12),Status=SellerFinance.Domain.OrderStatus.Completed,Lines=[new(){Id=Guid.NewGuid(),OrderId="order",ProductId="p1",Revenue=1000,Quantity=1,FeeRate=.1m,Delivery=100,OtherVariableCosts=50}]});db.Expenses.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",ProductId="p1",Type=ExpenseType.Advertising,Amount=25,Date=new(2026,8,12),CreatedByUserId="test"});await db.SaveChangesAsync();var artifact=await new ExportBuilder(db).BuildAsync(Job("xlsx","Products"),CancellationToken.None);
        using var stream=new MemoryStream(artifact.Content);using var workbook=new XLWorkbook(stream);var sheet=workbook.Worksheet(1);Assert.Equal("SKU",sheet.Cell("A1").GetString());Assert.Equal("Fees",sheet.Cell("F1").GetString());Assert.Equal("Delivery",sheet.Cell("G1").GetString());Assert.Equal("Other expenses",sheet.Cell("H1").GetString());Assert.Equal("SKU-1",sheet.Cell("A2").GetString());Assert.Equal(1000,Convert.ToDecimal(sheet.Cell("D2").Value.GetNumber()));Assert.Equal(400,Convert.ToDecimal(sheet.Cell("E2").Value.GetNumber()));Assert.Equal(100,Convert.ToDecimal(sheet.Cell("F2").Value.GetNumber()));Assert.Equal(100,Convert.ToDecimal(sheet.Cell("G2").Value.GetNumber()));Assert.Equal(75,Convert.ToDecimal(sheet.Cell("H2").Value.GetNumber()));Assert.Equal(325,Convert.ToDecimal(sheet.Cell("I2").Value.GetNumber()));
    }

    [Fact]
    public async Task ExportBuilder_Exports_Paginated_Order_Source()
    {
        await using var db=CreateDb();db.Products.Add(new(){Id="p1",OrganizationId="org",Sku="SKU-1",Name="Product"});db.ProductCostHistory.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",ProductId="p1",CostAmount=500m,EffectiveFrom=new(2026,1,1),CreatedByUserId="test"});db.Orders.Add(new(){Id="order-1",ExternalId="KSP-1",OrganizationId="org",Date=new(2026,8,12),CompletionDate=new(2026,8,12),Status=SellerFinance.Domain.OrderStatus.Completed,Lines=[new(){Id=Guid.NewGuid(),OrderId="order-1",ProductId="p1",Revenue=1500m,Quantity=1}]});await db.SaveChangesAsync();
        var artifact=await new ExportBuilder(db).BuildAsync(Job("csv","Orders"),CancellationToken.None);var text=Encoding.UTF8.GetString(artifact.Content);
        Assert.Contains("KSP-1",text);Assert.Contains("Other expenses",text);Assert.Contains("Total expenses",text);Assert.Equal(1,artifact.RowCount);var outside=Job("csv","Orders");outside.DateFrom=new(2026,9,1);outside.DateTo=new(2026,9,30);Assert.Equal(0,(await new ExportBuilder(db).BuildAsync(outside,CancellationToken.None)).RowCount);db.Products.Add(new(){Id="missing",OrganizationId="org",Sku="MISSING",Name="Missing"});db.Orders.Add(new(){Id="order-2",ExternalId="KSP-2",OrganizationId="org",Date=new(2026,8,12),CompletionDate=new(2026,8,12),Status=SellerFinance.Domain.OrderStatus.Completed,Lines=[new(){Id=Guid.NewGuid(),OrderId="order-2",ProductId="missing",Revenue=2000,Quantity=1}]});await db.SaveChangesAsync();var complete=Job("csv","Orders");complete.CompleteCostsOnly=true;var completeArtifact=await new ExportBuilder(db).BuildAsync(complete,CancellationToken.None);Assert.Equal(1,completeArtifact.RowCount);Assert.DoesNotContain("KSP-2",Encoding.UTF8.GetString(completeArtifact.Content));
    }

    [Fact]
    public async Task Concurrent_Export_Workers_Claim_Different_Jobs()
    {
        var services=new ServiceCollection();var database=Guid.NewGuid().ToString();services.AddDbContext<SellerFinanceDbContext>(x=>x.UseInMemoryDatabase(database));services.AddScoped<ExportBuilder>();await using var provider=services.BuildServiceProvider();await using(var scope=provider.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<SellerFinanceDbContext>();db.ExportJobs.AddRange(Job("csv","Products"),Job("csv","Products"));await db.SaveChangesAsync();}
        var scopes=provider.GetRequiredService<IServiceScopeFactory>();var first=new ExportWorker(scopes,NullLogger<ExportWorker>.Instance);var second=new ExportWorker(scopes,NullLogger<ExportWorker>.Instance);await Task.WhenAll(first.ProcessOneAsync(CancellationToken.None),second.ProcessOneAsync(CancellationToken.None));
        await using var verify=provider.CreateAsyncScope();var jobs=await verify.ServiceProvider.GetRequiredService<SellerFinanceDbContext>().ExportJobs.ToArrayAsync();Assert.Equal(2,jobs.Length);Assert.All(jobs,x=>Assert.Equal(ExportJobStatus.Succeeded,x.Status));
    }

    [Fact]
    public void Active_Sync_Job_Index_Is_Unique_And_Filtered()
    {
        using var db=CreateDb();var index=db.Model.FindEntityType(typeof(SyncJobEntity))!.GetIndexes().Single(x=>x.Properties.Select(p=>p.Name).SequenceEqual([nameof(SyncJobEntity.MarketplaceConnectionId)]));Assert.True(index.IsUnique);Assert.Equal("\"Status\" IN (0, 1, 3)",index.GetFilter());
    }

    [Fact]
    public async Task Export_Worker_Reclaims_Stale_Running_Job()
    {
        var services=new ServiceCollection();var database=Guid.NewGuid().ToString();services.AddDbContext<SellerFinanceDbContext>(x=>x.UseInMemoryDatabase(database));services.AddScoped<ExportBuilder>();await using var provider=services.BuildServiceProvider();await using(var scope=provider.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<SellerFinanceDbContext>();var job=Job("csv","Products");job.Status=ExportJobStatus.Running;job.StartedAt=DateTimeOffset.UtcNow.AddHours(-1);db.ExportJobs.Add(job);await db.SaveChangesAsync();}
        await new ExportWorker(provider.GetRequiredService<IServiceScopeFactory>(),NullLogger<ExportWorker>.Instance).ProcessOneAsync(CancellationToken.None);await using var verify=provider.CreateAsyncScope();Assert.Equal(ExportJobStatus.Succeeded,(await verify.ServiceProvider.GetRequiredService<SellerFinanceDbContext>().ExportJobs.SingleAsync()).Status);
    }

    [Fact]
    public async Task Telegram_Start_Code_Links_Only_Matching_Pending_Organization()
    {
        await using var db=CreateDb();const string code="link-code";db.TelegramConnections.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",LinkCodeHash=TokenTools.Hash(code),LinkCodeExpiresAt=DateTimeOffset.UtcNow.AddMinutes(5)});await db.SaveChangesAsync();var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"TELEGRAM_BOT_TOKEN","test"}}).Build();var client=new TelegramClient(new HttpClient(new OkHandler()),config);using var update=JsonDocument.Parse("""{"message":{"text":"/start link-code","chat":{"id":12345}}}""");
        await TelegramWebhook.ProcessAsync(update.RootElement,db,client,CancellationToken.None);
        var connection=await db.TelegramConnections.SingleAsync();Assert.Equal("Active",connection.Status);Assert.Equal(12345,connection.ChatId);Assert.True(await db.AuditLogs.AnyAsync(x=>x.OrganizationId=="org"&&x.Action=="telegram.link.completed"&&x.EntityId==connection.Id.ToString()));Assert.DoesNotContain(code,connection.LinkCodeHash);
    }

    [Fact]
    public async Task Telegram_Webhook_Ignores_Malformed_Chat_Id_Without_Consuming_Link_Code()
    {
        await using var db=CreateDb();const string code="pending-code";var hash=TokenTools.Hash(code);db.TelegramConnections.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",LinkCodeHash=hash,LinkCodeExpiresAt=DateTimeOffset.UtcNow.AddMinutes(5)});await db.SaveChangesAsync();var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"TELEGRAM_BOT_TOKEN","test"}}).Build();var client=new TelegramClient(new HttpClient(new OkHandler()),config);using var update=JsonDocument.Parse("""{"message":{"text":"/start pending-code","chat":{"id":"not-a-number"}}}""");
        await TelegramWebhook.ProcessAsync(update.RootElement,db,client,CancellationToken.None);var connection=await db.TelegramConnections.SingleAsync();Assert.Equal("Pending",connection.Status);Assert.Null(connection.ChatId);Assert.Equal(hash,connection.LinkCodeHash);Assert.Empty(db.AuditLogs);
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
    public async Task Notification_Worker_Reclaims_Stale_Sending_Delivery()
    {
        var services=new ServiceCollection();var database=Guid.NewGuid().ToString();services.AddDbContext<SellerFinanceDbContext>(x=>x.UseInMemoryDatabase(database));await using var provider=services.BuildServiceProvider();await using(var scope=provider.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<SellerFinanceDbContext>();db.TelegramConnections.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",Status="Active",ChatId=123});db.NotificationDeliveries.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",EventType=NotificationEventType.MissingCost,DeduplicationKey="stale",Message="Safe message",Status=NotificationDeliveryStatus.Sending,StartedAt=DateTimeOffset.UtcNow.AddMinutes(-20)});await db.SaveChangesAsync();}
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"TELEGRAM_BOT_TOKEN","test"}}).Build();await new NotificationDeliveryWorker(provider.GetRequiredService<IServiceScopeFactory>(),new TelegramClient(new HttpClient(new OkHandler()),config),NullLogger<NotificationDeliveryWorker>.Instance).ProcessOneAsync(CancellationToken.None);await using var verify=provider.CreateAsyncScope();var delivery=await verify.ServiceProvider.GetRequiredService<SellerFinanceDbContext>().NotificationDeliveries.SingleAsync();Assert.Equal(NotificationDeliveryStatus.Sent,delivery.Status);Assert.Equal(1,delivery.Attempt);
    }

    [Fact]
    public async Task Saas_Admin_Retry_Creates_New_Safe_Job_Only_For_Attention_State()
    {
        await using var db=CreateDb();var connection=Guid.NewGuid();var source=Guid.NewGuid();db.Organizations.Add(new(){Id="org",Name="Org",Status="Active"});db.Subscriptions.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",Status=SubscriptionStatus.Active,PeriodEnd=DateTimeOffset.UtcNow.AddMonths(1)});db.MarketplaceConnections.Add(new(){Id=connection,OrganizationId="org",Provider="Kaspi",TokenCiphertext=[1],TokenNonce=[1],TokenTag=[1]});db.SyncJobs.Add(new(){Id=source,OrganizationId="org",MarketplaceConnectionId=connection,Status=SyncJobStatus.RequiresAttention,WindowFrom=DateTimeOffset.UtcNow.AddDays(-14),WindowTo=DateTimeOffset.UtcNow.AddDays(-1)});await db.SaveChangesAsync();
        var result=await SaasAdminOperations.RetrySyncAsync(db,source);
        Assert.Equal(SyncRetryFailure.None,result.Failure);Assert.NotNull(result.Job);Assert.NotEqual(source,result.Job!.Id);Assert.Equal(SyncJobStatus.Queued,result.Job.Status);Assert.Equal(connection,result.Job.MarketplaceConnectionId);
    }

    [Fact]
    public async Task Saas_Admin_Retry_Is_Blocked_By_Disabled_Feature()
    {
        await using var db=CreateDb();var connection=Guid.NewGuid();var source=Guid.NewGuid();db.Organizations.Add(new(){Id="org",Name="Org",Status="Active"});db.Subscriptions.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",Status=SubscriptionStatus.Active,PeriodEnd=DateTimeOffset.UtcNow.AddMonths(1)});db.OrganizationFeatureFlags.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",Key="KaspiSync",Enabled=false,UpdatedByUserId="admin"});db.MarketplaceConnections.Add(new(){Id=connection,OrganizationId="org",Provider="Kaspi",TokenCiphertext=[1],TokenNonce=[1],TokenTag=[1]});db.SyncJobs.Add(new(){Id=source,OrganizationId="org",MarketplaceConnectionId=connection,Status=SyncJobStatus.RequiresAttention,WindowFrom=DateTimeOffset.UtcNow.AddDays(-14),WindowTo=DateTimeOffset.UtcNow});await db.SaveChangesAsync();
        Assert.Equal(SyncRetryFailure.OrganizationDisabled,(await SaasAdminOperations.RetrySyncAsync(db,source)).Failure);
    }

    private static ExportJobEntity Job(string format,string report)=>new(){Id=Guid.NewGuid(),OrganizationId="org",CreatedByUserId="user",Format=format,ReportType=report,DownloadTokenHash="hash"};
    private static SellerFinanceDbContext CreateDb()=>new(new DbContextOptionsBuilder<SellerFinanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed class OkHandler:HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));}
    private sealed class StatusHandler(HttpStatusCode status):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>Task.FromResult(new HttpResponseMessage(status));}
}
