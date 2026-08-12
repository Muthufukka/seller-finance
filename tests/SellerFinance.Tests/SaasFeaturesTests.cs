using System.Net;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SellerFinance.Api;

namespace SellerFinance.Tests;

public sealed class SaasFeaturesTests
{
    [Fact]
    public async Task ExportBuilder_Creates_Utf8_Csv_Without_Missing_Cost_As_Zero()
    {
        await using var db=CreateDb();db.Products.Add(new(){Id="p1",OrganizationId="org",Sku="SKU-1",Name="Product"});await db.SaveChangesAsync();
        var artifact=await new ExportBuilder(db).BuildAsync(Job("csv","MissingCosts"),CancellationToken.None);var text=Encoding.UTF8.GetString(artifact.Content);
        Assert.StartsWith("\uFEFF",text);Assert.Contains("SKU-1",text);Assert.Equal(1,artifact.RowCount);
    }

    [Fact]
    public async Task ExportBuilder_Creates_Valid_Xlsx()
    {
        await using var db=CreateDb();db.Products.Add(new(){Id="p1",OrganizationId="org",Sku="SKU-1",Name="Product"});await db.SaveChangesAsync();var artifact=await new ExportBuilder(db).BuildAsync(Job("xlsx","Products"),CancellationToken.None);
        using var stream=new MemoryStream(artifact.Content);using var workbook=new XLWorkbook(stream);Assert.Equal("SKU",workbook.Worksheet(1).Cell("A1").GetString());Assert.Equal("SKU-1",workbook.Worksheet(1).Cell("A2").GetString());
    }

    [Fact]
    public async Task Telegram_Start_Code_Links_Only_Matching_Pending_Organization()
    {
        await using var db=CreateDb();const string code="link-code";db.TelegramConnections.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",LinkCodeHash=TokenTools.Hash(code),LinkCodeExpiresAt=DateTimeOffset.UtcNow.AddMinutes(5)});await db.SaveChangesAsync();var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"TELEGRAM_BOT_TOKEN","test"}}).Build();var client=new TelegramClient(new HttpClient(new OkHandler()),config);using var update=JsonDocument.Parse("""{"message":{"text":"/start link-code","chat":{"id":12345}}}""");
        await TelegramWebhook.ProcessAsync(update.RootElement,db,client,CancellationToken.None);
        var connection=await db.TelegramConnections.SingleAsync();Assert.Equal("Active",connection.Status);Assert.Equal(12345,connection.ChatId);
    }

    private static ExportJobEntity Job(string format,string report)=>new(){Id=Guid.NewGuid(),OrganizationId="org",CreatedByUserId="user",Format=format,ReportType=report,DownloadTokenHash="hash"};
    private static SellerFinanceDbContext CreateDb()=>new(new DbContextOptionsBuilder<SellerFinanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed class OkHandler:HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));}
}
