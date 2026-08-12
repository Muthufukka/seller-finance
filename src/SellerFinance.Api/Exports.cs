using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace SellerFinance.Api;

public sealed record ExportArtifact(byte[] Content,string ContentType,string FileName,int RowCount);

public sealed class ExportBuilder(SellerFinanceDbContext db)
{
    public async Task<ExportArtifact> BuildAsync(ExportJobEntity job,CancellationToken ct)
    {
        object[] source=job.ReportType.ToLowerInvariant() switch
        {
            "orders"=>await AllOrdersAsync(job),
            "abc"=>await DbAnalytics.AbcAsync(db,job.OrganizationId,"profit",job.DateFrom,job.DateTo),
            "missingcosts"=>(await DbAnalytics.ProductsAsync(db,job.OrganizationId,job.DateFrom,job.DateTo)).Where(IsMissingCost).ToArray(),
            _=>await DbAnalytics.ProductsAsync(db,job.OrganizationId,job.DateFrom,job.DateTo)
        };
        var rows=JsonSerializer.SerializeToElement(source).EnumerateArray().ToArray();var columns=Columns(job.ReportType);
        return job.Format.Equals("csv",StringComparison.OrdinalIgnoreCase)?BuildCsv(job,rows,columns):BuildXlsx(job,rows,columns);
    }

    private async Task<object[]> AllOrdersAsync(ExportJobEntity job)
    {
        var rows=new List<object>();for(var page=1;;page++){var result=JsonSerializer.SerializeToElement(await DbAnalytics.OrdersAsync(db,job.OrganizationId,from:job.DateFrom,to:job.DateTo,page:page,pageSize:100));rows.AddRange(result.GetProperty("items").EnumerateArray().Select(x=>(object)x.Clone()));if(page>=result.GetProperty("totalPages").GetInt32())break;}return rows.ToArray();
    }

    private static bool IsMissingCost(object value){var json=JsonSerializer.SerializeToElement(value);return json.TryGetProperty("cost",out var cost)&&cost.ValueKind==JsonValueKind.Null;}
    private static (string Key,string Header)[] Columns(string report)=>report.ToLowerInvariant() switch
    {
        "orders"=>[("externalId","Order code"),("date","Date"),("status","Status"),("amount","Amount"),("fees","Fees"),("delivery","Delivery"),("profit","Profit"),("complete","Complete")],
        "abc"=>[("sku","SKU"),("name","Name"),("group","ABC"),("value","Value"),("revenue","Revenue"),("profit","Profit"),("units","Units"),("cumulativePct","Cumulative %")],
        "missingcosts"=>[("sku","SKU"),("name","Name"),("units","Units"),("revenue","Revenue"),("coveragePct","Coverage %")],
        _=>[("sku","SKU"),("name","Name"),("units","Units"),("revenue","Revenue"),("cogs","COGS"),("profit","Profit"),("margin","Margin %"),("cost","Current cost"),("coveragePct","Coverage %")]
    };

    private static ExportArtifact BuildCsv(ExportJobEntity job,JsonElement[] rows,(string Key,string Header)[] columns)
    {
        var text=new StringBuilder();text.Append('\uFEFF').AppendLine(String.Join(';',columns.Select(x=>Escape(x.Header))));foreach(var row in rows)text.AppendLine(String.Join(';',columns.Select(x=>Escape(Value(row,x.Key)))));return new(Encoding.UTF8.GetBytes(text.ToString()),"text/csv; charset=utf-8",FileName(job,"csv"),rows.Length);
    }
    private static ExportArtifact BuildXlsx(ExportJobEntity job,JsonElement[] rows,(string Key,string Header)[] columns)
    {
        using var workbook=new XLWorkbook();var sheet=workbook.AddWorksheet("Report");for(var c=0;c<columns.Length;c++)sheet.Cell(1,c+1).Value=columns[c].Header;for(var r=0;r<rows.Length;r++)for(var c=0;c<columns.Length;c++)SetCell(sheet.Cell(r+2,c+1),rows[r],columns[c].Key);sheet.Range(1,1,1,columns.Length).Style.Font.Bold=true;sheet.Range(1,1,1,columns.Length).Style.Fill.BackgroundColor=XLColor.FromHtml("#DDE9DF");sheet.SheetView.FreezeRows(1);sheet.Columns().AdjustToContents(8,42);using var stream=new MemoryStream();workbook.SaveAs(stream);return new(stream.ToArray(),"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",FileName(job,"xlsx"),rows.Length);
    }
    private static void SetCell(IXLCell cell,JsonElement row,string key){if(!row.TryGetProperty(key,out var value)||value.ValueKind==JsonValueKind.Null)return;if(value.ValueKind==JsonValueKind.Number&&value.TryGetDecimal(out var number)){cell.Value=number;cell.Style.NumberFormat.Format="#,##0.00";}else if(value.ValueKind is JsonValueKind.True or JsonValueKind.False)cell.Value=value.GetBoolean();else cell.Value=value.ToString();}
    private static string Value(JsonElement row,string key)=>row.TryGetProperty(key,out var value)&&value.ValueKind!=JsonValueKind.Null?value.ToString():"";
    private static string Escape(string value)=>$"\"{value.Replace("\"","\"\"")}\"";
    private static string FileName(ExportJobEntity job,string extension)=>$"seller-finance-{job.ReportType.ToLowerInvariant()}-{DateTime.UtcNow:yyyyMMddHHmm}.{extension}";
}

public sealed class ExportWorker(IServiceScopeFactory scopes,ILogger<ExportWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested){try{await ProcessAsync(stoppingToken);}catch(Exception ex){logger.LogError(ex,"Export worker iteration failed");}await Task.Delay(TimeSpan.FromSeconds(5),stoppingToken);}
    }
    private async Task ProcessAsync(CancellationToken ct)
    {
        await using var scope=scopes.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<SellerFinanceDbContext>();
        await db.ExportJobs.Where(x=>x.ExpiresAt<DateTimeOffset.UtcNow&&x.FileContent!=null).ExecuteUpdateAsync(x=>x.SetProperty(y=>y.FileContent,(byte[]?)null).SetProperty(y=>y.Status,ExportJobStatus.Expired),ct);
        var job=await db.ExportJobs.OrderBy(x=>x.CreatedAt).FirstOrDefaultAsync(x=>x.Status==ExportJobStatus.Queued,ct);if(job is null)return;job.Status=ExportJobStatus.Running;await db.SaveChangesAsync(ct);
        try{var artifact=await scope.ServiceProvider.GetRequiredService<ExportBuilder>().BuildAsync(job,ct);job.FileContent=artifact.Content;job.ContentType=artifact.ContentType;job.FileName=artifact.FileName;job.RowCount=artifact.RowCount;job.Status=ExportJobStatus.Succeeded;job.CompletedAt=DateTimeOffset.UtcNow;}
        catch(Exception ex){logger.LogWarning("Export {ExportId} failed with {ErrorType}",job.Id,ex.GetType().Name);job.Status=ExportJobStatus.Failed;job.ErrorCode="EXPORT_FAILED";job.CompletedAt=DateTimeOffset.UtcNow;}await db.SaveChangesAsync(ct);
    }
}
