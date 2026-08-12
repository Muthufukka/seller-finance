using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
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

        var settings=await client.PutAsJsonAsync($"/api/v1/organizations/{organizationId}",new{name="E2E Updated",timeZone="Asia/Qyzylorda",currency="KZT"});
        Assert.Equal(HttpStatusCode.OK,settings.StatusCode);
        var expense=await client.PostAsJsonAsync("/api/v1/expenses",new{type="Advertising",amount=1250.50m,date="2026-08-12",comment="e2e"});
        Assert.Equal(HttpStatusCode.Created,expense.StatusCode);
        Assert.Single((await (await client.GetAsync("/api/v1/expenses")).Content.ReadFromJsonAsync<JsonElement[]>())!);
        const string productId="e2e-product";await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<SellerFinanceDbContext>();db.Products.AddRange(new(){Id=productId,OrganizationId=organizationId,Sku="E2E-SKU",Name="E2E Product",Category="Home"},new(){Id="e2e-product-2",OrganizationId=organizationId,Sku="Z-SKU",Name="Zeta Product"});db.ProductCostHistory.Add(new(){Id=Guid.NewGuid(),OrganizationId=organizationId,ProductId=productId,CostAmount=400,EffectiveFrom=new(2026,1,1),Source=CostSource.Manual,CreatedByUserId="e2e"});db.Orders.Add(new(){Id="e2e-order",ExternalId="e2e-order",OrganizationId=organizationId,Status=SellerFinance.Domain.OrderStatus.Completed,Date=new(2026,8,12),CompletionDate=new(2026,8,12),Lines=[new(){Id=Guid.NewGuid(),OrderId="e2e-order",ProductId=productId,Revenue=1000,Quantity=1}]});await db.SaveChangesAsync();}
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/v1/fee-rules",new{scope="Category",valueType="Percentage",value=10,effectiveFrom="2026-08-12",category=""})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/v1/fee-rules",new{scope="Default",valueType="Percentage",value=10,effectiveFrom="2026-08-12",effectiveTo="2026-08-11"})).StatusCode);
        var feeRuleResponse=await client.PostAsJsonAsync("/api/v1/fee-rules",new{scope="Category",valueType="Percentage",value=10,effectiveFrom="2026-08-01",category=" Home "});Assert.Equal(HttpStatusCode.Created,feeRuleResponse.StatusCode);var feeRuleId=(await feeRuleResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();Assert.Equal(HttpStatusCode.OK,(await client.PutAsJsonAsync($"/api/v1/fee-rules/{feeRuleId}/end",new{effectiveTo="2026-08-31"})).StatusCode);await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<SellerFinanceDbContext>();var rule=await db.FeeRules.SingleAsync(x=>x.Id==feeRuleId);Assert.Equal("Home",rule.Category);Assert.Equal(new DateOnly(2026,8,31),rule.EffectiveTo);Assert.True(await db.AuditLogs.AnyAsync(x=>x.OrganizationId==organizationId&&x.Action=="fee.rule.ended"&&x.EntityId==feeRuleId.ToString()));}
        var productSeries=await client.GetFromJsonAsync<JsonElement[]>($"/api/v1/products/{productId}/timeseries?dateFrom=2026-08-01&dateTo=2026-08-31");Assert.NotNull(productSeries);Assert.Single(productSeries);Assert.Equal(1000,productSeries[0].GetProperty("revenue").GetDecimal());
        Assert.Equal(HttpStatusCode.OK,(await client.PutAsJsonAsync($"/api/v1/products/{productId}/status",new{status="Archived"})).StatusCode);var archived=await client.GetFromJsonAsync<JsonElement>("/api/v1/products?filter=archived&sortBy=revenue&sortDirection=desc");Assert.Single(archived.GetProperty("items").EnumerateArray());Assert.Equal("revenue",archived.GetProperty("sortBy").GetString());var sorted=await client.GetFromJsonAsync<JsonElement>("/api/v1/products?sortBy=name&sortDirection=desc");Assert.Equal("e2e-product-2",sorted.GetProperty("items")[0].GetProperty("id").GetString());Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync("/api/v1/products?sortBy=unknown")).StatusCode);await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<SellerFinanceDbContext>();Assert.Equal("Archived",(await db.Products.SingleAsync(x=>x.Id==productId)).Status);Assert.True(await db.AuditLogs.AnyAsync(x=>x.OrganizationId==organizationId&&x.Action=="product.status.changed"&&x.EntityId==productId));}
        using(var crossSite=new HttpRequestMessage(HttpMethod.Put,$"/api/v1/products/{productId}/status")){crossSite.Headers.Add("Origin","https://attacker.example");crossSite.Content=JsonContent.Create(new{status="Active"});Assert.Equal(HttpStatusCode.Forbidden,(await client.SendAsync(crossSite)).StatusCode);}await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<SellerFinanceDbContext>();Assert.Equal("Archived",(await db.Products.SingleAsync(x=>x.Id==productId)).Status);}

        client.DefaultRequestHeaders.Add("X-Organization-Id","demo-organization");Assert.Equal(organizationId,(await client.GetFromJsonAsync<JsonElement>("/api/v1/session")).GetProperty("organizationId").GetString());client.DefaultRequestHeaders.Remove("X-Organization-Id");
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync($"/api/v1/products/{productId}/timeseries")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.PutAsJsonAsync($"/api/v1/products/{productId}/status",new{status="Active"})).StatusCode);

        var deletion=await client.SendAsync(new(HttpMethod.Delete,$"/api/v1/organizations/{organizationId}"){Content=JsonContent.Create(new{organizationName="E2E Updated",password})});
        Assert.Equal(HttpStatusCode.OK,deletion.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/v1/session")).StatusCode);
    }

    [Fact]
    public async Task Onboarding_Kaspi_Sync_Cost_Import_Dashboard_And_Export_Works_End_To_End()
    {
        using var client=factory.CreateClient(new(){AllowAutoRedirect=false,HandleCookies=true});var email=$"pilot-{Guid.NewGuid():N}@example.test";const string token="fixture-kaspi-token";
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/v1/auth/register",new{email,password="PilotTest123",displayName="Pilot Owner",organizationName="Pilot Flow"})).StatusCode);var session=await client.GetFromJsonAsync<JsonElement>("/api/v1/session");var organizationId=session.GetProperty("organizationId").GetString()!;

        var connectionResponse=await client.PostAsJsonAsync("/api/v1/kaspi/connections",new{displayName="Fixture Store",token});Assert.Equal(HttpStatusCode.Created,connectionResponse.StatusCode);var connectionId=(await connectionResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var syncResponse=await client.PostAsync($"/api/v1/kaspi/connections/{connectionId}/sync",null);Assert.Equal(HttpStatusCode.Accepted,syncResponse.StatusCode);var syncId=(await syncResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await new KaspiSyncWorker(factory.Services.GetRequiredService<IServiceScopeFactory>(),NullLogger<KaspiSyncWorker>.Instance).ProcessOneAsync(CancellationToken.None);
        var sync=await client.GetFromJsonAsync<JsonElement>($"/api/v1/kaspi/sync/{syncId}");Assert.Equal("Succeeded",sync.GetProperty("status").GetString());Assert.Equal(1,sync.GetProperty("importedOrders").GetInt32());

        var products=await client.GetFromJsonAsync<JsonElement>("/api/v1/products");var product=Assert.Single(products.GetProperty("items").EnumerateArray());Assert.Equal("FIXTURE-SKU",product.GetProperty("sku").GetString());
        using var multipart=new MultipartFormDataContent();using var csv=new ByteArrayContent(Encoding.UTF8.GetBytes("sku;cost;effectiveFrom\nFIXTURE-SKU;6000;2026-08-01"));csv.Headers.ContentType=new("text/csv");multipart.Add(csv,"file","costs.csv");var previewResponse=await client.PostAsync("/api/v1/costs/imports/preview",multipart);Assert.Equal(HttpStatusCode.OK,previewResponse.StatusCode);var preview=await previewResponse.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(1,preview.GetProperty("expectedChanges").GetInt32());var importId=preview.GetProperty("id").GetGuid();Assert.Equal(HttpStatusCode.OK,(await client.PostAsync($"/api/v1/costs/imports/{importId}/confirm",null)).StatusCode);

        var summary=await client.GetFromJsonAsync<JsonElement>("/api/v1/analytics/summary?dateFrom=2026-08-01&dateTo=2026-08-31");Assert.Equal(14990,summary.GetProperty("revenue").GetDecimal());Assert.Equal(100,summary.GetProperty("coveragePct").GetDecimal());Assert.False(summary.GetProperty("isPreliminary").GetBoolean());
        var exportResponse=await client.PostAsJsonAsync("/api/v1/exports",new{reportType="Products",format="csv",dateFrom="2026-08-01",dateTo="2026-08-31",completeCostsOnly=true});Assert.Equal(HttpStatusCode.Accepted,exportResponse.StatusCode);var export=await exportResponse.Content.ReadFromJsonAsync<JsonElement>();var exportId=export.GetProperty("id").GetGuid();var downloadToken=export.GetProperty("downloadToken").GetString()!;
        await new ExportWorker(factory.Services.GetRequiredService<IServiceScopeFactory>(),NullLogger<ExportWorker>.Instance).ProcessOneAsync(CancellationToken.None);var exportStatus=await client.GetFromJsonAsync<JsonElement>($"/api/v1/exports/{exportId}");Assert.Equal("Succeeded",exportStatus.GetProperty("status").GetString());Assert.Equal(1,exportStatus.GetProperty("rowCount").GetInt32());var download=await client.GetAsync($"/api/v1/exports/download/{downloadToken}");Assert.Equal(HttpStatusCode.OK,download.StatusCode);Assert.Contains("FIXTURE-SKU",await download.Content.ReadAsStringAsync());

        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<SellerFinanceDbContext>();var connection=await db.MarketplaceConnections.SingleAsync(x=>x.Id==connectionId);Assert.DoesNotContain(token,Encoding.UTF8.GetString(connection.TokenCiphertext));Assert.True(await db.AuditLogs.AnyAsync(x=>x.OrganizationId==organizationId&&x.Action=="product.cost.import.applied"));Assert.True(await db.AuditLogs.AnyAsync(x=>x.OrganizationId==organizationId&&x.Action=="export.queued"));
    }

    [Fact]
    public async Task Health_And_Protected_Routes_Start_Successfully()
    {
        using var client=factory.CreateClient(new(){AllowAutoRedirect=false});
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/health/database")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/health/ready")).StatusCode);
        var docs=await client.GetStringAsync("/api-docs");Assert.Contains("/openapi/v1.json",docs);var openApi=await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");Assert.True(openApi.GetProperty("paths").TryGetProperty("/api/v1/products",out _));Assert.True(openApi.GetProperty("paths").TryGetProperty("/api/v1/exports",out _));
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/v1/session")).StatusCode);
    }

    [Fact]
    public async Task Email_Confirmation_And_Password_Reset_Are_Audited_And_Do_Not_Enumerate_Accounts()
    {
        using var client=factory.CreateClient(new(){AllowAutoRedirect=false,HandleCookies=true});var email=$"auth-{Guid.NewGuid():N}@example.test";const string oldPassword="PilotTest123";const string newPassword="ChangedPilot123";
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/v1/auth/register",new{email,password=oldPassword,displayName="Auth User",organizationName="Auth E2E"})).StatusCode);var session=await client.GetFromJsonAsync<JsonElement>("/api/v1/session");var organizationId=session.GetProperty("organizationId").GetString()!;string userId;string confirmationToken;string resetToken;
        await using(var scope=factory.Services.CreateAsyncScope()){var users=scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();var user=(await users.FindByEmailAsync(email))!;user.EmailConfirmed=false;Assert.True((await users.UpdateAsync(user)).Succeeded);userId=user.Id;confirmationToken=await users.GenerateEmailConfirmationTokenAsync(user);resetToken=await users.GeneratePasswordResetTokenAsync(user);}
        var confirmationUrl=$"/api/v1/auth/confirm-email?userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(confirmationToken)}";Assert.Equal(HttpStatusCode.OK,(await client.GetAsync(confirmationUrl)).StatusCode);
        var knownForgot=await client.PostAsJsonAsync("/api/v1/auth/forgot-password",new{email});var unknownForgot=await client.PostAsJsonAsync("/api/v1/auth/forgot-password",new{email=$"missing-{Guid.NewGuid():N}@example.test"});Assert.Equal(HttpStatusCode.OK,knownForgot.StatusCode);Assert.Equal(await knownForgot.Content.ReadAsStringAsync(),await unknownForgot.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NoContent,(await client.PostAsJsonAsync("/api/v1/auth/reset-password",new{email,token=resetToken,newPassword})).StatusCode);Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/v1/auth/reset-password",new{email,token=resetToken,newPassword="AnotherPilot123"})).StatusCode);
        await client.PostAsync("/api/v1/auth/logout",null);Assert.Equal(HttpStatusCode.Unauthorized,(await client.PostAsJsonAsync("/api/v1/auth/login",new{email,password=oldPassword,rememberMe=false})).StatusCode);Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/v1/auth/login",new{email,password=newPassword,rememberMe=false})).StatusCode);
        await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<SellerFinanceDbContext>();Assert.True(await db.AuditLogs.AnyAsync(x=>x.OrganizationId==organizationId&&x.Action=="auth.email.confirmed"&&x.UserId==userId));Assert.True(await db.AuditLogs.AnyAsync(x=>x.OrganizationId==organizationId&&x.Action=="auth.password.reset.completed"&&x.UserId==userId));}
    }

    [Fact]
    public async Task Required_Email_Without_Smtp_Fails_Readiness_And_Registration()
    {
        await using var isolated=new MissingSmtpApplicationFactory();using var client=isolated.CreateClient(new(){AllowAutoRedirect=false});Assert.Equal(HttpStatusCode.ServiceUnavailable,(await client.GetAsync("/health/ready")).StatusCode);var registration=await client.PostAsJsonAsync("/api/v1/auth/register",new{email=$"smtp-{Guid.NewGuid():N}@example.test",password="PilotTest123",displayName="SMTP",organizationName="SMTP Org"});Assert.Equal(HttpStatusCode.ServiceUnavailable,registration.StatusCode);
    }

    [Fact]
    public async Task Authenticated_User_Can_Create_And_Switch_To_An_Isolated_Organization()
    {
        using var client=factory.CreateClient(new(){AllowAutoRedirect=false,HandleCookies=true});var email=$"multi-{Guid.NewGuid():N}@example.test";Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/v1/auth/register",new{email,password="PilotTest123",displayName="Multi",organizationName="First Org"})).StatusCode);var first=await client.GetFromJsonAsync<JsonElement>("/api/v1/session");var firstId=first.GetProperty("organizationId").GetString()!;
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/v1/organizations",new{name="X"})).StatusCode);var createdResponse=await client.PostAsJsonAsync("/api/v1/organizations",new{name="Second Org"});Assert.Equal(HttpStatusCode.Created,createdResponse.StatusCode);var created=(await createdResponse.Content.ReadFromJsonAsync<JsonElement>());var secondId=created.GetProperty("id").GetString()!;var organizations=await client.GetFromJsonAsync<JsonElement[]>("/api/v1/organizations");Assert.Contains(organizations!,x=>x.GetProperty("id").GetString()==secondId&&x.GetProperty("role").GetString()=="Owner");
        var second=await client.GetFromJsonAsync<JsonElement>("/api/v1/session");Assert.Equal("Second Org",second.GetProperty("organizationName").GetString());Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/v1/products")).GetProperty("items").EnumerateArray());Assert.Equal(HttpStatusCode.NotFound,(await client.PostAsync($"/api/v1/organizations/{Guid.NewGuid():N}/select",null)).StatusCode);Assert.Equal(HttpStatusCode.NoContent,(await client.PostAsync($"/api/v1/organizations/{firstId}/select",null)).StatusCode);Assert.Equal("First Org",(await client.GetFromJsonAsync<JsonElement>("/api/v1/session")).GetProperty("organizationName").GetString());Assert.Equal(HttpStatusCode.NoContent,(await client.PostAsync("/api/v1/auth/logout",null)).StatusCode);Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/v1/auth/login",new{email,password="PilotTest123",rememberMe=true})).StatusCode);Assert.Equal(firstId,(await client.GetFromJsonAsync<JsonElement>("/api/v1/session")).GetProperty("organizationId").GetString());await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<SellerFinanceDbContext>();var userId=await db.Users.Where(x=>x.Email==email).Select(x=>x.Id).SingleAsync();Assert.Equal(firstId,await db.UserClaims.Where(x=>x.UserId==userId&&x.ClaimType==TenantSecurity.ActiveOrganizationClaim).Select(x=>x.ClaimValue).SingleAsync());Assert.True(await db.Subscriptions.AnyAsync(x=>x.OrganizationId==secondId&&x.Status==SubscriptionStatus.Trialing));Assert.Equal(3,await db.NotificationRules.CountAsync(x=>x.OrganizationId==secondId));Assert.True(await db.AuditLogs.AnyAsync(x=>x.OrganizationId==firstId&&x.Action=="organization.selected"));
    }

    [Fact]
    public async Task Owner_Manages_Members_Invitations_And_Role_Boundaries()
    {
        using var ownerClient=factory.CreateClient(new(){AllowAutoRedirect=false,HandleCookies=true});var ownerEmail=$"owner-{Guid.NewGuid():N}@example.test";const string password="PilotTest123";
        Assert.Equal(HttpStatusCode.OK,(await ownerClient.PostAsJsonAsync("/api/v1/auth/register",new{email=ownerEmail,password,displayName="Owner",organizationName="Members E2E"})).StatusCode);var session=await ownerClient.GetFromJsonAsync<JsonElement>("/api/v1/session");var organizationId=session.GetProperty("organizationId").GetString()!;var ownerId=session.GetProperty("userId").GetString()!;
        var memberEmail=$"viewer-{Guid.NewGuid():N}@example.test";string memberId;
        const string roleProductId="role-product";await using(var scope=factory.Services.CreateAsyncScope()){var users=scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();var member=new AppUser{UserName=memberEmail,Email=memberEmail,EmailConfirmed=true,DisplayName="Viewer"};Assert.True((await users.CreateAsync(member,password)).Succeeded);memberId=member.Id;var db=scope.ServiceProvider.GetRequiredService<SellerFinanceDbContext>();db.OrganizationUsers.Add(new(){OrganizationId=organizationId,UserId=memberId,Role=OrganizationRole.Analyst,JoinedAt=DateTimeOffset.UtcNow});db.Products.Add(new(){Id=roleProductId,OrganizationId=organizationId,Sku="ROLE",Name="Role product"});var subscription=await db.Subscriptions.SingleAsync(x=>x.OrganizationId==organizationId);subscription.Plan=SubscriptionPlan.Business;await db.SaveChangesAsync();}

        var members=await ownerClient.GetFromJsonAsync<JsonElement[]>($"/api/v1/organizations/{organizationId}/members");Assert.Equal(2,members!.Length);
        Assert.Equal(HttpStatusCode.NoContent,(await ownerClient.PutAsJsonAsync($"/api/v1/organizations/{organizationId}/members/{memberId}/role",new{role="Viewer"})).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await ownerClient.PutAsJsonAsync($"/api/v1/organizations/{organizationId}/members/{ownerId}/role",new{role="Admin"})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await ownerClient.DeleteAsync($"/api/v1/organizations/{organizationId}/members/{ownerId}")).StatusCode);

        using var viewerClient=factory.CreateClient(new(){AllowAutoRedirect=false,HandleCookies=true});Assert.Equal(HttpStatusCode.OK,(await viewerClient.PostAsJsonAsync("/api/v1/auth/login",new{email=memberEmail,password,rememberMe=false})).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await viewerClient.DeleteAsync($"/api/v1/organizations/{organizationId}/members/{ownerId}")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await viewerClient.PutAsJsonAsync($"/api/v1/products/{roleProductId}/status",new{status="Archived"})).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await viewerClient.PostAsync("/api/v1/telegram/test",null)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await viewerClient.PostAsync($"/api/v1/kaspi/connections/{Guid.NewGuid()}/sync",null)).StatusCode);

        var invitedEmail=$"invite-{Guid.NewGuid():N}@example.test";using var invitationRequest=new HttpRequestMessage(HttpMethod.Post,$"/api/v1/organizations/{organizationId}/members"){Content=JsonContent.Create(new{email=invitedEmail,role="Analyst"})};invitationRequest.Headers.Host="attacker.example";var invitation=await ownerClient.SendAsync(invitationRequest);Assert.Equal(HttpStatusCode.OK,invitation.StatusCode);var safeInvitation=await invitation.Content.ReadFromJsonAsync<JsonElement>();Assert.StartsWith("http://localhost/?invitationToken=",safeInvitation.GetProperty("invitationUrl").GetString());Assert.Equal(HttpStatusCode.Conflict,(await ownerClient.PostAsJsonAsync($"/api/v1/organizations/{organizationId}/members",new{email=invitedEmail,role="Viewer"})).StatusCode);var pending=await ownerClient.GetFromJsonAsync<JsonElement[]>($"/api/v1/organizations/{organizationId}/invitations");Assert.NotNull(pending);Assert.Single(pending);var invitationId=pending[0].GetProperty("id").GetGuid();Assert.Equal(HttpStatusCode.NoContent,(await ownerClient.DeleteAsync($"/api/v1/organizations/{organizationId}/invitations/{invitationId}")).StatusCode);
        var acceptingEmail=$"accept-{Guid.NewGuid():N}@example.test";var acceptingInvitation=await (await ownerClient.PostAsJsonAsync($"/api/v1/organizations/{organizationId}/members",new{email=acceptingEmail,role="Analyst"})).Content.ReadFromJsonAsync<JsonElement>();var invitationToken=acceptingInvitation.GetProperty("invitationToken").GetString()!;using var acceptingClient=factory.CreateClient(new(){AllowAutoRedirect=false,HandleCookies=true});Assert.Equal(HttpStatusCode.OK,(await acceptingClient.PostAsJsonAsync("/api/v1/auth/register",new{email=acceptingEmail,password,displayName="Accepting",organizationName="Temporary Org"})).StatusCode);var accepted=await acceptingClient.PostAsJsonAsync("/api/v1/invitations/accept",new{token=invitationToken});Assert.Equal(HttpStatusCode.OK,accepted.StatusCode);var targetSession=await acceptingClient.GetFromJsonAsync<JsonElement>("/api/v1/session");Assert.Equal("Analyst",targetSession.GetProperty("role").GetString());
        Assert.Equal(HttpStatusCode.NoContent,(await ownerClient.DeleteAsync($"/api/v1/organizations/{organizationId}/members/{memberId}")).StatusCode);
    }
}

