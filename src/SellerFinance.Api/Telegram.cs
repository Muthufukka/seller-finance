using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace SellerFinance.Api;

public static class TelegramDeliveryRegistration
{
    public static IServiceCollection AddTelegramDelivery(this IServiceCollection services)
    {
        services.AddHttpClient<TelegramClient>(client=>client.Timeout=TimeSpan.FromSeconds(15)).RemoveAllLoggers();
        return services;
    }
}

public sealed class TelegramClient(HttpClient http,IConfiguration configuration)
{
    public bool IsConfigured=>!String.IsNullOrWhiteSpace(configuration["TELEGRAM_BOT_TOKEN"]);
    public async Task<bool> SendAsync(long chatId,string message,CancellationToken ct)
    {
        var token=configuration["TELEGRAM_BOT_TOKEN"];if(String.IsNullOrWhiteSpace(token))return false;
        using var content=new FormUrlEncodedContent(new Dictionary<string,string>{{"chat_id",chatId.ToString()},{"text",message}});
        using var response=await http.PostAsync($"https://api.telegram.org/bot{token}/sendMessage",content,ct);return response.IsSuccessStatusCode;
    }
}

public sealed class NotificationDispatcher(IServiceScopeFactory scopes)
{
    public async Task<bool> QueueAsync(string organizationId,NotificationEventType eventType,string text,decimal? value,string deduplicationKey,CancellationToken ct)
    {
        await using var scope=scopes.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<SellerFinanceDbContext>();
        if(!await FeatureFlags.IsEnabledAsync(db,organizationId,"TelegramNotifications",ct))return false;
        if(!await db.TelegramConnections.AsNoTracking().AnyAsync(x=>x.OrganizationId==organizationId&&x.Status=="Active"&&x.ChatId!=null,ct))return false;
        var rule=await db.NotificationRules.AsNoTracking().SingleOrDefaultAsync(x=>x.OrganizationId==organizationId&&x.EventType==eventType&&x.Enabled,ct);if(rule is null||!MatchesThreshold(eventType,value,rule.Threshold))return false;
        if(await db.NotificationDeliveries.AnyAsync(x=>x.OrganizationId==organizationId&&x.DeduplicationKey==deduplicationKey,ct))return false;
        db.NotificationDeliveries.Add(new(){Id=Guid.NewGuid(),OrganizationId=organizationId,EventType=eventType,DeduplicationKey=deduplicationKey,Message=text,Value=value});try{await db.SaveChangesAsync(ct);return true;}catch(DbUpdateException ex)when(ex.InnerException is Npgsql.PostgresException{SqlState:Npgsql.PostgresErrorCodes.UniqueViolation}){return false;}
    }

    public static bool MatchesThreshold(NotificationEventType type,decimal? value,decimal? threshold)=>type switch
    {
        NotificationEventType.NegativeMargin=>value.HasValue&&value.Value<(threshold??0m),
        _=>!threshold.HasValue||!value.HasValue||value.Value>=threshold.Value
    };
}

