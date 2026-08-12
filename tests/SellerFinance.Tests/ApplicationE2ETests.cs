using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Identity;
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

    [Fact]
    public async Task Owner_Manages_Members_Invitations_And_Role_Boundaries()
    {
        using var ownerClient=factory.CreateClient(new(){AllowAutoRedirect=false,HandleCookies=true});var ownerEmail=$"owner-{Guid.NewGuid():N}@example.test";const string password="PilotTest123";
        Assert.Equal(HttpStatusCode.OK,(await ownerClient.PostAsJsonAsync("/api/v1/auth/register",new{email=ownerEmail,password,displayName="Owner",organizationName="Members E2E"})).StatusCode);var session=await ownerClient.GetFromJsonAsync<JsonElement>("/api/v1/session");var organizationId=session.GetProperty("organizationId").GetString()!;var ownerId=session.GetProperty("userId").GetString()!;ownerClient.DefaultRequestHeaders.Add("X-Organization-Id",organizationId);
        var memberEmail=$"viewer-{Guid.NewGuid():N}@example.test";string memberId;
        await using(var scope=factory.Services.CreateAsyncScope()){var users=scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();var member=new AppUser{UserName=memberEmail,Email=memberEmail,EmailConfirmed=true,DisplayName="Viewer"};Assert.True((await users.CreateAsync(member,password)).Succeeded);memberId=member.Id;var db=scope.ServiceProvider.GetRequiredService<SellerFinanceDbContext>();db.OrganizationUsers.Add(new(){OrganizationId=organizationId,UserId=memberId,Role=OrganizationRole.Analyst,JoinedAt=DateTimeOffset.UtcNow});var subscription=await db.Subscriptions.SingleAsync(x=>x.OrganizationId==organizationId);subscription.Plan=SubscriptionPlan.Business;await db.SaveChangesAsync();}

        var members=await ownerClient.GetFromJsonAsync<JsonElement[]>($"/api/v1/organizations/{organizationId}/members");Assert.Equal(2,members!.Length);
        Assert.Equal(HttpStatusCode.NoContent,(await ownerClient.PutAsJsonAsync($"/api/v1/organizations/{organizationId}/members/{memberId}/role",new{role="Viewer"})).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await ownerClient.PutAsJsonAsync($"/api/v1/organizations/{organizationId}/members/{ownerId}/role",new{role="Admin"})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await ownerClient.DeleteAsync($"/api/v1/organizations/{organizationId}/members/{ownerId}")).StatusCode);

        using var viewerClient=factory.CreateClient(new(){AllowAutoRedirect=false,HandleCookies=true});Assert.Equal(HttpStatusCode.OK,(await viewerClient.PostAsJsonAsync("/api/v1/auth/login",new{email=memberEmail,password,rememberMe=false})).StatusCode);viewerClient.DefaultRequestHeaders.Add("X-Organization-Id",organizationId);Assert.Equal(HttpStatusCode.Forbidden,(await viewerClient.DeleteAsync($"/api/v1/organizations/{organizationId}/members/{ownerId}")).StatusCode);

        var invitedEmail=$"invite-{Guid.NewGuid():N}@example.test";var invitation=await ownerClient.PostAsJsonAsync($"/api/v1/organizations/{organizationId}/members",new{email=invitedEmail,role="Analyst"});Assert.Equal(HttpStatusCode.OK,invitation.StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await ownerClient.PostAsJsonAsync($"/api/v1/organizations/{organizationId}/members",new{email=invitedEmail,role="Viewer"})).StatusCode);var pending=await ownerClient.GetFromJsonAsync<JsonElement[]>($"/api/v1/organizations/{organizationId}/invitations");Assert.NotNull(pending);Assert.Single(pending);var invitationId=pending[0].GetProperty("id").GetGuid();Assert.Equal(HttpStatusCode.NoContent,(await ownerClient.DeleteAsync($"/api/v1/organizations/{organizationId}/invitations/{invitationId}")).StatusCode);
        var acceptingEmail=$"accept-{Guid.NewGuid():N}@example.test";var acceptingInvitation=await (await ownerClient.PostAsJsonAsync($"/api/v1/organizations/{organizationId}/members",new{email=acceptingEmail,role="Analyst"})).Content.ReadFromJsonAsync<JsonElement>();var invitationToken=acceptingInvitation.GetProperty("invitationToken").GetString()!;using var acceptingClient=factory.CreateClient(new(){AllowAutoRedirect=false,HandleCookies=true});Assert.Equal(HttpStatusCode.OK,(await acceptingClient.PostAsJsonAsync("/api/v1/auth/register",new{email=acceptingEmail,password,displayName="Accepting",organizationName="Temporary Org"})).StatusCode);var acceptingSession=await acceptingClient.GetFromJsonAsync<JsonElement>("/api/v1/session");acceptingClient.DefaultRequestHeaders.Add("X-Organization-Id",acceptingSession.GetProperty("organizationId").GetString()!);var accepted=await acceptingClient.PostAsJsonAsync("/api/v1/invitations/accept",new{token=invitationToken});Assert.Equal(HttpStatusCode.OK,accepted.StatusCode);acceptingClient.DefaultRequestHeaders.Remove("X-Organization-Id");acceptingClient.DefaultRequestHeaders.Add("X-Organization-Id",organizationId);var targetSession=await acceptingClient.GetFromJsonAsync<JsonElement>("/api/v1/session");Assert.Equal("Analyst",targetSession.GetProperty("role").GetString());
        Assert.Equal(HttpStatusCode.NoContent,(await ownerClient.DeleteAsync($"/api/v1/organizations/{organizationId}/members/{memberId}")).StatusCode);
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
