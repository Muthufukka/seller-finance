using Microsoft.EntityFrameworkCore;

namespace SellerFinance.Api;

public enum OrganizationDeletionFailure { None, NotFound, Forbidden, ConfirmationMismatch, ActiveSync }

public sealed record OrganizationDeletionResult(OrganizationDeletionFailure Failure, bool UserDeleted = false, int DeletedOrders = 0, int DeletedProducts = 0);

public static class OrganizationDeletion
{
    public static async Task<OrganizationDeletionResult> DeleteAsync(
        SellerFinanceDbContext db,
        string organizationId,
        string currentUserId,
        TenantMembership membership,
        string confirmation,
        CancellationToken ct = default)
    {
        if (membership.OrganizationId != organizationId || membership.Role != OrganizationRole.Owner)
            return new(OrganizationDeletionFailure.Forbidden);

        var organization = await db.Organizations.SingleOrDefaultAsync(x => x.Id == organizationId, ct);
        if (organization is null) return new(OrganizationDeletionFailure.NotFound);
        if (!String.Equals(organization.Name, confirmation.Trim(), StringComparison.Ordinal))
            return new(OrganizationDeletionFailure.ConfirmationMismatch);
        if (await db.SyncJobs.AnyAsync(x => x.OrganizationId == organizationId &&
            (x.Status == SyncJobStatus.Queued || x.Status == SyncJobStatus.Running || x.Status == SyncJobStatus.RetryScheduled), ct))
            return new(OrganizationDeletionFailure.ActiveSync);

        var orderIds = await db.Orders.Where(x => x.OrganizationId == organizationId).Select(x => x.Id).ToArrayAsync(ct);
        var orderLineIds = await db.OrderLines.Where(x => orderIds.Contains(x.OrderId)).Select(x => x.Id).ToArrayAsync(ct);
        var importJobIds = await db.CostImportJobs.Where(x => x.OrganizationId == organizationId).Select(x => x.Id).ToArrayAsync(ct);
        var deletedProducts = await db.Products.CountAsync(x => x.OrganizationId == organizationId, ct);

        db.ActualFees.RemoveRange(db.ActualFees.Where(x => x.OrganizationId == organizationId || orderLineIds.Contains(x.OrderLineId)));
        db.OrderLines.RemoveRange(db.OrderLines.Where(x => orderIds.Contains(x.OrderId)));
        db.Orders.RemoveRange(db.Orders.Where(x => x.OrganizationId == organizationId));
        db.SyncJobs.RemoveRange(db.SyncJobs.Where(x => x.OrganizationId == organizationId));
        db.MarketplaceConnections.RemoveRange(db.MarketplaceConnections.Where(x => x.OrganizationId == organizationId));
        db.ProductCostHistory.RemoveRange(db.ProductCostHistory.Where(x => x.OrganizationId == organizationId));
        db.CostImportRows.RemoveRange(db.CostImportRows.Where(x => importJobIds.Contains(x.ImportJobId)));
        db.CostImportJobs.RemoveRange(db.CostImportJobs.Where(x => x.OrganizationId == organizationId));
        db.FeeRules.RemoveRange(db.FeeRules.Where(x => x.OrganizationId == organizationId));
        db.Expenses.RemoveRange(db.Expenses.Where(x => x.OrganizationId == organizationId));
        db.ExportJobs.RemoveRange(db.ExportJobs.Where(x => x.OrganizationId == organizationId));
        db.TelegramConnections.RemoveRange(db.TelegramConnections.Where(x => x.OrganizationId == organizationId));
        db.NotificationDeliveries.RemoveRange(db.NotificationDeliveries.Where(x => x.OrganizationId == organizationId));
        db.NotificationRules.RemoveRange(db.NotificationRules.Where(x => x.OrganizationId == organizationId));
        db.OrganizationFeatureFlags.RemoveRange(db.OrganizationFeatureFlags.Where(x => x.OrganizationId == organizationId));
        db.Subscriptions.RemoveRange(db.Subscriptions.Where(x => x.OrganizationId == organizationId));
        db.OrganizationInvitations.RemoveRange(db.OrganizationInvitations.Where(x => x.OrganizationId == organizationId));
        db.AuditLogs.RemoveRange(db.AuditLogs.Where(x => x.OrganizationId == organizationId));
        db.OrganizationUsers.RemoveRange(db.OrganizationUsers.Where(x => x.OrganizationId == organizationId));
        db.Products.RemoveRange(db.Products.Where(x => x.OrganizationId == organizationId));
        db.Organizations.Remove(organization);

        var deleteUser = !await db.OrganizationUsers.AsNoTracking().AnyAsync(x => x.UserId == currentUserId && x.OrganizationId != organizationId && x.JoinedAt != null, ct);
        if (deleteUser)
        {
            var user = await db.Users.SingleOrDefaultAsync(x => x.Id == currentUserId, ct);
            if (user is not null) db.Users.Remove(user);
        }

        db.AuditLogs.Add(new()
        {
            Id = Guid.NewGuid(), Action = "privacy.organization.deleted", EntityType = "OrganizationHash",
            EntityId = TokenTools.Hash(organizationId), MetadataSafe = "{}"
        });
        await db.SaveChangesAsync(ct);
        return new(OrganizationDeletionFailure.None, deleteUser, orderIds.Length, deletedProducts);
    }
}
