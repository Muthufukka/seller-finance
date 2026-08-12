using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using SellerFinance.Api;

namespace SellerFinance.Tests;

public sealed class KaspiIntegrationTests
{
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
        const string json="""{"data":[{"type":"orders","id":"id-1","attributes":{"code":"123","totalPrice":14990,"status":"COMPLETED","creationDate":1786453200000}}]}""";
        const string entries="""{"data":[{"type":"orderentries","id":"entry-1","attributes":{"quantity":2,"totalPrice":14990,"deliveryCost":500}}]}""";const string product="""{"data":{"type":"masterproducts","id":"product-1","attributes":{"code":"SKU-1","name":"Товар","category":"Категория"}}}""";
        var http=new HttpClient(new RouteHandler(json,entries,product)){BaseAddress=new Uri("https://kaspi.kz/shop/api/v2/")};
        var result=await new KaspiClient(http).GetOrdersAsync("token",DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow,CancellationToken.None);
        Assert.True(result.Success);Assert.Single(result.Orders);Assert.Equal("123",result.Orders[0].Code);Assert.Equal("SKU-1",Assert.Single(result.Orders[0].Lines).ProductCode);
    }

    [Fact]
    public async Task Importer_Upserts_Per_Connection_And_Allows_Same_External_Order_In_Two_Stores()
    {
        await using var db=new SellerFinanceDbContext(new DbContextOptionsBuilder<SellerFinanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);var first=Guid.NewGuid();var second=Guid.NewGuid();var source=new KaspiOrderDto("external-1","100",1000,"COMPLETED",DateTimeOffset.UtcNow,[new("entry-1","SKU","Product",null,1,1000,0)]);
        await KaspiOrderImporter.UpsertAsync(db,"org",first,[source]);await db.SaveChangesAsync();await KaspiOrderImporter.UpsertAsync(db,"org",first,[source]);await db.SaveChangesAsync();await KaspiOrderImporter.UpsertAsync(db,"org",second,[source]);await db.SaveChangesAsync();
        Assert.Equal(2,await db.Orders.CountAsync());Assert.Single(await db.Orders.Where(x=>x.MarketplaceConnectionId==first).ToArrayAsync());Assert.Single(await db.Orders.Where(x=>x.MarketplaceConnectionId==second).ToArrayAsync());
    }

    private sealed class StubHandler(HttpStatusCode status,string content):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>Task.FromResult(new HttpResponseMessage(status){Content=new StringContent(content,Encoding.UTF8,"application/vnd.api+json")});
    }
    private sealed class RouteHandler(string orders,string entries,string product):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken){var path=request.RequestUri!.AbsolutePath;var body=path.EndsWith("/product")?product:path.EndsWith("/entries")?entries:orders;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/vnd.api+json")});}
    }
}
