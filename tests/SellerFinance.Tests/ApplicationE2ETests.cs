using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SellerFinance.Api;

namespace SellerFinance.Tests;

public sealed class ApplicationE2ETests : IClassFixture<SellerFinanceApplicationFactory>
{
    private readonly SellerFinanceApplicationFactory factory;
    public ApplicationE2ETests(SellerFinanceApplicationFactory factory)=>this.factory=factory;

    [Fact]
    public async Task Register_Settings_Expense_Tenant_Isolation_And_Delete_Flow_Works()
    {
        using var client=factory.CreateClient(new(){AllowAutoRedirect=false,HandleCookies=true});
        var email=$"e2e-{Guid.NewGuid():N}@example.test";const string password="PilotTest123";const string organizationName="E2E Pilot Organization";

        var register=await client.PostAsJsonAsync("/api/v1/auth/register",new{email,password,displayName="E2E Owner",organizationName});
        Assert.Equal(HttpStatusCode.OK,register.StatusCode);
        await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<SellerFinanceDbContext>();var user=await db.Users.SingleAsync(x=>x.Email==email);Assert.True(await db.OrganizationUsers.AnyAsync(x=>x.UserId==user.Id&&x.JoinedAt!=null));Assert.True(await db.Subscriptions.AnyAsync(x=>x.OrganizationId==db.OrganizationUsers.Single(m=>m.UserId==user.Id).OrganizationId&&x.PeriodEnd>DateTimeOffset.UtcNow));}
        var sessionResponse=await client.GetAsync("/api/v1/session");Assert.Equal(HttpStatusCode.OK,sessionResponse.StatusCode);
        var session=await JsonDocument.ParseAsync(await sessionResponse.Content.ReadAsStreamAsync());var organizationId=session.RootElement.GetProperty("organizationId").GetString()!;

        client.DefaultRequestHeaders.Add("X-Organization-Id",organizationId);
        var settings=await client.PutAsJsonAsync($"/api/v1/organizations/{organizationId}",new{name="E2E Updated",timeZone="Asia/Qyzylorda",currency="KZT"});
        Assert.Equal(HttpStatusCode.OK,settings.StatusCode);
        var expense=await client.PostAsJsonAsync("/api/v1/expenses",new{type="Advertising",amount=1250.50m,date="2026-08-12",comment="e2e"});
        Assert.Equal(HttpStatusCode.Created,expense.StatusCode);
        Assert.Single((await (await client.GetAsync("/api/v1/expenses")).Content.ReadFromJsonAsync<JsonElement[]>())!);

        client.DefaultRequestHeaders.Remove("X-Organization-Id");client.DefaultRequestHeaders.Add("X-Organization-Id","demo-organization");
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/api/v1/products")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Organization-Id");client.DefaultRequestHeaders.Add("X-Organization-Id",organizationId);

        var deletion=await client.SendAsync(new(HttpMethod.Delete,$"/api/v1/organizations/{organizationId}"){Content=JsonContent.Create(new{organizationName="E2E Updated",password})});
        Assert.Equal(HttpStatusCode.OK,deletion.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/v1/session")).StatusCode);
    }

    [Fact]
    public async Task Health_And_Protected_Routes_Start_Successfully()
    {
        using var client=factory.CreateClient(new(){AllowAutoRedirect=false});
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/health/database")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/v1/session")).StatusCode);
    }
}

public sealed class SellerFinanceApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string databaseName=$"seller-finance-e2e-{Guid.NewGuid():N}";
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");builder.UseSetting("TEST_USE_INMEMORY","true");builder.UseSetting("TOKEN_ENCRYPTION_KEY",Convert.ToBase64String(new byte[32]));builder.UseSetting("EMAIL_CONFIRMATION_REQUIRED","false");
        builder.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["TEST_USE_INMEMORY"]="true",["TOKEN_ENCRYPTION_KEY"]=Convert.ToBase64String(new byte[32]),["EMAIL_CONFIRMATION_REQUIRED"]="false"
        }));
        builder.ConfigureServices(services=>
        {
            services.RemoveAll<DbContextOptions<SellerFinanceDbContext>>();services.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<SellerFinanceDbContext>>();services.RemoveAll<SellerFinanceDbContext>();
            services.AddDbContext<SellerFinanceDbContext>(options=>options.UseInMemoryDatabase(databaseName));
        });
    }
}
