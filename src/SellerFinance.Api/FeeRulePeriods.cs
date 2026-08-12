using Microsoft.EntityFrameworkCore;

namespace SellerFinance.Api;

public static class FeeRulePeriods
{
    public static async Task<bool> OverlapsAsync(
        SellerFinanceDbContext db,string organizationId,FeeRuleScope scope,string? productId,string? category,
        DateOnly effectiveFrom,DateOnly? effectiveTo,Guid? excludeId=null,CancellationToken ct=default)
    {
        var query=db.FeeRules.AsNoTracking().Where(x=>x.OrganizationId==organizationId&&x.Scope==scope);
        query=scope switch
        {
            FeeRuleScope.Product=>query.Where(x=>x.ProductId==productId),
            FeeRuleScope.Category=>query.Where(x=>x.Category==category),
            _=>query
        };
        if(excludeId.HasValue)query=query.Where(x=>x.Id!=excludeId.Value);
        return await query.AnyAsync(x=>(!effectiveTo.HasValue||x.EffectiveFrom<=effectiveTo.Value)&&(!x.EffectiveTo.HasValue||x.EffectiveTo.Value>=effectiveFrom),ct);
    }
}
