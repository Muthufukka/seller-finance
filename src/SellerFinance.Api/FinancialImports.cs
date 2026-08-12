using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;
using Npgsql;

namespace SellerFinance.Api;

public sealed class FinancialImportException(string message) : Exception(message);

public sealed class FinancialImportService(SellerFinanceDbContext db)
{
    private const int MaxRows = 10_000;
    private const long MaxBytes = 5 * 1024 * 1024;
    private const long MaxUncompressedXlsxBytes=25L*1024*1024;
    private static readonly SemaphoreSlim NonRelationalConfirmLock=new(1,1);

    public async Task<FinancialImportJobEntity> PreviewAsync(FinancialImportType type,string tenant,string userId,IFormFile file,CancellationToken ct)
    {
        if(file.Length is <=0 or >MaxBytes)throw new FinancialImportException("Размер файла должен быть от 1 байта до 5 МБ");
        var extension=Path.GetExtension(file.FileName).ToLowerInvariant();
        if(extension is not ".csv" and not ".xlsx")throw new FinancialImportException("Поддерживаются только CSV и XLSX");
        await using var memory=new MemoryStream();await file.CopyToAsync(memory,ct);memory.Position=0;
        List<Dictionary<string,string>> raw;
        try{ValidateSignature(memory,extension);raw=extension==".xlsx"?ReadXlsx(memory):ReadCsv(memory);}
        catch(FinancialImportException){throw;}
        catch(Exception ex) when(ex is not OperationCanceledException){throw new FinancialImportException("Не удалось прочитать файл. Проверьте формат и заголовки колонок");}
        if(raw.Count>MaxRows)throw new FinancialImportException($"В одном импорте допускается не более {MaxRows:N0} строк");

        var job=new FinancialImportJobEntity{Id=Guid.NewGuid(),OrganizationId=tenant,CreatedByUserId=userId,Type=type,FileNameSafe=SafeFileName(file.FileName),TotalRows=raw.Count};
        if(type==FinancialImportType.Expenses)await BuildExpenseRows(job,raw,tenant,ct);else await BuildFeeRows(job,raw,tenant,ct);
        db.FinancialImportJobs.Add(job);await db.SaveChangesAsync(ct);return job;
    }

