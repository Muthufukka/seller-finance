using Microsoft.EntityFrameworkCore;

namespace SellerFinance.Api;

public enum OrganizationSettingsFailure { None, NotFound, Forbidden, InvalidName, InvalidTimeZone, InvalidCurrency }

public sealed record OrganizationSettingsResult(OrganizationSettingsFailure Failure, OrganizationEntity? Organization = null);

public static class OrganizationSettings
{
    private static readonly HashSet<string> SupportedCurrencies = new(StringComparer.OrdinalIgnoreCase) { "KZT" };

    public static async Task<OrganizationSettingsResult> UpdateAsync(
        SellerFinanceDbContext db,
        string organizationId,
        TenantMembership membership,
        string name,
        string timeZone,
        string currency,
        bool allocateOrganizationExpenses=false,
        CancellationToken ct = default)
    {
        if (membership.OrganizationId != organizationId || membership.Role is not (OrganizationRole.Owner or OrganizationRole.Admin))
            return new(OrganizationSettingsFailure.Forbidden);

        name = name.Trim();
        timeZone = timeZone.Trim();
        currency = currency.Trim().ToUpperInvariant();
        if (name.Length is < 2 or > 120) return new(OrganizationSettingsFailure.InvalidName);
        if (!SupportedCurrencies.Contains(currency)) return new(OrganizationSettingsFailure.InvalidCurrency);
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(timeZone); }
        catch (TimeZoneNotFoundException) { return new(OrganizationSettingsFailure.InvalidTimeZone); }
        catch (InvalidTimeZoneException) { return new(OrganizationSettingsFailure.InvalidTimeZone); }

        var organization = await db.Organizations.SingleOrDefaultAsync(x => x.Id == organizationId, ct);
        if (organization is null) return new(OrganizationSettingsFailure.NotFound);
        organization.Name = name;
        organization.TimeZone = timeZone;
        organization.Currency = currency;
        organization.AllocateOrganizationExpenses = allocateOrganizationExpenses;
        return new(OrganizationSettingsFailure.None, organization);
    }
}
