using System.Globalization;
using System.IO.Compression;
using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;
using Npgsql;

namespace SellerFinance.Api;

public sealed record RawCostRow(int RowNumber,string Sku,string Cost,string EffectiveFrom);

public sealed class CostImportService(SellerFinanceDbContext db)
{
    private const int MaxFileBytes=5*1024*1024;
    private const int MaxRows=10_000;
    private const int MaxSkuLength=200;
    private const long MaxUncompressedXlsxBytes=25L*1024*1024;
    private static readonly SemaphoreSlim NonRelationalConfirmLock=new(1,1);

    public async Task<CostImportJobEntity> PreviewAsync(string tenant,string userId,IFormFile file,CancellationToken ct)
    {
        if(file.Length==0||file.Length>MaxFileBytes)throw new CostImportException("Размер файла должен быть от 1 байта до 5 МБ");
        var extension=Path.GetExtension(file.FileName).ToLowerInvariant();
        if(extension is not ".csv" and not ".xlsx")throw new CostImportException("Поддерживаются только CSV и XLSX");
        await using var memory=new MemoryStream();await file.CopyToAsync(memory,ct);memory.Position=0;
        ValidateSignature(memory,extension);
        IReadOnlyList<RawCostRow> raw;
        try{raw=extension==".xlsx"?ReadXlsx(memory):ReadCsv(memory);}
        catch(CostImportException){throw;}
        catch(Exception ex) when(ex is not OperationCanceledException){throw new CostImportException("Не удалось прочитать файл. Проверьте формат и заголовки колонок");}
        if(raw.Count>MaxRows)throw new CostImportException("В одном импорте допускается не более 10 000 строк");
        var products=await db.Products.AsNoTracking().Where(x=>x.OrganizationId==tenant).ToDictionaryAsync(x=>x.Sku,StringComparer.OrdinalIgnoreCase,ct);
        var job=new CostImportJobEntity{Id=Guid.NewGuid(),OrganizationId=tenant,CreatedByUserId=userId,FileNameSafe=SafeFileName(file.FileName),Source=extension==".xlsx"?CostSource.XlsxImport:CostSource.CsvImport,TotalRows=raw.Count};
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var source in raw)
        {
            var sku=source.Sku.Trim();decimal? cost=ParseMoney(source.Cost);DateOnly? date=ParseDate(source.EffectiveFrom);var status="Valid";string? error=null;products.TryGetValue(sku,out var product);
            if(String.IsNullOrWhiteSpace(sku)){status="Error";error="SKU обязателен";}
            else if(sku.Length>MaxSkuLength){status="Error";error=$"SKU не должен превышать {MaxSkuLength} символов";}
            else if(cost is null||cost<=0){status="Error";error="Некорректная себестоимость";}
            else if(date is null){status="Error";error="Некорректная дата EffectiveFrom";}
            else if(!seen.Add($"{sku}|{date:yyyy-MM-dd}")){status="Duplicate";error="Дубликат SKU и даты в файле";}
            else if(product is null){status="Unmatched";error="Товар с таким SKU не найден";}
            else if(await db.ProductCostHistory.AnyAsync(x=>x.OrganizationId==tenant&&x.ProductId==product.Id&&x.EffectiveFrom==date,ct)){status="Duplicate";error="Себестоимость на эту дату уже существует";}
            var row=new CostImportRowEntity{Id=Guid.NewGuid(),ImportJobId=job.Id,RowNumber=source.RowNumber,Sku=sku,ProductId=product?.Id,CostAmount=cost,EffectiveFrom=date,Status=status,Error=error};db.CostImportRows.Add(row);
            switch(status){case "Valid":job.MatchedRows++;job.ExpectedChanges++;break;case "Unmatched":job.UnmatchedRows++;break;case "Duplicate":job.DuplicateRows++;break;default:job.ErrorRows++;break;}
        }
        db.CostImportJobs.Add(job);await db.SaveChangesAsync(ct);return job;
    }

    public async Task<int> ConfirmAsync(Guid jobId,string tenant,string userId,CancellationToken ct)
    {
        if(!db.Database.IsRelational())await NonRelationalConfirmLock.WaitAsync(ct);
        try
        {
            await using var transaction=db.Database.IsRelational()?await db.Database.BeginTransactionAsync(ct):null;
            var exists=await db.CostImportJobs.AsNoTracking().AnyAsync(x=>x.Id==jobId&&x.OrganizationId==tenant,ct);
            if(!exists)throw new KeyNotFoundException();
            var now=DateTimeOffset.UtcNow;
            var claimed=db.Database.IsRelational()
                ?await db.CostImportJobs.Where(x=>x.Id==jobId&&x.OrganizationId==tenant&&x.Status==CostImportStatus.Preview&&x.ExpiresAt>now).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,CostImportStatus.Applied).SetProperty(x=>x.AppliedAt,now),ct)
                :await ClaimNonRelationalAsync(jobId,tenant,now,ct);
            if(claimed!=1)throw new CostImportException("Preview уже применён или истёк");
            var job=await db.CostImportJobs.AsNoTracking().SingleAsync(x=>x.Id==jobId&&x.OrganizationId==tenant,ct);
            var rows=await db.CostImportRows.AsNoTracking().Where(x=>x.ImportJobId==jobId&&x.Status=="Valid").ToArrayAsync(ct);
            foreach(var row in rows)db.ProductCostHistory.Add(new(){Id=Guid.NewGuid(),OrganizationId=tenant,ProductId=row.ProductId!,CostAmount=row.CostAmount!.Value,EffectiveFrom=row.EffectiveFrom!.Value,Source=job.Source,ImportJobId=job.Id,CreatedByUserId=userId});
            try{await db.SaveChangesAsync(ct);}catch(DbUpdateException ex) when(ex.InnerException is PostgresException{SqlState:PostgresErrorCodes.UniqueViolation}){throw new CostImportException("Себестоимость изменилась после preview. Создайте новый preview");}
            if(transaction is not null)await transaction.CommitAsync(ct);return rows.Length;
        }
        finally{if(!db.Database.IsRelational())NonRelationalConfirmLock.Release();}
    }

    public static object ToPreview(CostImportJobEntity job,IEnumerable<CostImportRowEntity> rows)=>new{job.Id,job.Status,job.TotalRows,job.MatchedRows,job.UnmatchedRows,job.ErrorRows,job.DuplicateRows,job.ExpectedChanges,job.ExpiresAt,rows=rows.Select(x=>new{x.RowNumber,x.Sku,x.ProductId,x.CostAmount,x.EffectiveFrom,x.Status,x.Error})};

    private static IReadOnlyList<RawCostRow> ReadXlsx(Stream stream)
    {
        ValidateXlsxArchive(stream);stream.Position=0;
        using var workbook=new XLWorkbook(stream);var sheet=workbook.Worksheets.FirstOrDefault()??throw new CostImportException("Файл не содержит листов");var used=sheet.RangeUsed()??throw new CostImportException("Файл пуст");
        if(used.RowCount()>MaxRows+1||used.ColumnCount()>100)throw new CostImportException("Лист превышает лимит 10 000 строк или 100 колонок");
        if(used.CellsUsed().Any(x=>x.HasFormula))throw new CostImportException("Формулы в файле импорта не поддерживаются");
        var headers=Headers(used.FirstRow().Cells().Select((c,i)=>(c.GetString(),i+1)));
        var sku=Find(headers,"sku","артикул");var cost=Find(headers,"cost","costamount","себестоимость");var date=Find(headers,"effectivefrom","дата","датадействия");
        return used.RowsUsed().Skip(1).Where(r=>!r.IsEmpty()).Select(r=>new RawCostRow(r.RowNumber(),r.Cell(sku).GetString(),r.Cell(cost).GetFormattedString(),r.Cell(date).GetFormattedString())).ToArray();
    }

    private static IReadOnlyList<RawCostRow> ReadCsv(Stream stream)
    {
        stream.Position=0;using var reader=new StreamReader(stream,System.Text.Encoding.UTF8,true,leaveOpen:true);var first=reader.ReadLine()??throw new CostImportException("Файл пуст");stream.Position=0;reader.DiscardBufferedData();
        using var parser=new TextFieldParser(stream,System.Text.Encoding.UTF8,true){TextFieldType=FieldType.Delimited,HasFieldsEnclosedInQuotes=true};parser.SetDelimiters(first.Count(x=>x==';')>=first.Count(x=>x==',')?";":",");
        var header=Headers((parser.ReadFields()??[]).Select((x,i)=>(x,i)));var sku=Find(header,"sku","артикул");var cost=Find(header,"cost","costamount","себестоимость");var date=Find(header,"effectivefrom","дата","датадействия");var rows=new List<RawCostRow>();var number=1;
        try{while(!parser.EndOfData){number++;var fields=parser.ReadFields()??[];if(fields.All(String.IsNullOrWhiteSpace))continue;if(rows.Count==MaxRows)throw new CostImportException("В одном импорте допускается не более 10 000 строк");rows.Add(new(number,Value(fields,sku),Value(fields,cost),Value(fields,date)));}}catch(MalformedLineException){throw new CostImportException($"Некорректная CSV-строка {number}");}return rows;
    }

    private async Task<int> ClaimNonRelationalAsync(Guid jobId,string tenant,DateTimeOffset now,CancellationToken ct){var job=await db.CostImportJobs.SingleOrDefaultAsync(x=>x.Id==jobId&&x.OrganizationId==tenant,ct);if(job is null||job.Status!=CostImportStatus.Preview||job.ExpiresAt<=now)return 0;job.Status=CostImportStatus.Applied;job.AppliedAt=now;return 1;}
    private static Dictionary<string,int> Headers(IEnumerable<(string Name,int Index)> values){var result=new Dictionary<string,int>();foreach(var (name,index) in values){var normalized=Normalize(name);if(String.IsNullOrEmpty(normalized))continue;if(!result.TryAdd(normalized,index))throw new CostImportException($"Повторяющаяся колонка: {name.Trim()}");}return result;}
    private static void ValidateSignature(Stream stream,string extension){Span<byte> prefix=stackalloc byte[4];var read=stream.Read(prefix);stream.Position=0;if(extension==".xlsx"&&(read<4||prefix[0]!=0x50||prefix[1]!=0x4B||prefix[2]!=0x03||prefix[3]!=0x04))throw new CostImportException("Содержимое файла не соответствует формату XLSX");if(extension==".csv"&&prefix[..read].Contains((byte)0))throw new CostImportException("CSV содержит недопустимые бинарные данные");}
    private static void ValidateXlsxArchive(Stream stream){stream.Position=0;using var archive=new ZipArchive(stream,ZipArchiveMode.Read,true);long total=0;foreach(var entry in archive.Entries){if(entry.Length>MaxUncompressedXlsxBytes||total>MaxUncompressedXlsxBytes-entry.Length)throw new CostImportException("Распакованный XLSX превышает лимит 25 МБ");total+=entry.Length;}if(!archive.Entries.Any(x=>x.FullName.Equals("[Content_Types].xml",StringComparison.OrdinalIgnoreCase)))throw new CostImportException("Некорректная структура XLSX");}
    private static string SafeFileName(string value){var normalized=value.Replace('\\','/');var name=normalized[(normalized.LastIndexOf('/')+1)..];name=new String(name.Where(x=>!Char.IsControl(x)).ToArray()).Trim();if(String.IsNullOrWhiteSpace(name))return "import";return name.Length<=255?name:name[..255];}

    private static int Find(Dictionary<string,int> headers,params string[] names){foreach(var name in names)if(headers.TryGetValue(Normalize(name),out var index))return index;throw new CostImportException($"Нет обязательной колонки: {names[0]}");}
    private static string Value(string[] fields,int index)=>index<fields.Length?fields[index]:"";
    private static string Normalize(string value)=>new(value.Trim().ToLowerInvariant().Where(Char.IsLetterOrDigit).ToArray());
    private static decimal? ParseMoney(string value){value=value.Trim().Replace("₸","").Replace(" ","");if(Decimal.TryParse(value,NumberStyles.Number,CultureInfo.GetCultureInfo("ru-RU"),out var ru))return ru;if(Decimal.TryParse(value,NumberStyles.Number,CultureInfo.InvariantCulture,out var inv))return inv;return null;}
    private static DateOnly? ParseDate(string value){if(DateOnly.TryParse(value,CultureInfo.GetCultureInfo("ru-RU"),DateTimeStyles.None,out var ru))return ru;if(DateOnly.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.None,out var inv))return inv;return null;}
}

public sealed class CostImportException(string message):Exception(message);
