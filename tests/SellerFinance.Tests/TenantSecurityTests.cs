using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SellerFinance.Api;

namespace SellerFinance.Tests;

public sealed class TenantSecurityTests
{
    [Fact]
    public async Task ResolveAsync_Rejects_CrossTenant_Selector()
    {
        await using var db = CreateDb();
        AddOrganization(db,"org-a");AddOrganization(db,"org-b");
        db.OrganizationUsers.Add(Member("user-a", "org-a"));
        await db.SaveChangesAsync();
        var context = Context("user-a", "org-b");

        var result = await TenantSecurity.ResolveAsync(context, db);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_Returns_Only_Authenticated_Membership()
    {
        await using var db = CreateDb();
        AddOrganization(db,"org-a");AddOrganization(db,"org-b");
        db.OrganizationUsers.AddRange(Member("user-a", "org-a", OrganizationRole.Analyst), Member("user-b", "org-b"));
        await db.SaveChangesAsync();

        var result = await TenantSecurity.ResolveAsync(Context("user-a", "org-a"), db);

        Assert.NotNull(result);
        Assert.Equal("org-a", result.OrganizationId);
        Assert.Equal(OrganizationRole.Analyst, result.Role);
    }

    [Fact]
    public async Task ResolveAsync_Rejects_Suspended_Organization()
    {
        await using var db=CreateDb();AddOrganization(db,"org","Suspended");db.OrganizationUsers.Add(Member("user","org"));await db.SaveChangesAsync();
        Assert.Null(await TenantSecurity.ResolveAsync(Context("user","org"),db));
    }

    [Fact]
    public async Task ResolveAsync_Rejects_Expired_Subscription()
    {
        await using var db=CreateDb();db.Organizations.Add(new(){Id="org",Name="org",Status="Active"});db.Subscriptions.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",Status=SubscriptionStatus.Expired,PeriodEnd=DateTimeOffset.UtcNow.AddMinutes(-1)});db.OrganizationUsers.Add(Member("user","org"));await db.SaveChangesAsync();Assert.Null(await TenantSecurity.ResolveAsync(Context("user","org"),db));
    }

    [Fact]
    public async Task Feature_Flag_Defaults_Enabled_And_Stored_False_Wins()
    {
        await using var db=CreateDb();Assert.True(await FeatureFlags.IsEnabledAsync(db,"org","KaspiSync"));db.OrganizationFeatureFlags.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",Key="KaspiSync",Enabled=false,UpdatedByUserId="admin"});await db.SaveChangesAsync();Assert.False(await FeatureFlags.IsEnabledAsync(db,"org","KaspiSync"));Assert.False(FeatureFlags.IsKnown("unknown"));
    }

    [Fact]
    public void Saas_Admin_Requires_Exact_Configured_Authenticated_Email()
    {
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"SAAS_ADMIN_EMAIL","admin@example.test"}}).Build();Assert.True(SaasSecurity.IsAdmin(Context("id","org","admin@example.test").User,config));Assert.False(SaasSecurity.IsAdmin(Context("id","org","user@example.test").User,config));
    }

    [Fact]
    public void Unsafe_Cross_Site_Request_Is_Rejected_While_Same_Origin_Is_Allowed()
    {
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"PUBLIC_BASE_URL","https://seller.example"}}).Build();var cross=new DefaultHttpContext();cross.Request.Method="DELETE";cross.Request.Headers.Origin="https://attacker.example";Assert.False(RequestOriginSecurity.IsAllowed(cross,configuration));var fetch=new DefaultHttpContext();fetch.Request.Method="POST";fetch.Request.Headers["Sec-Fetch-Site"]="cross-site";Assert.False(RequestOriginSecurity.IsAllowed(fetch,configuration));var same=new DefaultHttpContext();same.Request.Method="PUT";same.Request.Headers.Origin="https://seller.example";Assert.True(RequestOriginSecurity.IsAllowed(same,configuration));
    }

    [Fact]
    public void Telegram_Webhook_Uses_Separate_Secret_Gate_And_Is_Origin_Exempt()
    {
        var context=new DefaultHttpContext();context.Request.Method="POST";context.Request.Path="/api/v1/telegram/webhook";context.Request.Headers.Origin="https://api.telegram.org";Assert.True(RequestOriginSecurity.IsAllowed(context,new ConfigurationBuilder().Build()));
    }

    private static SellerFinanceDbContext CreateDb() => new(new DbContextOptionsBuilder<SellerFinanceDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static OrganizationUserEntity Member(string userId, string organizationId, OrganizationRole role = OrganizationRole.Viewer) => new()
    {
        UserId = userId, OrganizationId = organizationId, Role = role, JoinedAt = DateTimeOffset.UtcNow
    };
    private static void AddOrganization(SellerFinanceDbContext db,string id,string status="Active"){db.Organizations.Add(new(){Id=id,Name=id,Status=status});db.Subscriptions.Add(new(){Id=Guid.NewGuid(),OrganizationId=id,Status=SubscriptionStatus.Trialing,PeriodEnd=DateTimeOffset.UtcNow.AddDays(14)});}

    private static DefaultHttpContext Context(string userId, string organizationId,string? email=null)
    {
        var context = new DefaultHttpContext();
        var claims=new List<Claim>{new(ClaimTypes.NameIdentifier,userId)};if(email is not null)claims.Add(new(ClaimTypes.Name,email));context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        context.Request.Headers["X-Organization-Id"] = organizationId;
        return context;
    }
}