public sealed class NotificationDeliveryWorker(IServiceScopeFactory scopes,TelegramClient telegram,ILogger<NotificationDeliveryWorker> logger):BackgroundService
{
    private static readonly SemaphoreSlim NonRelationalClaimLock=new(1,1);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested){try{await ProcessOneAsync(stoppingToken);}catch(Exception ex){logger.LogError("Notification worker iteration failed: {ErrorType}",ex.GetType().Name);}await Task.Delay(TimeSpan.FromSeconds(5),stoppingToken);}
    }

    public async Task ProcessOneAsync(CancellationToken ct)
    {
        await using var scope=scopes.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<SellerFinanceDbContext>();var now=DateTimeOffset.UtcNow;var staleBefore=now.AddMinutes(-10);var candidate=await db.NotificationDeliveries.AsNoTracking().OrderBy(x=>x.CreatedAt).FirstOrDefaultAsync(x=>((x.Status==NotificationDeliveryStatus.Queued||x.Status==NotificationDeliveryStatus.RetryScheduled)&&x.NextAttemptAt<=now)||x.Status==NotificationDeliveryStatus.Sending&&(!x.StartedAt.HasValue||x.StartedAt<staleBefore),ct);if(candidate is null)return;NotificationDeliveryEntity delivery;
        if(db.Database.IsRelational()){var claimed=await db.NotificationDeliveries.Where(x=>x.Id==candidate.Id&&(((x.Status==NotificationDeliveryStatus.Queued||x.Status==NotificationDeliveryStatus.RetryScheduled)&&x.NextAttemptAt<=now)||x.Status==NotificationDeliveryStatus.Sending&&(!x.StartedAt.HasValue||x.StartedAt<staleBefore))).ExecuteUpdateAsync(x=>x.SetProperty(y=>y.Status,NotificationDeliveryStatus.Sending).SetProperty(y=>y.StartedAt,now).SetProperty(y=>y.Attempt,y=>y.Attempt+1),ct);if(claimed==0)return;delivery=await db.NotificationDeliveries.SingleAsync(x=>x.Id==candidate.Id,ct);}else{await NonRelationalClaimLock.WaitAsync(ct);try{delivery=await db.NotificationDeliveries.SingleAsync(x=>x.Id==candidate.Id,ct);if(!(((delivery.Status==NotificationDeliveryStatus.Queued||delivery.Status==NotificationDeliveryStatus.RetryScheduled)&&delivery.NextAttemptAt<=now)||delivery.Status==NotificationDeliveryStatus.Sending&&(!delivery.StartedAt.HasValue||delivery.StartedAt<staleBefore)))return;delivery.Status=NotificationDeliveryStatus.Sending;delivery.StartedAt=now;delivery.Attempt++;await db.SaveChangesAsync(ct);}finally{NonRelationalClaimLock.Release();}}
        var connection=await db.TelegramConnections.AsNoTracking().SingleOrDefaultAsync(x=>x.OrganizationId==delivery.OrganizationId&&x.Status=="Active"&&x.ChatId!=null,ct);if(connection is null){delivery.Status=NotificationDeliveryStatus.Suppressed;delivery.ErrorCode="TELEGRAM_NOT_LINKED";await db.SaveChangesAsync(ct);return;}
        bool sent=false;try{sent=await telegram.SendAsync(connection.ChatId!.Value,delivery.Message,ct);}catch(HttpRequestException){delivery.ErrorCode="TELEGRAM_UNAVAILABLE";}catch(OperationCanceledException)when(!ct.IsCancellationRequested){delivery.ErrorCode="TELEGRAM_TIMEOUT";}
        if(sent){delivery.Status=NotificationDeliveryStatus.Sent;delivery.SentAt=DateTimeOffset.UtcNow;delivery.ErrorCode=null;}else if(delivery.Attempt<5){delivery.Status=NotificationDeliveryStatus.RetryScheduled;delivery.ErrorCode??="TELEGRAM_REJECTED";delivery.NextAttemptAt=DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2,delivery.Attempt)*15+Random.Shared.Next(0,6));}else{delivery.Status=NotificationDeliveryStatus.Failed;delivery.ErrorCode??="TELEGRAM_REJECTED";logger.LogWarning("Notification delivery {DeliveryId} exhausted retries with {ErrorCode}",delivery.Id,delivery.ErrorCode);}await db.SaveChangesAsync(ct);
    }
}

public static class TelegramWebhook
{
    public static bool ValidSecret(string supplied,IConfiguration configuration)
    {
        var expected=configuration["TELEGRAM_WEBHOOK_SECRET"];if(String.IsNullOrWhiteSpace(expected))return false;var a=Encoding.UTF8.GetBytes(supplied);var b=Encoding.UTF8.GetBytes(expected);return a.Length==b.Length&&CryptographicOperations.FixedTimeEquals(a,b);
    }
    public static async Task ProcessAsync(JsonElement update,SellerFinanceDbContext db,TelegramClient client,CancellationToken ct)
    {
        if(!update.TryGetProperty("message",out var message)||!message.TryGetProperty("text",out var text)||text.ValueKind!=JsonValueKind.String||!message.TryGetProperty("chat",out var chat)||!chat.TryGetProperty("id",out var chatId)||chatId.ValueKind!=JsonValueKind.Number||!chatId.TryGetInt64(out var parsedChatId))return;var command=text.GetString()??"";if(!command.StartsWith("/start ",StringComparison.Ordinal))return;var code=command[7..].Trim();var connection=await db.TelegramConnections.SingleOrDefaultAsync(x=>x.LinkCodeHash==TokenTools.Hash(code)&&x.LinkCodeExpiresAt>DateTimeOffset.UtcNow&&x.Status=="Pending",ct);if(connection is null)return;connection.ChatId=parsedChatId;connection.Status="Active";connection.LinkedAt=DateTimeOffset.UtcNow;connection.LinkCodeHash=TokenTools.Hash(TokenTools.CreateToken());db.AuditLogs.Add(new(){Id=Guid.NewGuid(),OrganizationId=connection.OrganizationId,Action="telegram.link.completed",EntityType="TelegramConnection",EntityId=connection.Id.ToString()});await db.SaveChangesAsync(ct);await client.SendAsync(connection.ChatId.Value,"Seller Finance подключён. Уведомления будут приходить в этот чат.",ct);
    }
}
