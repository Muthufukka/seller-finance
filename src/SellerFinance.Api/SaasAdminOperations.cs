using Microsoft.EntityFrameworkCore;

namespace SellerFinance.Api;

public enum SyncRetryFailure { None, NotFound, NotRetryable, OrganizationDisabled, AlreadyRunning }
public sealed record SyncRetryResult(SyncJobEntity? Job,SyncRetryFailure Failure);

public static class SaasAdminOperations
{
    public static async Task<SyncRetryResult> RetrySyncAsync(SellerFinanceDbContext db,Guid sourceId,CancellationToken ct=default)
    {
        var source=await db.SyncJobs.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==sourceId,ct);if(source is null)return new(null,SyncRetryFailure.NotFound);if(source.Status!=SyncJobStatus.RequiresAttention)return new(null,SyncRetryFailure.NotRetryable);
        if(!await db.Organizations.AnyAsync(x=>x.Id==source.OrganizationId&&x.Status=="Active",ct)||!await FeatureFlags.IsEnabledAsync(db,source.OrganizationId,"KaspiSync",ct))return new(null,SyncRetryFailure.OrganizationDisabled);
        if(await db.SyncJobs.AnyAsync(x=>x.MarketplaceConnectionId==source.MarketplaceConnectionId&&(x.Status==SyncJobStatus.Queued||x.Status==SyncJobStatus.Running||x.Status==SyncJobStatus.RetryScheduled),ct))return new(null,SyncRetryFailure.AlreadyRunning);
        var retry=new SyncJobEntity{Id=Guid.NewGuid(),OrganizationId=source.OrganizationId,MarketplaceConnectionId=source.MarketplaceConnectionId,WindowFrom=source.WindowFrom,WindowTo=DateTimeOffset.UtcNow};db.SyncJobs.Add(retry);return new(retry,SyncRetryFailure.None);
    }
}
