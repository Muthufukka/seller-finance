using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SellerFinance.Api;
using SellerFinance.Domain;

namespace SellerFinance.Tests;

public sealed class KaspiIntegrationTests
{
    [Fact]
    public async Task Importer_Does_Not_Update_Order_From_Another_Tenant_Even_With_Matching_Connection_And_External_Id()
    {
        await using var db=new SellerFinanceDbContext(new DbContextOptionsBuilder<SellerFinanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);var connection=Guid.NewGuid();db.Orders.Add(new(){Id="foreign",OrganizationId="other",MarketplaceConnectionId=connection,ExternalId="external",Code="PRIVATE",Date=new(2026,1,1)});await db.SaveChangesAsync();var source=new KaspiOrderDto("external","PUBLIC",1000,"COMPLETED",DateTimeOffset.UtcNow,[new("line","SKU","Product",null,1,1000,0)]);await KaspiOrderImporter.UpsertAsync(db,"org",connection,[source]);await db.SaveChangesAsync();Assert.Equal("PRIVATE",(await db.Orders.SingleAsync(x=>x.Id=="foreign")).Code);Assert.Equal(1,await db.Orders.CountAsync(x=>x.OrganizationId=="org"));
    }

    [Fact]
    public async Task Sync_Worker_Quarantines_Job_When_Connection_Belongs_To_Another_Tenant()
    {
        var database=Guid.NewGuid().ToString();var services=new ServiceCollection().AddDbContext<SellerFinanceDbContext>(x=>x.UseInMemoryDatabase(database)).BuildServiceProvider();var connectionId=Guid.NewGuid();var jobId=Guid.NewGuid();await using(var scope=services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<SellerFinanceDbContext>();db.Organizations.AddRange(new(){Id="org",Name="Org"},new(){Id="other",Name="Other"});db.Subscriptions.AddRange(new(){Id=Guid.NewGuid(),OrganizationId="org",Status=SubscriptionStatus.Trialing,PeriodEnd=DateTimeOffset.UtcNow.AddDays(1)},new(){Id=Guid.NewGuid(),OrganizationId="other",Status=SubscriptionStatus.Trialing,PeriodEnd=DateTimeOffset.UtcNow.AddDays(1)});db.MarketplaceConnections.Add(new(){Id=connectionId,OrganizationId="other",Status=MarketplaceConnectionStatus.Active});db.SyncJobs.Add(new(){Id=jobId,OrganizationId="org",MarketplaceConnectionId=connectionId,Status=SyncJobStatus.Queued,WindowFrom=DateTimeOffset.UtcNow.AddDays(-1),WindowTo=DateTimeOffset.UtcNow});await db.SaveChangesAsync();}
        await new KaspiSyncWorker(services.GetRequiredService<IServiceScopeFactory>(),NullLogger<KaspiSyncWorker>.Instance).ProcessOneAsync(default);await using var verify=services.CreateAsyncScope();var job=await verify.ServiceProvider.GetRequiredService<SellerFinanceDbContext>().SyncJobs.SingleAsync(x=>x.Id==jobId);Assert.Equal(SyncJobStatus.RequiresAttention,job.Status);Assert.Equal("TENANT_CONNECTION_MISMATCH",job.ErrorCode);
    }
    [Fact]
    public void TokenCipher_RoundTrips_Without_Persisting_Plaintext()
    {
        var key=Convert.ToBase64String(Enumerable.Range(1,32).Select(x=>(byte)x).ToArray());
        var cipher=new TokenCipher(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"TOKEN_ENCRYPTION_KEY",key}}).Build());
        var encrypted=cipher.Encrypt("secret-kaspi-token");
        var entity=new MarketplaceConnectionEntity{TokenCiphertext=encrypted.Ciphertext,TokenNonce=encrypted.Nonce,TokenTag=encrypted.Tag};

        Assert.Equal("secret-kaspi-token",cipher.Decrypt(entity));
        Assert.DoesNotContain("secret-kaspi-token",Convert.ToBase64String(entity.TokenCiphertext));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized,"TOKEN_UNAUTHORIZED")]
    [InlineData(HttpStatusCode.Forbidden,"TOKEN_FORBIDDEN")]
    [InlineData((HttpStatusCode)429,"RATE_LIMITED")]
    [InlineData(HttpStatusCode.InternalServerError,"KASPI_UNAVAILABLE")]
    public async Task Client_Maps_Failure_Without_Logging_Or_Returning_Token(HttpStatusCode status,string code)
    {
        var http=new HttpClient(new StubHandler(status,"{}")){BaseAddress=new Uri("https://kaspi.kz/shop/api/v2/")};
        var result=await new KaspiClient(http).GetOrdersAsync("top-secret",DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow,CancellationToken.None);
        Assert.False(result.Success);Assert.Equal(code,result.ErrorCode);
    }

    [Fact]
    public async Task Client_Parses_Official_JsonApi_Order_Shape()
    {
        const string json="""{"data":[{"type":"orders","id":"id-1","attributes":{"code":"123","totalPrice":14990,"status":"COMPLETED","creationDate":1786453200000,"completionDate":1786539600000,"paymentMode":"PREPAID","deliveryCostForSeller":750}}]}""";
        const string entries="""{"data":[{"type":"orderentries","id":"entry-1","attributes":{"quantity":2,"totalPrice":14990,"basePrice":8000,"deliveryCost":500}}]}""";const string product="""{"data":{"type":"masterproducts","id":"product-1","attributes":{"code":"SKU-1","name":"Товар","category":"Категория"}}}""";
        var http=new HttpClient(new RouteHandler(json,entries,product)){BaseAddress=new Uri("https://kaspi.kz/shop/api/v2/")};
        var result=await new KaspiClient(http).GetOrdersAsync("token",DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow,CancellationToken.None);
        Assert.True(result.Success);var order=Assert.Single(result.Orders);Assert.Equal("123",order.Code);Assert.Equal("PREPAID",order.PaymentMode);Assert.Equal(750,order.SellerDeliveryCost);Assert.NotNull(order.CompletedAt);var line=Assert.Single(order.Lines);Assert.Equal("SKU-1",line.ProductCode);Assert.Equal("product-1",line.ExternalProductId);Assert.Equal(8000,line.BasePrice);Assert.Equal(500,line.ItemDeliveryCost);
    }

    [Fact]
    public async Task Client_Classifies_Network_And_Invalid_Contract_Failures_Without_Throwing()
    {
        var network=new KaspiClient(new HttpClient(new ThrowingHandler()){BaseAddress=new Uri("https://kaspi.kz/shop/api/v2/")});
        var unavailable=await network.GetOrdersAsync("secret",DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow,CancellationToken.None);
        Assert.False(unavailable.Success);Assert.Equal(HttpStatusCode.ServiceUnavailable,unavailable.StatusCode);Assert.Equal("KASPI_UNAVAILABLE",unavailable.ErrorCode);

        var invalid=new KaspiClient(new HttpClient(new StubHandler(HttpStatusCode.OK,"{not-json")){BaseAddress=new Uri("https://kaspi.kz/shop/api/v2/")});
        var malformed=await invalid.GetOrdersAsync("secret",DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow,CancellationToken.None);
        Assert.False(malformed.Success);Assert.Equal(HttpStatusCode.BadGateway,malformed.StatusCode);Assert.Equal("KASPI_INVALID_RESPONSE",malformed.ErrorCode);
    }

    [Fact]
    public async Task Client_Fails_Explicitly_Instead_Of_Silently_Truncating_At_Page_Limit()
    {
        var handler=new FullPageHandler();var client=new KaspiClient(new HttpClient(handler){BaseAddress=new Uri("https://kaspi.kz/shop/api/v2/")});
        var result=await client.GetOrdersAsync("secret",DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow,CancellationToken.None);
        Assert.False(result.Success);Assert.Equal("KASPI_PAGINATION_LIMIT",result.ErrorCode);Assert.Equal(100,handler.RequestCount);Assert.Empty(result.Orders);
    }

    [Fact]
    public async Task Importer_Upserts_Per_Connection_And_Allows_Same_External_Order_In_Two_Stores()
    {
        await using var db=new SellerFinanceDbContext(new DbContextOptionsBuilder<SellerFinanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);var first=Guid.NewGuid();var second=Guid.NewGuid();var source=new KaspiOrderDto("external-1","100",1000,"COMPLETED",DateTimeOffset.UtcNow,[new("entry-1","SKU","Product",null,1,1000,0)]);
        await KaspiOrderImporter.UpsertAsync(db,"org",first,[source]);await db.SaveChangesAsync();await KaspiOrderImporter.UpsertAsync(db,"org",first,[source]);await db.SaveChangesAsync();await KaspiOrderImporter.UpsertAsync(db,"org",second,[source]);await db.SaveChangesAsync();
        Assert.Equal(2,await db.Orders.CountAsync());Assert.Single(await db.Orders.Where(x=>x.MarketplaceConnectionId==first).ToArrayAsync());Assert.Single(await db.Orders.Where(x=>x.MarketplaceConnectionId==second).ToArrayAsync());Assert.Equal(2,await db.OrderStatusHistory.CountAsync());
    }

    [Fact]
    public async Task Importer_Records_Only_Real_Status_Transitions_And_Preserves_Kaspi_Status()
    {
        await using var db=new SellerFinanceDbContext(new DbContextOptionsBuilder<SellerFinanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);var connection=Guid.NewGuid();var created=DateTimeOffset.UtcNow;KaspiOrderDto Source(string status)=>new("external","CODE",1000,status,created,[new("line","SKU","Product",null,1,1000,0)]);
        await KaspiOrderImporter.UpsertAsync(db,"org",connection,[Source("COMPLETED")]);await db.SaveChangesAsync();await KaspiOrderImporter.UpsertAsync(db,"org",connection,[Source("COMPLETED")]);await db.SaveChangesAsync();await KaspiOrderImporter.UpsertAsync(db,"org",connection,[Source("KASPI_DELIVERY_RETURN_REQUESTED")]);await db.SaveChangesAsync();
        var history=await db.OrderStatusHistory.OrderBy(x=>x.ChangedAt).ToArrayAsync();Assert.Equal(2,history.Length);Assert.Equal(OrderStatus.Completed,history[0].Status);Assert.Equal("KASPI_DELIVERY_RETURN_REQUESTED",history[1].ExternalStatus);Assert.Equal(OrderStatus.Returned,history[1].Status);Assert.Equal(OrderStatus.Returned,(await db.Orders.SingleAsync()).Status);
    }

    [Fact]
    public async Task Importer_Uses_Completion_Date_And_Allocates_Order_Delivery_Exactly_Once()
    {
        await using var db=new SellerFinanceDbContext(new DbContextOptionsBuilder<SellerFinanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);var connection=Guid.NewGuid();var completed=new DateTimeOffset(2026,8,12,14,0,0,TimeSpan.Zero);
        var source=new KaspiOrderDto("external","CODE",3000,"COMPLETED",completed.AddDays(-2),[new("l1","A","A",null,1,1000,null,1200,"pa"),new("l2","B","B",null,1,2000,null,2300,"pb")],completed,"PAY_WITH_CREDIT",999);
        await KaspiOrderImporter.UpsertAsync(db,"org",connection,[source]);await db.SaveChangesAsync();var order=await db.Orders.Include(x=>x.Lines).SingleAsync();
        Assert.Equal(new DateOnly(2026,8,12),order.CompletionDate);Assert.False(order.CalculationDateFallback);Assert.Equal("CODE",order.Code);Assert.Equal("PAY_WITH_CREDIT",order.PaymentMode);Assert.Equal(999,order.Lines.Sum(x=>x.Delivery));Assert.All(order.Lines,x=>Assert.Null(x.ItemDeliveryCost));
    }

    [Fact]
    public async Task Importer_Prefers_Item_Delivery_And_Does_Not_Add_Order_Level_Delivery()
    {
        await using var db=new SellerFinanceDbContext(new DbContextOptionsBuilder<SellerFinanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);var source=new KaspiOrderDto("external","CODE",3000,"COMPLETED",DateTimeOffset.UtcNow,[new("l1","A","A",null,1,1000,100),new("l2","B","B",null,1,2000,200)],null,null,999);
        await KaspiOrderImporter.UpsertAsync(db,"org",Guid.NewGuid(),[source]);await db.SaveChangesAsync();var order=await db.Orders.Include(x=>x.Lines).SingleAsync();Assert.Equal(300,order.Lines.Sum(x=>x.Delivery));Assert.Equal(999,order.SellerDeliveryCost);
    }

    private sealed class StubHandler(HttpStatusCode status,string content):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>Task.FromResult(new HttpResponseMessage(status){Content=new StringContent(content,Encoding.UTF8,"application/vnd.api+json")});
    }
    private sealed class RouteHandler(string orders,string entries,string product):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken){var path=request.RequestUri!.AbsolutePath;var body=path.EndsWith("/product")?product:path.EndsWith("/entries")?entries:orders;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/vnd.api+json")});}
    }
    private sealed class ThrowingHandler:HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>throw new HttpRequestException("network unavailable");
    }
    private sealed class FullPageHandler:HttpMessageHandler
    {
        private static readonly string Body="{\"data\":["+String.Join(',',Enumerable.Range(0,100).Select(x=>$"{{\"type\":\"orders\",\"id\":\"{x}\",\"attributes\":{{\"code\":\"{x}\",\"totalPrice\":1,\"status\":\"COMPLETED\",\"creationDate\":1786453200000}}}}"))+"]}";
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken){RequestCount++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Body,Encoding.UTF8,"application/vnd.api+json")});}
    }
}
