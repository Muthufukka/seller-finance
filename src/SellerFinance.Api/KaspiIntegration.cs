using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SellerFinance.Domain;

namespace SellerFinance.Api;

public sealed class TokenCipher(IConfiguration configuration)
{
    private byte[] Key => ReadKey(configuration["TOKEN_ENCRYPTION_KEY"]);

    public (byte[] Ciphertext, byte[] Nonce, byte[] Tag) Encrypt(string value)
    {
        var nonce=RandomNumberGenerator.GetBytes(12); var tag=new byte[16]; var plain=System.Text.Encoding.UTF8.GetBytes(value); var cipher=new byte[plain.Length];
        using var aes=new AesGcm(Key,16); aes.Encrypt(nonce,plain,cipher,tag);
        CryptographicOperations.ZeroMemory(plain);
        return (cipher,nonce,tag);
    }

    public string Decrypt(MarketplaceConnectionEntity connection)
    {
        var plain=new byte[connection.TokenCiphertext.Length];
        using var aes=new AesGcm(Key,16); aes.Decrypt(connection.TokenNonce,connection.TokenCiphertext,connection.TokenTag,plain);
        try { return System.Text.Encoding.UTF8.GetString(plain); } finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private static byte[] ReadKey(string? value)
    {
        if(String.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("TOKEN_ENCRYPTION_KEY is not configured");
        try { var key=Convert.FromBase64String(value); if(key.Length==32)return key; } catch(FormatException) { }
        throw new InvalidOperationException("TOKEN_ENCRYPTION_KEY must be a base64-encoded 32-byte key");
    }
}

public sealed record KaspiOrderDto(string Id,string Code,decimal TotalPrice,string Status,DateTimeOffset CreatedAt);
public sealed record KaspiResult(bool Success,HttpStatusCode StatusCode,string? ErrorCode,IReadOnlyList<KaspiOrderDto> Orders);

public sealed class KaspiClient(HttpClient http)
{
    public async Task<KaspiResult> GetOrdersAsync(string token,DateTimeOffset from,DateTimeOffset to,CancellationToken cancellationToken)
    {
        var uri=$"orders?page[number]=0&page[size]=100&filter[orders][creationDate][$ge]={from.ToUnixTimeMilliseconds()}&filter[orders][creationDate][$le]={to.ToUnixTimeMilliseconds()}";
        using var request=new HttpRequestMessage(HttpMethod.Get,uri);
        request.Headers.TryAddWithoutValidation("X-Auth-Token",token);
        request.Headers.TryAddWithoutValidation("Accept","application/vnd.api+json");
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellationToken);
        if(!response.IsSuccessStatusCode) return new(false,response.StatusCode,MapError(response.StatusCode),[]);
        await using var stream=await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json=await JsonDocument.ParseAsync(stream,cancellationToken:cancellationToken);
        var orders=new List<KaspiOrderDto>();
        if(json.RootElement.TryGetProperty("data",out var data)) foreach(var item in data.EnumerateArray())
        {
            var a=item.GetProperty("attributes");
            var id=item.GetProperty("id").GetString()!;
            var code=a.TryGetProperty("code",out var c)?c.ToString():id;
            var price=a.TryGetProperty("totalPrice",out var p)&&p.TryGetDecimal(out var amount)?amount:0;
            var status=a.TryGetProperty("status",out var s)?s.GetString()??"PENDING":"PENDING";
            var millis=a.TryGetProperty("creationDate",out var d)&&d.TryGetInt64(out var ms)?ms:DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            orders.Add(new(id,code,price,status,DateTimeOffset.FromUnixTimeMilliseconds(millis)));
        }
        return new(true,response.StatusCode,null,orders);
    }

    private static string MapError(HttpStatusCode status)=>status switch { HttpStatusCode.Unauthorized=>"TOKEN_UNAUTHORIZED",HttpStatusCode.Forbidden=>"TOKEN_FORBIDDEN",(HttpStatusCode)429=>"RATE_LIMITED",_ when (int)status>=500=>"KASPI_UNAVAILABLE",_=>"KASPI_REQUEST_FAILED" };
}

public sealed class KaspiSyncWorker(IServiceScopeFactory scopes,ILogger<KaspiSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessOneAsync(stoppingToken); } catch(Exception ex) { logger.LogError(ex,"Kaspi sync worker iteration failed"); }
            await Task.Delay(TimeSpan.FromSeconds(10),stoppingToken);
        }
    }

    private async Task ProcessOneAsync(CancellationToken ct)
    {
        await using var scope=scopes.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<SellerFinanceDbContext>();
        var job=await db.SyncJobs.OrderBy(x=>x.CreatedAt).FirstOrDefaultAsync(x=>(x.Status==SyncJobStatus.Queued||x.Status==SyncJobStatus.RetryScheduled)&&x.NextAttemptAt<=DateTimeOffset.UtcNow,ct);
        if(job is null)return;
        job.Status=SyncJobStatus.Running; job.StartedAt=DateTimeOffset.UtcNow; job.Attempt++; await db.SaveChangesAsync(ct);
        var connection=await db.MarketplaceConnections.SingleAsync(x=>x.Id==job.MarketplaceConnectionId,ct);
        KaspiResult result;
        try { var token=scope.ServiceProvider.GetRequiredService<TokenCipher>().Decrypt(connection); result=await scope.ServiceProvider.GetRequiredService<KaspiClient>().GetOrdersAsync(token,job.WindowFrom,job.WindowTo,ct); }
        catch(CryptographicException) { result=new(false,0,"TOKEN_DECRYPTION_FAILED",[]); }
        if(result.Success)
        {
            foreach(var source in result.Orders)
            {
                var order=await db.Orders.Include(x=>x.Lines).SingleOrDefaultAsync(x=>x.OrganizationId==job.OrganizationId&&x.ExternalId==source.Id,ct);
                if(order is null) { order=new(){Id=Guid.NewGuid().ToString("N"),OrganizationId=job.OrganizationId,ExternalId=source.Id}; db.Orders.Add(order); }
                order.Date=DateOnly.FromDateTime(source.CreatedAt.UtcDateTime); order.Status=MapStatus(source.Status);order.CalculationDateFallback=order.Status==OrderStatus.Completed&&order.CompletionDate is null;
                if(order.Lines.Count==0) order.Lines.Add(new(){Id=Guid.NewGuid(),OrderId=order.Id,ProductId="kaspi-unmapped",Revenue=source.TotalPrice,Quantity=1}); else order.Lines[0].Revenue=source.TotalPrice;
            }
            job.Status=SyncJobStatus.Succeeded; job.CompletedAt=DateTimeOffset.UtcNow; job.ImportedOrders=result.Orders.Count; connection.Status=MarketplaceConnectionStatus.Active; connection.LastSuccessfulSyncAt=DateTimeOffset.UtcNow; connection.LastErrorCode=null;
        }
        else
        {
            job.ErrorCode=result.ErrorCode; var retryable=result.StatusCode==(HttpStatusCode)429||(int)result.StatusCode>=500;
            if(retryable&&job.Attempt<5){job.Status=SyncJobStatus.RetryScheduled;job.NextAttemptAt=DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2,job.Attempt)*30);}
            else {job.Status=SyncJobStatus.RequiresAttention;job.CompletedAt=DateTimeOffset.UtcNow;connection.Status=MarketplaceConnectionStatus.RequiresAttention;connection.LastErrorCode=result.ErrorCode;}
        }
        await db.SaveChangesAsync(ct);
        var notifications=scope.ServiceProvider.GetRequiredService<NotificationDispatcher>();
        if(job.Status==SyncJobStatus.RequiresAttention)await notifications.DispatchAsync(job.OrganizationId,NotificationEventType.SyncRequiresAttention,"Seller Finance: синхронизация Kaspi требует внимания.",null,ct);
        else if(job.Status==SyncJobStatus.Succeeded&&await db.OrderLines.AnyAsync(x=>db.Orders.Any(o=>o.Id==x.OrderId&&o.OrganizationId==job.OrganizationId)&&x.UnitCost==null,ct))await notifications.DispatchAsync(job.OrganizationId,NotificationEventType.MissingCost,"Seller Finance: после синхронизации найдены товары без себестоимости.",null,ct);
    }

    private static OrderStatus MapStatus(string value)=>value.ToUpperInvariant() switch { "COMPLETED" or "DELIVERED"=>OrderStatus.Completed,"RETURNED"=>OrderStatus.Returned,"CANCELLED" or "CANCELED"=>OrderStatus.Cancelled,_=>OrderStatus.Pending };
}
