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

public sealed class NotificationDispatcher(IServiceScopeFactory scopes,TelegramClient telegram)
{
    public async Task DispatchAsync(string organizationId,NotificationEventType eventType,string text,decimal? value,CancellationToken ct)
    {
        await using var scope=scopes.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<SellerFinanceDbContext>();var connection=await db.TelegramConnections.AsNoTracking().SingleOrDefaultAsync(x=>x.OrganizationId==organizationId&&x.Status=="Active"&&x.ChatId!=null,ct);if(connection is null)return;var rule=await db.NotificationRules.AsNoTracking().SingleOrDefaultAsync(x=>x.OrganizationId==organizationId&&x.EventType==eventType&&x.Enabled,ct);if(rule is null||rule.Threshold.HasValue&&value.HasValue&&value.Value<rule.Threshold.Value)return;await telegram.SendAsync(connection.ChatId!.Value,text,ct);
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