    private async Task BuildExpenseRows(FinancialImportJobEntity job,List<Dictionary<string,string>> raw,string tenant,CancellationToken ct)
    {
        Require(raw,"type","amount","date");
        var products=await db.Products.AsNoTracking().Where(x=>x.OrganizationId==tenant).ToDictionaryAsync(x=>x.Sku,StringComparer.OrdinalIgnoreCase,ct);
        var orders=await db.Orders.AsNoTracking().Where(x=>x.OrganizationId==tenant).ToDictionaryAsync(x=>x.ExternalId,StringComparer.OrdinalIgnoreCase,ct);
        var existing=(await db.Expenses.AsNoTracking().Where(x=>x.OrganizationId==tenant&&x.ImportFingerprint!=null).Select(x=>x.ImportFingerprint!).ToArrayAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var seen=new HashSet<string>(StringComparer.Ordinal);
        for(var i=0;i<raw.Count;i++)
        {
            var source=raw[i];var typeText=Get(source,"type");var amount=Money(Get(source,"amount"));var date=Date(Get(source,"date"));var periodEndText=Get(source,"periodend");var periodEnd=String.IsNullOrWhiteSpace(periodEndText)?null:Date(periodEndText);var sku=Get(source,"sku");var externalOrder=Get(source,"orderexternalid","orderid");var comment=Null(Get(source,"comment"));
            products.TryGetValue(sku,out var product);orders.TryGetValue(externalOrder,out var order);var status="Valid";string? error=null;
            var parsed=Enum.TryParse<ExpenseType>(typeText,true,out var expenseType);
            if(!parsed){status="Error";error="Некорректный Type";}else if(amount is null||amount<=0){status="Error";error="Некорректная сумма";}else if(date is null){status="Error";error="Некорректная дата";}else if(periodEndText.Length>0&&periodEnd is null){status="Error";error="Некорректная дата PeriodEnd";}else if(periodEnd.HasValue&&periodEnd<date){status="Error";error="PeriodEnd раньше Date";}else if(periodEnd.HasValue&&periodEnd.Value.DayNumber-date.Value.DayNumber>=366){status="Error";error="Период превышает 366 дней";}else if(sku.Length>0&&product is null){status="Error";error="SKU не найден в организации";}else if(externalOrder.Length>0&&order is null){status="Error";error="Заказ не найден в организации";}
            var fingerprint=status=="Valid"?Hash($"{expenseType}|{amount:0.####}|{date:yyyy-MM-dd}|{periodEnd:yyyy-MM-dd}|{product?.Id}|{order?.Id}|{comment}"):null;
            if(fingerprint is not null&&(!seen.Add(fingerprint)||existing.Contains(fingerprint))){status="Duplicate";error="Расход уже существует";}
            Add(job,new(){Id=Guid.NewGuid(),ImportJobId=job.Id,RowNumber=i+2,Status=status,Error=error,ExpenseType=parsed?expenseType:null,Amount=amount,Date=date,PeriodEnd=periodEnd,ProductId=product?.Id,OrderId=order?.Id,Comment=comment,Fingerprint=fingerprint});
        }
    }

    private async Task BuildFeeRows(FinancialImportJobEntity job,List<Dictionary<string,string>> raw,string tenant,CancellationToken ct)
    {
        Require(raw,"orderexternalid","amount");
        var orders=await db.Orders.AsNoTracking().Where(x=>x.OrganizationId==tenant).Select(x=>new{x.Id,x.ExternalId}).ToArrayAsync(ct);
        var orderMap=orders.ToDictionary(x=>x.ExternalId,StringComparer.OrdinalIgnoreCase);var orderIds=orders.Select(x=>x.Id).ToArray();
        var lines=await db.OrderLines.AsNoTracking().Where(x=>orderIds.Contains(x.OrderId)).ToArrayAsync(ct);
        var products=await db.Products.AsNoTracking().Where(x=>x.OrganizationId==tenant).ToDictionaryAsync(x=>x.Sku,x=>x.Id,StringComparer.OrdinalIgnoreCase,ct);
        var existing=await db.ActualFees.AsNoTracking().Where(x=>x.OrganizationId==tenant).ToDictionaryAsync(x=>x.OrderLineId,ct);var seen=new HashSet<Guid>();
        for(var i=0;i<raw.Count;i++)
        {
            var source=raw[i];var externalOrder=Get(source,"orderexternalid");var externalLine=Get(source,"lineexternalid");var sku=Get(source,"sku");var amount=Money(Get(source,"amount"));var externalRef=Null(Get(source,"externalref"));
            orderMap.TryGetValue(externalOrder,out var order);OrderLineEntity? line=null;var status="Valid";string? error=null;
            if(order is null){status="Error";error="Заказ не найден в организации";}
            else
            {
                var candidates=lines.Where(x=>x.OrderId==order.Id&&(externalLine.Length>0?String.Equals(x.ExternalId,externalLine,StringComparison.OrdinalIgnoreCase):(products.TryGetValue(sku,out var productId)&&x.ProductId==productId))).ToArray();
                if(candidates.Length!=1){status="Error";error=candidates.Length==0?"Строка заказа не найдена":"Найдено несколько строк заказа";}else line=candidates[0];
            }
            if(amount is null||amount<0){status="Error";error="Некорректная сумма";}
            if(status=="Valid"&&line is not null&&!seen.Add(line.Id)){status="Duplicate";error="Строка заказа повторяется в файле";}
            if(status=="Valid"&&line is not null&&existing.TryGetValue(line.Id,out var fee))status=fee.Amount==amount&&String.Equals(fee.ExternalRef,externalRef,StringComparison.Ordinal)?"Duplicate":"Update";
            Add(job,new(){Id=Guid.NewGuid(),ImportJobId=job.Id,RowNumber=i+2,Status=status,Error=error,Amount=amount,OrderId=order?.Id,OrderLineId=line?.Id,ExternalRef=externalRef});
        }
    }

    public async Task<int> ConfirmAsync(Guid jobId,string tenant,string userId,CancellationToken ct)
    {
        if(!db.Database.IsRelational())await NonRelationalConfirmLock.WaitAsync(ct);
        try
        {
            await using var transaction=db.Database.IsRelational()?await db.Database.BeginTransactionAsync(ct):null;
            if(!await db.FinancialImportJobs.AsNoTracking().AnyAsync(x=>x.Id==jobId&&x.OrganizationId==tenant,ct))throw new KeyNotFoundException();var now=DateTimeOffset.UtcNow;
            var claimed=db.Database.IsRelational()?await db.FinancialImportJobs.Where(x=>x.Id==jobId&&x.OrganizationId==tenant&&x.Status==FinancialImportStatus.Preview&&x.ExpiresAt>now).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,FinancialImportStatus.Applied).SetProperty(x=>x.AppliedAt,now),ct):await ClaimNonRelationalAsync(jobId,tenant,now,ct);if(claimed!=1)throw new FinancialImportException("Preview уже применён или истёк");
            var job=await db.FinancialImportJobs.AsNoTracking().SingleAsync(x=>x.Id==jobId&&x.OrganizationId==tenant,ct);var rows=await db.FinancialImportRows.AsNoTracking().Where(x=>x.ImportJobId==jobId&&(x.Status=="Valid"||x.Status=="Update")).ToArrayAsync(ct);
            if(job.Type==FinancialImportType.Expenses)foreach(var row in rows)db.Expenses.Add(new(){Id=Guid.NewGuid(),OrganizationId=tenant,Type=row.ExpenseType!.Value,Amount=row.Amount!.Value,Date=row.Date!.Value,PeriodEnd=row.PeriodEnd,ProductId=row.ProductId,OrderId=row.OrderId,Comment=row.Comment,Source=ExpenseSource.Import,ImportJobId=job.Id,ImportFingerprint=row.Fingerprint,CreatedByUserId=userId});
            else foreach(var row in rows){var fee=await db.ActualFees.SingleOrDefaultAsync(x=>x.OrganizationId==tenant&&x.OrderLineId==row.OrderLineId,ct);if(fee is null)db.ActualFees.Add(new(){Id=Guid.NewGuid(),OrganizationId=tenant,OrderLineId=row.OrderLineId!.Value,Amount=row.Amount!.Value,Source="Import",ImportJobId=job.Id,ExternalRef=row.ExternalRef,CreatedByUserId=userId});else{fee.Amount=row.Amount!.Value;fee.Source="Import";fee.ImportJobId=job.Id;fee.ExternalRef=row.ExternalRef;}}
            try{await db.SaveChangesAsync(ct);}catch(DbUpdateException ex)when(ex.InnerException is PostgresException{SqlState:PostgresErrorCodes.UniqueViolation}){throw new FinancialImportException("Финансовые данные изменились после preview. Создайте новый preview");}if(transaction is not null)await transaction.CommitAsync(ct);return rows.Length;
        }
        finally{if(!db.Database.IsRelational())NonRelationalConfirmLock.Release();}
    }

    public static object ToPreview(FinancialImportJobEntity job,IEnumerable<FinancialImportRowEntity> rows)=>new{job.Id,type=job.Type.ToString(),status=job.Status.ToString(),job.TotalRows,job.ValidRows,job.UpdateRows,job.DuplicateRows,job.ErrorRows,job.ExpectedChanges,job.ExpiresAt,rows=rows.Select(x=>new{x.RowNumber,x.Status,x.Error,type=x.ExpenseType?.ToString(),x.Amount,x.Date,x.PeriodEnd,x.ProductId,x.OrderId,x.OrderLineId,x.Comment,x.ExternalRef})};

    private void Add(FinancialImportJobEntity job,FinancialImportRowEntity row){db.FinancialImportRows.Add(row);switch(row.Status){case "Valid":job.ValidRows++;job.ExpectedChanges++;break;case "Update":job.UpdateRows++;job.ExpectedChanges++;break;case "Duplicate":job.DuplicateRows++;break;default:job.ErrorRows++;break;}}
    private static void Require(List<Dictionary<string,string>> rows,params string[] names){if(rows.Count==0)throw new FinancialImportException("Файл не содержит строк");foreach(var name in names)if(!rows[0].ContainsKey(Normalize(name)))throw new FinancialImportException($"Нет обязательной колонки: {name}");}
    private async Task<int> ClaimNonRelationalAsync(Guid jobId,string tenant,DateTimeOffset now,CancellationToken ct){var job=await db.FinancialImportJobs.SingleOrDefaultAsync(x=>x.Id==jobId&&x.OrganizationId==tenant,ct);if(job is null||job.Status!=FinancialImportStatus.Preview||job.ExpiresAt<=now)return 0;job.Status=FinancialImportStatus.Applied;job.AppliedAt=now;return 1;}
    private static List<Dictionary<string,string>> ReadXlsx(Stream stream){ValidateXlsxArchive(stream);stream.Position=0;using var book=new XLWorkbook(stream);var sheet=book.Worksheets.FirstOrDefault()??throw new FinancialImportException("Файл не содержит листов");var range=sheet.RangeUsed()??throw new FinancialImportException("Файл пуст");if(range.RowCount()>MaxRows+1||range.ColumnCount()>100)throw new FinancialImportException("Лист превышает лимит 10 000 строк или 100 колонок");if(range.CellsUsed().Any(x=>x.HasFormula))throw new FinancialImportException("Формулы в файле импорта не поддерживаются");var headers=Headers(range.FirstRow().Cells().Select((c,i)=>(c.GetString(),i+1)));return range.RowsUsed().Skip(1).Where(x=>!x.IsEmpty()).Select(row=>headers.ToDictionary(x=>x.Name,x=>row.Cell(x.Index).GetFormattedString(),StringComparer.OrdinalIgnoreCase)).ToList();}
    private static List<Dictionary<string,string>> ReadCsv(Stream stream){stream.Position=0;using var reader=new StreamReader(stream,Encoding.UTF8,true,leaveOpen:true);var first=reader.ReadLine()??throw new FinancialImportException("Файл пуст");stream.Position=0;reader.DiscardBufferedData();using var parser=new TextFieldParser(stream,Encoding.UTF8,true){TextFieldType=FieldType.Delimited,HasFieldsEnclosedInQuotes=true};parser.SetDelimiters(first.Count(x=>x==';')>=first.Count(x=>x==',')?";":",");var headers=Headers((parser.ReadFields()??[]).Select((x,i)=>(x,i)));var result=new List<Dictionary<string,string>>();try{while(!parser.EndOfData){var values=parser.ReadFields()??[];if(values.All(String.IsNullOrWhiteSpace))continue;if(result.Count==MaxRows)throw new FinancialImportException("В одном импорте допускается не более 10 000 строк");result.Add(headers.ToDictionary(x=>x.Name,x=>x.Index<values.Length?values[x.Index]:"",StringComparer.OrdinalIgnoreCase));}}catch(MalformedLineException){throw new FinancialImportException($"Некорректная CSV-строка {result.Count+2}");}return result;}
    private static (string Name,int Index)[] Headers(IEnumerable<(string Name,int Index)> source){var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var result=new List<(string,int)>();foreach(var (name,index) in source){var normalized=Normalize(name);if(String.IsNullOrWhiteSpace(normalized))continue;if(!seen.Add(normalized))throw new FinancialImportException($"Повторяющаяся колонка: {name.Trim()}");result.Add((normalized,index));}return result.ToArray();}
    private static void ValidateSignature(Stream stream,string extension){Span<byte> prefix=stackalloc byte[4];var read=stream.Read(prefix);stream.Position=0;if(extension==".xlsx"&&(read<4||prefix[0]!=0x50||prefix[1]!=0x4B||prefix[2]!=0x03||prefix[3]!=0x04))throw new FinancialImportException("Содержимое файла не соответствует формату XLSX");if(extension==".csv"&&prefix[..read].Contains((byte)0))throw new FinancialImportException("CSV содержит недопустимые бинарные данные");}
    private static void ValidateXlsxArchive(Stream stream){stream.Position=0;using var archive=new ZipArchive(stream,ZipArchiveMode.Read,true);long total=0;foreach(var entry in archive.Entries){if(entry.Length>MaxUncompressedXlsxBytes||total>MaxUncompressedXlsxBytes-entry.Length)throw new FinancialImportException("Распакованный XLSX превышает лимит 25 МБ");total+=entry.Length;}if(!archive.Entries.Any(x=>x.FullName.Equals("[Content_Types].xml",StringComparison.OrdinalIgnoreCase)))throw new FinancialImportException("Некорректная структура XLSX");}
    private static string SafeFileName(string value){var normalized=value.Replace('\\','/');var name=normalized[(normalized.LastIndexOf('/')+1)..];name=new String(name.Where(x=>!Char.IsControl(x)).ToArray()).Trim();if(String.IsNullOrWhiteSpace(name))return "import";return name.Length<=255?name:name[..255];}
    private static string Get(Dictionary<string,string> row,params string[] names){foreach(var name in names)if(row.TryGetValue(Normalize(name),out var value))return value.Trim();return "";}
    private static string? Null(string value)=>String.IsNullOrWhiteSpace(value)?null:value.Trim();
    private static string Normalize(string value)=>new(value.Trim().ToLowerInvariant().Where(Char.IsLetterOrDigit).ToArray());
    private static decimal? Money(string value){value=value.Trim().Replace("₸","").Replace(" ","");if(Decimal.TryParse(value,NumberStyles.Number,CultureInfo.GetCultureInfo("ru-RU"),out var ru))return ru;if(Decimal.TryParse(value,NumberStyles.Number,CultureInfo.InvariantCulture,out var invariant))return invariant;return null;}
    private static DateOnly? Date(string value){if(DateOnly.TryParse(value,CultureInfo.GetCultureInfo("ru-RU"),DateTimeStyles.None,out var ru))return ru;if(DateOnly.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.None,out var invariant))return invariant;return null;}
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
