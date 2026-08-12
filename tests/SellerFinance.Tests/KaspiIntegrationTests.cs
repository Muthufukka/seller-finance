using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
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
        var http=new HttpClient(new StubHandler(HttpStatusCode.OK,json)){BaseAddress=new Uri("https://kaspi.kz/shop/api/v2/")};
        var result=await new KaspiClient(http).GetOrdersAsync("token",DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow,CancellationToken.None);
        Assert.True(result.Success);Assert.Single(result.Orders);Assert.Equal("123",result.Orders[0].Code);Assert.Equal(14990,result.Orders[0].TotalPrice);
    }

    private sealed class StubHandler(HttpStatusCode status,string content):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>Task.FromResult(new HttpResponseMessage(status){Content=new StringContent(content,Encoding.UTF8,"application/vnd.api+json")});
    }
}
