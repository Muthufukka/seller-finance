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

public sealed record KaspiOrderLineDto(string EntryId,string ProductCode,string Name,string? Category,int Quantity,decimal Revenue,decimal Delivery);
public sealed record KaspiOrderDto(string Id,string Code,decimal TotalPrice,string Status,DateTimeOffset CreatedAt,IReadOnlyList<KaspiOrderLineDto> Lines);
public sealed record KaspiResult(bool Success,HttpStatusCode StatusCode,string? ErrorCode,IReadOnlyList<KaspiOrderDto> Orders);

public sealed class KaspiClient(HttpClient http)
{
    public async Task<KaspiResult> GetOrdersAsync(string token,DateTimeOffset from,DateTimeOffset to,CancellationToken cancellationToken)
    {
        var orders=new List<KaspiOrderDto>();
        HttpStatusCode statusCode=HttpStatusCode.OK;
        for(var page=0;page<100;page++)
        {
            var uri=$"orders?page[number]={page}&page[size]=100&filter[orders][creationDate][$ge]={from.ToUnixTimeMilliseconds()}&filter[orders][creationDate][$le]={to.ToUnixTimeMilliseconds()}";using var response=await SendAsync(uri,token,cancellationToken);statusCode=response.StatusCode;if(!response.IsSuccessStatusCode)return new(false,response.StatusCode,MapError(response.StatusCode),[]);await using var stream=await response.Content.ReadAsStreamAsync(cancellationToken);using var json=await JsonDocument.ParseAsync(stream,cancellationToken:cancellationToken);if(!json.RootElement.TryGetProperty("data",out var data))break;var count=0;
            foreach(var item in data.EnumerateArray()){count++;var a=item.GetProperty("attributes");var id=item.GetProperty("id").GetString()!;var code=a.TryGetProperty("code",out var c)?c.ToString():id;var price=a.TryGetProperty("totalPrice",out var p)&&p.TryGetDecimal(out var amount)?amount:0;var status=a.TryGetProperty("status",out var s)?s.GetString()??"PENDING":"PENDING";var millis=a.TryGetProperty("creationDate",out var d)&&d.TryGetInt64(out var ms)?ms:DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();orders.Add(new(id,code,price,status,DateTimeOffset.FromUnixTimeMilliseconds(millis),[]));}
            if(count<100)break;
        }
        var enriched=new List<KaspiOrderDto>();foreach(var order in orders){var lines=await GetLinesAsync(token,order.Id,cancellationToken);if(!lines.Success)return new(false,lines.StatusCode,lines.ErrorCode,[]);enriched.Add(order with{Lines=lines.Lines});}return new(true,statusCode,null,enriched);
    }

