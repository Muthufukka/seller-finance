using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace SellerFinance.Api;

public sealed record TenantMembership(string OrganizationId, OrganizationRole Role);

public static class TenantSecurity
{
    public static async Task<TenantMembership?> ResolveAsync(HttpContext context, SellerFinanceDbContext db)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (String.IsNullOrWhiteSpace(userId)) return null;
        var requested = context.Request.Headers["X-Organization-Id"].ToString();
        var query = db.OrganizationUsers.AsNoTracking().Where(x => x.UserId == userId && x.JoinedAt != null);
        var membership = String.IsNullOrWhiteSpace(requested)
            ? await query.OrderBy(x => x.JoinedAt).FirstOrDefaultAsync()
            : await query.SingleOrDefaultAsync(x => x.OrganizationId == requested);
        return membership is null ? null : new(membership.OrganizationId, membership.Role);
    }

    public static bool CanWrite(this TenantMembership membership) => membership.Role is OrganizationRole.Owner or OrganizationRole.Admin or OrganizationRole.Analyst;
    public static bool CanManageMembers(this TenantMembership membership) => membership.Role is OrganizationRole.Owner or OrganizationRole.Admin;
    public static string Tenant(this HttpContext context) => ((TenantMembership)context.Items["membership"]!).OrganizationId;
    public static TenantMembership Membership(this HttpContext context) => (TenantMembership)context.Items["membership"]!;
}

public static class AuditWriter
{
    public static void Add(SellerFinanceDbContext db, HttpContext context, string action, string entityType, string? entityId = null, string metadataSafe = "{}") =>
        db.AuditLogs.Add(new()
        {
            Id = Guid.NewGuid(), OrganizationId = context.Items.TryGetValue("membership", out var value) ? ((TenantMembership)value!).OrganizationId : null,
            UserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier), Action = action, EntityType = entityType, EntityId = entityId, MetadataSafe = metadataSafe
        });
}

public static class TokenTools
{
    public static string CreateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+','-').Replace('/','_').TrimEnd('=');
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
