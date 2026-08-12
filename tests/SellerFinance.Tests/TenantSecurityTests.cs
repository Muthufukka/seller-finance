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
        db.Organizations.AddRange(Organization("org-a"),Organization("org-b"));
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
        db.Organizations.AddRange(Organization("org-a"),Organization("org-b"));
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
        await using var db=CreateDb();db.Organizations.Add(new(){Id="org",Name="org",Status="Suspended"});db.OrganizationUsers.Add(Member("user","org"));await db.SaveChangesAsync();
        Assert.Null(await TenantSecurity.ResolveAsync(Context("user","org"),db));
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

    private static SellerFinanceDbContext CreateDb() => new(new DbContextOptionsBuilder<SellerFinanceDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static OrganizationUserEntity Member(string userId, string organizationId, OrganizationRole role = OrganizationRole.Viewer) => new()
    {
        UserId = userId, OrganizationId = organizationId, Role = role, JoinedAt = DateTimeOffset.UtcNow
    };
    private static OrganizationEntity Organization(string id)=>new(){Id=id,Name=id,Status="Active"};

    private static DefaultHttpContext Context(string userId, string organizationId,string? email=null)
    {
        var context = new DefaultHttpContext();
        var claims=new List<Claim>{new(ClaimTypes.NameIdentifier,userId)};if(email is not null)claims.Add(new(ClaimTypes.Name,email));context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        context.Request.Headers["X-Organization-Id"] = organizationId;
        return context;
    }
}
