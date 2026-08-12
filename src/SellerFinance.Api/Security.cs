using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace SellerFinance.Api;

public static class RequestOriginSecurity
{
    private static readonly HashSet<string> SafeMethods=["GET","HEAD","OPTIONS","TRACE"];
    public static bool IsAllowed(HttpContext context,IConfiguration configuration)
    {
        if(SafeMethods.Contains(context.Request.Method)||context.Request.Path.Equals("/api/v1/telegram/webhook"))return true;
        if(context.Request.Headers.TryGetValue("Sec-Fetch-Site",out var fetchSite)&&fetchSite.Any(x=>String.Equals(x,"cross-site",StringComparison.OrdinalIgnoreCase)))return false;
        var origin=context.Request.Headers.Origin.ToString();if(String.IsNullOrWhiteSpace(origin))return true;
        if(!Uri.TryCreate(origin,UriKind.Absolute,out var supplied))return false;
        var configured=configuration["PUBLIC_BASE_URL"];if(Uri.TryCreate(configured,UriKind.Absolute,out var expected)&&SameAuthority(supplied,expected))return true;
        if(!context.Request.Host.HasValue||String.IsNullOrWhiteSpace(context.Request.Scheme))return false;var requestOrigin=new UriBuilder(context.Request.Scheme,context.Request.Host.Host,context.Request.Host.Port??-1).Uri;return SameAuthority(supplied,requestOrigin);
    }
    private static bool SameAuthority(Uri left,Uri right)=>String.Equals(left.Scheme,right.Scheme,StringComparison.OrdinalIgnoreCase)&&String.Equals(left.Host,right.Host,StringComparison.OrdinalIgnoreCase)&&left.Port==right.Port;
}

public sealed record TenantMembership(string OrganizationId, OrganizationRole Role);

public static class TenantSecurity
{
    public const string ActiveOrganizationClaim="seller_finance:organization_id";
    public static async Task<TenantMembership?> ResolveAsync(HttpContext context, SellerFinanceDbContext db)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (String.IsNullOrWhiteSpace(userId)) return null;
        var requested = context.User.FindFirstValue(ActiveOrganizationClaim);
        var now=DateTimeOffset.UtcNow;var query = from memberRow in db.OrganizationUsers.AsNoTracking() join organization in db.Organizations.AsNoTracking() on memberRow.OrganizationId equals organization.Id join subscription in db.Subscriptions.AsNoTracking() on organization.Id equals subscription.OrganizationId where memberRow.UserId==userId&&memberRow.JoinedAt!=null&&organization.Status=="Active"&&(subscription.Status==SubscriptionStatus.Active||subscription.Status==SubscriptionStatus.Trialing)&&subscription.PeriodEnd>now select memberRow;
        var membership = String.IsNullOrWhiteSpace(requested)
            ? await query.OrderBy(x => x.JoinedAt).FirstOrDefaultAsync()
            : await query.SingleOrDefaultAsync(x => x.OrganizationId == requested);
        return membership is null ? null : new(membership.OrganizationId, membership.Role);
    }

    public static Claim ActiveOrganization(string organizationId)=>new(ActiveOrganizationClaim,organizationId);
    public static async Task SetActiveOrganizationAsync(UserManager<AppUser> users,AppUser user,string organizationId)
    {
        var existing=(await users.GetClaimsAsync(user)).Where(x=>x.Type==ActiveOrganizationClaim).ToArray();
        if(existing.Length==1&&existing[0].Value==organizationId)return;
        if(existing.Length>0){var removed=await users.RemoveClaimsAsync(user,existing);if(!removed.Succeeded)throw new InvalidOperationException("Unable to replace active organization claim");}
        var added=await users.AddClaimAsync(user,ActiveOrganization(organizationId));if(!added.Succeeded)throw new InvalidOperationException("Unable to persist active organization claim");
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
    public static void AddSystem(SellerFinanceDbContext db,HttpContext context,string organizationId,string action,string entityType,string? entityId=null,string metadataSafe="{}")=>db.AuditLogs.Add(new(){Id=Guid.NewGuid(),OrganizationId=organizationId,UserId=context.User.FindFirstValue(ClaimTypes.NameIdentifier),Action=action,EntityType=entityType,EntityId=entityId,MetadataSafe=metadataSafe});
}

public static class TokenTools
{
    public static string CreateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+','-').Replace('/','_').TrimEnd('=');
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public static class SaasSecurity
{
    public static bool IsAdmin(ClaimsPrincipal user,IConfiguration configuration)=>!String.IsNullOrWhiteSpace(configuration["SAAS_ADMIN_EMAIL"])&&String.Equals(user.Identity?.Name,configuration["SAAS_ADMIN_EMAIL"],StringComparison.OrdinalIgnoreCase);
}

public static class FeatureFlags
{
    public static readonly string[] Known=["KaspiSync","TelegramNotifications","AdvancedExports"];
    public static bool IsKnown(string key)=>Known.Contains(key,StringComparer.OrdinalIgnoreCase);
    public static async Task<bool> IsEnabledAsync(SellerFinanceDbContext db,string organizationId,string key,CancellationToken ct=default)=>(await db.OrganizationFeatureFlags.AsNoTracking().Where(x=>x.OrganizationId==organizationId&&x.Key==key).Select(x=>(bool?)x.Enabled).SingleOrDefaultAsync(ct))??true;
}

public static class PlanLimits
{
    public static int MaxMembers(SubscriptionPlan plan)=>plan switch{SubscriptionPlan.Trial=>2,SubscriptionPlan.Start=>3,SubscriptionPlan.Pro=>10,SubscriptionPlan.Business=>30,_=>2};
    public static int MaxStores(SubscriptionPlan plan)=>plan switch{SubscriptionPlan.Trial or SubscriptionPlan.Start=>1,SubscriptionPlan.Pro=>3,SubscriptionPlan.Business=>10,_=>1};
    public static DateTimeOffset InitialHistoryFrom(SubscriptionPlan plan,DateTimeOffset now)=>plan switch{SubscriptionPlan.Trial=>now.AddDays(-90),SubscriptionPlan.Start=>now.AddDays(-365),_=>DateTimeOffset.UnixEpoch};
}

public static class Subscriptions
{
    public static Task<SubscriptionEntity> GetAsync(SellerFinanceDbContext db,string organizationId,CancellationToken ct=default)=>db.Subscriptions.SingleAsync(x=>x.OrganizationId==organizationId,ct);
}
