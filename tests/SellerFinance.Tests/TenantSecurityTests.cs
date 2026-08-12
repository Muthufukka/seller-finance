using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SellerFinance.Api;

namespace SellerFinance.Tests;

public sealed class TenantSecurityTests
{
    [Fact]
    public async Task ResolveAsync_Rejects_CrossTenant_Selector()
    {
        await using var db = CreateDb();
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
        db.OrganizationUsers.AddRange(Member("user-a", "org-a", OrganizationRole.Analyst), Member("user-b", "org-b"));
        await db.SaveChangesAsync();

        var result = await TenantSecurity.ResolveAsync(Context("user-a", "org-a"), db);

        Assert.NotNull(result);
        Assert.Equal("org-a", result.OrganizationId);
        Assert.Equal(OrganizationRole.Analyst, result.Role);
    }

    private static SellerFinanceDbContext CreateDb() => new(new DbContextOptionsBuilder<SellerFinanceDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static OrganizationUserEntity Member(string userId, string organizationId, OrganizationRole role = OrganizationRole.Viewer) => new()
    {
        UserId = userId, OrganizationId = organizationId, Role = role, JoinedAt = DateTimeOffset.UtcNow
    };

    private static DefaultHttpContext Context(string userId, string organizationId)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test"));
        context.Request.Headers["X-Organization-Id"] = organizationId;
        return context;
    }
}