    private async Task<(bool Success,HttpStatusCode StatusCode,string? ErrorCode,IReadOnlyList<KaspiOrderLineDto> Lines)> GetLinesAsync(string token,string orderId,CancellationToken ct)
    {
        using var response=await SendAsync($"orders/{Uri.EscapeDataString(orderId)}/entries",token,ct);if(!response.IsSuccessStatusCode)return(false,response.StatusCode,MapError(response.StatusCode),[]);await using var stream=await response.Content.ReadAsStreamAsync(ct);using var json=await JsonDocument.ParseAsync(stream,cancellationToken:ct);var lines=new List<KaspiOrderLineDto>();
        foreach(var entry in json.RootElement.GetProperty("data").EnumerateArray()){var entryId=entry.GetProperty("id").GetString()!;var a=entry.GetProperty("attributes");var quantity=a.TryGetProperty("quantity",out var q)&&q.TryGetInt32(out var qty)?qty:1;var revenue=a.TryGetProperty("totalPrice",out var t)&&t.TryGetDecimal(out var total)?total:0;var delivery=a.TryGetProperty("deliveryCost",out var d)&&d.TryGetDecimal(out var deliveryCost)?deliveryCost:0;using var productResponse=await SendAsync($"orderentries/{Uri.EscapeDataString(entryId)}/product",token,ct);if(!productResponse.IsSuccessStatusCode)return(false,productResponse.StatusCode,MapError(productResponse.StatusCode),[]);await using var productStream=await productResponse.Content.ReadAsStreamAsync(ct);using var productJson=await JsonDocument.ParseAsync(productStream,cancellationToken:ct);var product=productJson.RootElement.GetProperty("data");var pa=product.GetProperty("attributes");var code=pa.TryGetProperty("code",out var c)?c.ToString():product.GetProperty("id").ToString();var name=pa.TryGetProperty("name",out var n)?n.GetString()??code:code;var category=pa.TryGetProperty("category",out var cat)?cat.GetString():null;lines.Add(new(entryId,code,name,category,quantity,revenue,delivery));}return(true,response.StatusCode,null,lines);
    }
    private async Task<HttpResponseMessage> SendAsync(string uri,string token,CancellationToken ct){var request=new HttpRequestMessage(HttpMethod.Get,uri);request.Headers.TryAddWithoutValidation("X-Auth-Token",token);request.Headers.TryAddWithoutValidation("Accept","application/vnd.api+json");return await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);}

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
        if(job is null){var due=await db.MarketplaceConnections.AsNoTracking().FirstOrDefaultAsync(x=>x.Status==MarketplaceConnectionStatus.Active&&(!x.LastSuccessfulSyncAt.HasValue||x.LastSuccessfulSyncAt<DateTimeOffset.UtcNow.AddMinutes(-15)),ct);if(due is not null){db.SyncJobs.Add(new(){Id=Guid.NewGuid(),OrganizationId=due.OrganizationId,MarketplaceConnectionId=due.Id,WindowFrom=due.LastSuccessfulSyncAt.HasValue?DateTimeOffset.UtcNow.AddDays(-14):DateTimeOffset.UtcNow.AddDays(-90),WindowTo=DateTimeOffset.UtcNow});await db.SaveChangesAsync(ct);}return;}
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
                if(source.Lines.Count>0){var sourceIds=source.Lines.Select(x=>x.EntryId).ToHashSet();order.Lines.RemoveAll(x=>x.ExternalId is null&&x.ProductId=="kaspi-unmapped"||x.ExternalId is not null&&!sourceIds.Contains(x.ExternalId));}foreach(var item in source.Lines){var product=await db.Products.SingleOrDefaultAsync(x=>x.OrganizationId==job.OrganizationId&&x.Sku==item.ProductCode,ct);if(product is null){product=new(){Id=Guid.NewGuid().ToString("N"),OrganizationId=job.OrganizationId,Sku=item.ProductCode,Name=item.Name,Category=item.Category};db.Products.Add(product);}else{product.Name=item.Name;product.Category=item.Category;}var line=order.Lines.SingleOrDefault(x=>x.ExternalId==item.EntryId);if(line is null){line=new(){Id=Guid.NewGuid(),OrderId=order.Id,ExternalId=item.EntryId};order.Lines.Add(line);}line.ProductId=product.Id;line.Quantity=item.Quantity;line.Revenue=item.Revenue;line.Delivery=item.Delivery;}
            }
            job.Status=SyncJobStatus.Succeeded; job.CompletedAt=DateTimeOffset.UtcNow; job.ImportedOrders=result.Orders.Count; connection.Status=MarketplaceConnectionStatus.Active; connection.LastSuccessfulSyncAt=DateTimeOffset.UtcNow; connection.LastErrorCode=null;
        }
        else
        {
            job.ErrorCode=result.ErrorCode; var retryable=result.StatusCode==(HttpStatusCode)429||(int)result.StatusCode>=500;
            if(retryable&&job.Attempt<5){job.Status=SyncJobStatus.RetryScheduled;job.NextAttemptAt=DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2,job.Attempt)*30+Random.Shared.Next(0,16));}
            else {job.Status=SyncJobStatus.RequiresAttention;job.CompletedAt=DateTimeOffset.UtcNow;connection.Status=MarketplaceConnectionStatus.RequiresAttention;connection.LastErrorCode=result.ErrorCode;}
        }
        await db.SaveChangesAsync(ct);
        var notifications=scope.ServiceProvider.GetRequiredService<NotificationDispatcher>();var baseUrl=(scope.ServiceProvider.GetRequiredService<IConfiguration>()["PUBLIC_BASE_URL"]??"https://seller-finance.onrender.com").TrimEnd('/');
        if(job.Status==SyncJobStatus.RequiresAttention)await notifications.QueueAsync(job.OrganizationId,NotificationEventType.SyncRequiresAttention,$"Seller Finance: синхронизация Kaspi требует внимания. Открыть: {baseUrl}",null,$"sync:{job.Id}",ct);
        else if(job.Status==SyncJobStatus.Succeeded)await QueueFinancialAlertsAsync(db,notifications,job.OrganizationId,baseUrl,ct);
    }

    public static async Task QueueFinancialAlertsAsync(SellerFinanceDbContext db,NotificationDispatcher notifications,string organizationId,string baseUrl,CancellationToken ct)
    {
        var products=await DbAnalytics.ProductsAsync(db,organizationId);var json=products.Select(x=>JsonSerializer.SerializeToElement(x)).ToArray();var missing=json.Where(x=>x.GetProperty("revenue").GetDecimal()>0&&x.GetProperty("coveragePct").GetDecimal()<100m).ToArray();var bucket=DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        if(missing.Length>0)await notifications.QueueAsync(organizationId,NotificationEventType.MissingCost,$"Seller Finance: у {missing.Length} товар(ов) новые продажи без полной себестоимости. Открыть: {baseUrl}",missing.Length,$"missing-cost:{bucket}",ct);
        var negative=json.Where(x=>x.TryGetProperty("margin",out var margin)&&margin.ValueKind==JsonValueKind.Number&&margin.GetDecimal()<0m).OrderBy(x=>x.GetProperty("margin").GetDecimal()).ToArray();if(negative.Length>0){var worst=negative[0].GetProperty("margin").GetDecimal();await notifications.QueueAsync(organizationId,NotificationEventType.NegativeMargin,$"Seller Finance: отрицательная маржа у {negative.Length} товар(ов), минимум {worst:0.0}%. Открыть: {baseUrl}",worst,$"negative-margin:{bucket}",ct);}
    }

    private static OrderStatus MapStatus(string value)=>value.ToUpperInvariant() switch { "COMPLETED" or "DELIVERED"=>OrderStatus.Completed,"RETURNED"=>OrderStatus.Returned,"CANCELLED" or "CANCELED"=>OrderStatus.Cancelled,_=>OrderStatus.Pending };
}
