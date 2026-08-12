using Microsoft.EntityFrameworkCore;

namespace SellerFinance.Api;

public enum SyncRetryFailure { None, NotFound, NotRetryable, OrganizationDisabled, AlreadyRunning }
public sealed record SyncRetryResult(SyncJobEntity? Job,SyncRetryFailure Failure);

public static class SaasAdminOperations
{
    public static async Task<SyncRetryResult> RetrySyncAsync(SellerFinanceDbContext db,Guid sourceId,CancellationToken ct=default)
    {
        var source=await db.SyncJobs.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==sourceId,ct);if(source is null)return new(null,SyncRetryFailure.NotFound);if(source.Status!=SyncJobStatus.RequiresAttention)return new(null,SyncRetryFailure.NotRetryable);
        var now=DateTimeOffset.UtcNow;if(!await db.Organizations.AnyAsync(x=>x.Id==source.OrganizationId&&x.Status=="Active",ct)||!await db.Subscriptions.AnyAsync(x=>x.OrganizationId==source.OrganizationId&&(x.Status==SubscriptionStatus.Active||x.Status==SubscriptionStatus.Trialing)&&x.PeriodEnd>now,ct)||!await FeatureFlags.IsEnabledAsync(db,source.OrganizationId,"KaspiSync",ct))return new(null,SyncRetryFailure.OrganizationDisabled);
        if(await db.SyncJobs.AnyAsync(x=>x.MarketplaceConnectionId==source.MarketplaceConnectionId&&(x.Status==SyncJobStatus.Queued||x.Status==SyncJobStatus.Running||x.Status==SyncJobStatus.RetryScheduled),ct))return new(null,SyncRetryFailure.AlreadyRunning);
        var retry=new SyncJobEntity{Id=Guid.NewGuid(),OrganizationId=source.OrganizationId,MarketplaceConnectionId=source.MarketplaceConnectionId,WindowFrom=source.WindowFrom,WindowTo=DateTimeOffset.UtcNow};db.SyncJobs.Add(retry);return new(retry,SyncRetryFailure.None);
    }
}

public sealed class SubscriptionMaintenanceWorker(IServiceScopeFactory scopes,ILogger<SubscriptionMaintenanceWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken){while(!stoppingToken.IsCancellationRequested){try{await ProcessAsync(stoppingToken);}catch(Exception ex){logger.LogError("Subscription maintenance failed: {ErrorType}",ex.GetType().Name);}await Task.Delay(TimeSpan.FromMinutes(1),stoppingToken);}}
    public async Task<int> ProcessAsync(CancellationToken ct=default){await using var scope=scopes.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<SellerFinanceDbContext>();return await db.Subscriptions.Where(x=>x.PeriodEnd<=DateTimeOffset.UtcNow&&x.Status!=SubscriptionStatus.Expired).ExecuteUpdateAsync(x=>x.SetProperty(y=>y.Status,SubscriptionStatus.Expired).SetProperty(y=>y.UpdatedAt,DateTimeOffset.UtcNow),ct);}
}
