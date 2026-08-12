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

public sealed record KaspiOrderLineDto(string EntryId,string ProductCode,string Name,string? Category,int Quantity,decimal Revenue,decimal? ItemDeliveryCost,decimal? BasePrice=null,string? ExternalProductId=null);
public sealed record KaspiOrderDto(string Id,string Code,decimal TotalPrice,string Status,DateTimeOffset CreatedAt,IReadOnlyList<KaspiOrderLineDto> Lines,DateTimeOffset? CompletedAt=null,string? PaymentMode=null,decimal SellerDeliveryCost=0m);
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
            foreach(var item in data.EnumerateArray())
            {
                count++;var attributes=item.GetProperty("attributes");var id=item.GetProperty("id").GetString()!;
                var code=Text(attributes,"code")??id;var price=DecimalValue(attributes,"totalPrice")??0m;var status=Text(attributes,"status")??"PENDING";
                var created=DateValue(attributes,"creationDate")??DateTimeOffset.UtcNow;var completed=DateValue(attributes,"completionDate");
                var paymentMode=Text(attributes,"paymentMode");var sellerDelivery=DecimalValue(attributes,"deliveryCostForSeller")??0m;
                orders.Add(new(id,code,price,status,created,[],completed,paymentMode,sellerDelivery));
            }
            if(count<100)break;
        }
        var enriched=new List<KaspiOrderDto>();foreach(var order in orders){var lines=await GetLinesAsync(token,order.Id,cancellationToken);if(!lines.Success)return new(false,lines.StatusCode,lines.ErrorCode,[]);enriched.Add(order with{Lines=lines.Lines});}return new(true,statusCode,null,enriched);
    }

    private async Task<(bool Success,HttpStatusCode StatusCode,string? ErrorCode,IReadOnlyList<KaspiOrderLineDto> Lines)> GetLinesAsync(string token,string orderId,CancellationToken ct)
    {
        using var response=await SendAsync($"orders/{Uri.EscapeDataString(orderId)}/entries",token,ct);if(!response.IsSuccessStatusCode)return(false,response.StatusCode,MapError(response.StatusCode),[]);await using var stream=await response.Content.ReadAsStreamAsync(ct);using var json=await JsonDocument.ParseAsync(stream,cancellationToken:ct);var lines=new List<KaspiOrderLineDto>();
        foreach(var entry in json.RootElement.GetProperty("data").EnumerateArray())
        {
            var entryId=entry.GetProperty("id").GetString()!;var attributes=entry.GetProperty("attributes");
            var quantity=attributes.TryGetProperty("quantity",out var quantityJson)&&quantityJson.TryGetInt32(out var parsedQuantity)?parsedQuantity:1;
            var revenue=DecimalValue(attributes,"totalPrice")??0m;var delivery=DecimalValue(attributes,"deliveryCost");var basePrice=DecimalValue(attributes,"basePrice");
            using var productResponse=await SendAsync($"orderentries/{Uri.EscapeDataString(entryId)}/product",token,ct);if(!productResponse.IsSuccessStatusCode)return(false,productResponse.StatusCode,MapError(productResponse.StatusCode),[]);
            await using var productStream=await productResponse.Content.ReadAsStreamAsync(ct);using var productJson=await JsonDocument.ParseAsync(productStream,cancellationToken:ct);var product=productJson.RootElement.GetProperty("data");var productAttributes=product.GetProperty("attributes");
            var externalProductId=product.GetProperty("id").ToString();var code=Text(productAttributes,"code")??externalProductId;var name=Text(productAttributes,"name")??code;var category=Text(productAttributes,"category");
            lines.Add(new(entryId,code,name,category,quantity,revenue,delivery,basePrice,externalProductId));
        }
        return(true,response.StatusCode,null,lines);
    }
    private async Task<HttpResponseMessage> SendAsync(string uri,string token,CancellationToken ct){var request=new HttpRequestMessage(HttpMethod.Get,uri);request.Headers.TryAddWithoutValidation("X-Auth-Token",token);request.Headers.TryAddWithoutValidation("Accept","application/vnd.api+json");return await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);}

    private static string MapError(HttpStatusCode status)=>status switch { HttpStatusCode.Unauthorized=>"TOKEN_UNAUTHORIZED",HttpStatusCode.Forbidden=>"TOKEN_FORBIDDEN",(HttpStatusCode)429=>"RATE_LIMITED",_ when (int)status>=500=>"KASPI_UNAVAILABLE",_=>"KASPI_REQUEST_FAILED" };
    private static string? Text(JsonElement attributes,string name)=>attributes.TryGetProperty(name,out var value)&&value.ValueKind is not(JsonValueKind.Null or JsonValueKind.Undefined)?value.ToString():null;
    private static decimal? DecimalValue(JsonElement attributes,string name)=>attributes.TryGetProperty(name,out var value)&&value.TryGetDecimal(out var parsed)?parsed:null;
    private static DateTimeOffset? DateValue(JsonElement attributes,string name)
    {
        if(!attributes.TryGetProperty(name,out var value)||value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)return null;
        if(value.TryGetInt64(out var milliseconds))return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        return DateTimeOffset.TryParse(value.ToString(),System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AssumeUniversal,out var parsed)?parsed:null;
    }
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

    public async Task ProcessOneAsync(CancellationToken ct)
    {
        await using var scope=scopes.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<SellerFinanceDbContext>();
        var now=DateTimeOffset.UtcNow;var job=await(from candidate in db.SyncJobs join organization in db.Organizations on candidate.OrganizationId equals organization.Id join subscription in db.Subscriptions on organization.Id equals subscription.OrganizationId where organization.Status=="Active"&&(subscription.Status==SubscriptionStatus.Active||subscription.Status==SubscriptionStatus.Trialing)&&subscription.PeriodEnd>now&&(candidate.Status==SyncJobStatus.Queued||candidate.Status==SyncJobStatus.RetryScheduled)&&candidate.NextAttemptAt<=now&&!db.OrganizationFeatureFlags.Any(f=>f.OrganizationId==candidate.OrganizationId&&f.Key=="KaspiSync"&&!f.Enabled) orderby candidate.CreatedAt select candidate).FirstOrDefaultAsync(ct);
        if(job is null){var due=await(from dueConnection in db.MarketplaceConnections.AsNoTracking() join organization in db.Organizations.AsNoTracking() on dueConnection.OrganizationId equals organization.Id join subscription in db.Subscriptions.AsNoTracking() on organization.Id equals subscription.OrganizationId where organization.Status=="Active"&&(subscription.Status==SubscriptionStatus.Active||subscription.Status==SubscriptionStatus.Trialing)&&subscription.PeriodEnd>now&&dueConnection.Status==MarketplaceConnectionStatus.Active&&(!dueConnection.LastSuccessfulSyncAt.HasValue||dueConnection.LastSuccessfulSyncAt<DateTimeOffset.UtcNow.AddMinutes(-15))&&!db.OrganizationFeatureFlags.Any(f=>f.OrganizationId==dueConnection.OrganizationId&&f.Key=="KaspiSync"&&!f.Enabled) select dueConnection).FirstOrDefaultAsync(ct);if(due is not null){var subscription=await Subscriptions.GetAsync(db,due.OrganizationId,ct);var windowTo=DateTimeOffset.UtcNow;db.SyncJobs.Add(new(){Id=Guid.NewGuid(),OrganizationId=due.OrganizationId,MarketplaceConnectionId=due.Id,WindowFrom=due.LastSuccessfulSyncAt.HasValue?windowTo.AddDays(-14):PlanLimits.InitialHistoryFrom(subscription.Plan,windowTo),WindowTo=windowTo});await db.SaveChangesAsync(ct);}return;}
        job.Status=SyncJobStatus.Running; job.StartedAt=DateTimeOffset.UtcNow; job.Attempt++; await db.SaveChangesAsync(ct);
        var connection=await db.MarketplaceConnections.SingleAsync(x=>x.Id==job.MarketplaceConnectionId,ct);
        KaspiResult result;
        try { var token=scope.ServiceProvider.GetRequiredService<TokenCipher>().Decrypt(connection); result=await scope.ServiceProvider.GetRequiredService<KaspiClient>().GetOrdersAsync(token,job.WindowFrom,job.WindowTo,ct); }
        catch(CryptographicException) { result=new(false,0,"TOKEN_DECRYPTION_FAILED",[]); }
        if(result.Success)
        {
            await KaspiOrderImporter.UpsertAsync(db,job.OrganizationId,job.MarketplaceConnectionId,result.Orders,ct);
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

}

public static class KaspiOrderImporter
{
    public static async Task UpsertAsync(SellerFinanceDbContext db,string organizationId,Guid connectionId,IReadOnlyList<KaspiOrderDto> sources,CancellationToken ct=default)
    {
        foreach(var source in sources)
        {
            var order=await db.Orders.Include(x=>x.Lines).SingleOrDefaultAsync(x=>x.MarketplaceConnectionId==connectionId&&x.ExternalId==source.Id,ct);var isNew=order is null;
            if(order is null){order=new(){Id=Guid.NewGuid().ToString("N"),OrganizationId=organizationId,MarketplaceConnectionId=connectionId,ExternalId=source.Id};db.Orders.Add(order);}var mappedStatus=MapStatus(source.Status);if(isNew||order.Status!=mappedStatus)db.OrderStatusHistory.Add(new(){Id=Guid.NewGuid(),OrganizationId=organizationId,OrderId=order.Id,Status=mappedStatus,ExternalStatus=source.Status.Trim().ToUpperInvariant(),ChangedAt=DateTimeOffset.UtcNow});
            order.Code=source.Code;order.TotalPrice=source.TotalPrice;order.PaymentMode=source.PaymentMode;order.SellerDeliveryCost=source.SellerDeliveryCost;
            order.Date=DateOnly.FromDateTime(source.CreatedAt.UtcDateTime);order.CompletionDate=source.CompletedAt.HasValue?DateOnly.FromDateTime(source.CompletedAt.Value.UtcDateTime):null;order.Status=mappedStatus;order.CalculationDateFallback=order.Status==OrderStatus.Completed&&order.CompletionDate is null;
            if(source.Lines.Count>0){var sourceIds=source.Lines.Select(x=>x.EntryId).ToHashSet();order.Lines.RemoveAll(x=>x.ExternalId is null&&x.ProductId=="kaspi-unmapped"||x.ExternalId is not null&&!sourceIds.Contains(x.ExternalId));}
            var hasCompleteItemDelivery=source.Lines.Count>0&&source.Lines.All(x=>x.ItemDeliveryCost.HasValue);var allocatedDelivery=hasCompleteItemDelivery?null:FinanceCalculator.AllocateByRevenue(source.SellerDeliveryCost,source.Lines.Select(x=>x.Revenue).ToArray());
            for(var index=0;index<source.Lines.Count;index++)
            {
                var item=source.Lines[index];var product=await db.Products.SingleOrDefaultAsync(x=>x.OrganizationId==organizationId&&x.Sku==item.ProductCode,ct);
                if(product is null){product=new(){Id=Guid.NewGuid().ToString("N"),OrganizationId=organizationId,Sku=item.ProductCode,Name=item.Name,Category=item.Category,ExternalProductId=item.ExternalProductId};db.Products.Add(product);}else{product.Name=item.Name;product.Category=item.Category;product.ExternalProductId=item.ExternalProductId??product.ExternalProductId;}
                var line=order.Lines.SingleOrDefault(x=>x.ExternalId==item.EntryId);if(line is null){line=new(){Id=Guid.NewGuid(),OrderId=order.Id,ExternalId=item.EntryId};order.Lines.Add(line);}
                line.ProductId=product.Id;line.Quantity=item.Quantity;line.Revenue=item.Revenue;line.BasePrice=item.BasePrice;line.ItemDeliveryCost=item.ItemDeliveryCost;line.Delivery=hasCompleteItemDelivery?item.ItemDeliveryCost!.Value:allocatedDelivery![index];
            }
        }
    }
    private static OrderStatus MapStatus(string value)=>value.ToUpperInvariant() switch{"COMPLETED" or "DELIVERED"=>OrderStatus.Completed,"RETURNED"=>OrderStatus.Returned,"CANCELLED" or "CANCELED"=>OrderStatus.Cancelled,_=>OrderStatus.Pending};
}