public sealed class MissingSmtpApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var databaseName=$"seller-finance-no-smtp-{Guid.NewGuid():N}";builder.UseEnvironment("Testing");builder.UseSetting("TEST_USE_INMEMORY","true");builder.UseSetting("TOKEN_ENCRYPTION_KEY",Convert.ToBase64String(new byte[32]));builder.UseSetting("EMAIL_CONFIRMATION_REQUIRED","true");builder.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>{{"TEST_USE_INMEMORY","true"},{"TOKEN_ENCRYPTION_KEY",Convert.ToBase64String(new byte[32])},{"EMAIL_CONFIRMATION_REQUIRED","true"}}));builder.ConfigureServices(services=>{services.RemoveAll<DbContextOptions<SellerFinanceDbContext>>();services.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<SellerFinanceDbContext>>();services.RemoveAll<SellerFinanceDbContext>();services.AddDbContext<SellerFinanceDbContext>(options=>options.UseInMemoryDatabase(databaseName));});
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
            services.AddHttpClient<KaspiClient>(client=>{client.BaseAddress=new Uri("https://kaspi.kz/shop/api/v2/");client.Timeout=TimeSpan.FromSeconds(5);}).ConfigurePrimaryHttpMessageHandler(()=>new KaspiFixtureHandler());
        });
    }

    private sealed class KaspiFixtureHandler:HttpMessageHandler
    {
        private const string Orders="""{"data":[{"type":"orders","id":"fixture-order","attributes":{"code":"FIXTURE-001","totalPrice":14990,"status":"COMPLETED","creationDate":1786453200000,"completionDate":1786539600000,"paymentMode":"PREPAID","deliveryCostForSeller":750}}]}""";
        private const string Entries="""{"data":[{"type":"orderentries","id":"fixture-line","attributes":{"quantity":2,"totalPrice":14990,"basePrice":8000,"deliveryCost":500}}]}""";
        private const string Product="""{"data":{"type":"masterproducts","id":"fixture-product","attributes":{"code":"FIXTURE-SKU","name":"Fixture Product","category":"Home"}}}""";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken){var path=request.RequestUri!.AbsolutePath;var body=path.EndsWith("/product")?Product:path.EndsWith("/entries")?Entries:Orders;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/vnd.api+json")});}
    }
}
