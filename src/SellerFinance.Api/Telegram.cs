using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace SellerFinance.Api;

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
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested){try{await ProcessOneAsync(stoppingToken);}catch(Exception ex){logger.LogError("Notification worker iteration failed: {ErrorType}",ex.GetType().Name);}await Task.Delay(TimeSpan.FromSeconds(5),stoppingToken);}
    }

    public async Task ProcessOneAsync(CancellationToken ct)
    {
        await using var scope=scopes.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<SellerFinanceDbContext>();var candidate=await db.NotificationDeliveries.AsNoTracking().OrderBy(x=>x.CreatedAt).FirstOrDefaultAsync(x=>(x.Status==NotificationDeliveryStatus.Queued||x.Status==NotificationDeliveryStatus.RetryScheduled)&&x.NextAttemptAt<=DateTimeOffset.UtcNow,ct);if(candidate is null)return;NotificationDeliveryEntity delivery;
        if(db.Database.IsRelational()){var claimed=await db.NotificationDeliveries.Where(x=>x.Id==candidate.Id&&(x.Status==NotificationDeliveryStatus.Queued||x.Status==NotificationDeliveryStatus.RetryScheduled)).ExecuteUpdateAsync(x=>x.SetProperty(y=>y.Status,NotificationDeliveryStatus.Sending).SetProperty(y=>y.Attempt,y=>y.Attempt+1),ct);if(claimed==0)return;delivery=await db.NotificationDeliveries.SingleAsync(x=>x.Id==candidate.Id,ct);}else{delivery=await db.NotificationDeliveries.SingleAsync(x=>x.Id==candidate.Id,ct);delivery.Status=NotificationDeliveryStatus.Sending;delivery.Attempt++;await db.SaveChangesAsync(ct);}
        var connection=await db.TelegramConnections.AsNoTracking().SingleOrDefaultAsync(x=>x.OrganizationId==delivery.OrganizationId&&x.Status=="Active"&&x.ChatId!=null,ct);if(connection is null){delivery.Status=NotificationDeliveryStatus.Suppressed;delivery.ErrorCode="TELEGRAM_NOT_LINKED";await db.SaveChangesAsync(ct);return;}
        bool sent=false;try{sent=await telegram.SendAsync(connection.ChatId!.Value,delivery.Message,ct);}catch(HttpRequestException){delivery.ErrorCode="TELEGRAM_UNAVAILABLE";}
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
        if(!update.TryGetProperty("message",out var message)||!message.TryGetProperty("text",out var text)||!message.TryGetProperty("chat",out var chat)||!chat.TryGetProperty("id",out var chatId))return;var command=text.GetString()??"";if(!command.StartsWith("/start ",StringComparison.Ordinal))return;var code=command[7..].Trim();var connection=await db.TelegramConnections.SingleOrDefaultAsync(x=>x.LinkCodeHash==TokenTools.Hash(code)&&x.LinkCodeExpiresAt>DateTimeOffset.UtcNow&&x.Status=="Pending",ct);if(connection is null)return;connection.ChatId=chatId.GetInt64();connection.Status="Active";connection.LinkedAt=DateTimeOffset.UtcNow;connection.LinkCodeHash=TokenTools.Hash(TokenTools.CreateToken());await db.SaveChangesAsync(ct);await client.SendAsync(connection.ChatId.Value,"Seller Finance подключён. Уведомления будут приходить в этот чат.",ct);
    }
}
