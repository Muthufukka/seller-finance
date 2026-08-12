using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SellerFinance.Api;
using SellerFinance.Domain;

namespace SellerFinance.Tests;

public sealed class ProductCostsTests
{
    [Fact]
    public async Task Analytics_Uses_Latest_Cost_Effective_On_Order_Date()
    {
        await using var db=CreateDb();
        db.Products.Add(new(){Id="p1",OrganizationId="org",Sku="SKU-1",Name="Product"});
        db.ProductCostHistory.AddRange(Cost(100,new(2026,1,1)),Cost(250,new(2026,3,1)));
        db.Orders.Add(new(){Id="o1",ExternalId="e1",OrganizationId="org",Status=OrderStatus.Completed,Date=new(2026,2,15),CompletionDate=new(2026,2,15),Lines=[new(){Id=Guid.NewGuid(),OrderId="o1",ProductId="p1",Revenue=500,Quantity=2}]});
        await db.SaveChangesAsync();

        var json=JsonSerializer.Serialize(await DbAnalytics.SummaryAsync(db,"org"));

        using var document=JsonDocument.Parse(json);
        Assert.Equal(200,document.RootElement.GetProperty("Cogs").GetDecimal());
    }

    [Fact]
    public async Task Missing_History_Never_Becomes_Zero_Cost()
    {
        await using var db=CreateDb();db.Products.Add(new(){Id="p1",OrganizationId="org",Sku="SKU-1",Name="Product"});db.Orders.Add(new(){Id="o1",ExternalId="e1",OrganizationId="org",Status=OrderStatus.Completed,Date=new(2026,2,15),CompletionDate=new(2026,2,15),Lines=[new(){Id=Guid.NewGuid(),OrderId="o1",ProductId="p1",Revenue=500,Quantity=2}]});await db.SaveChangesAsync();
        var result=JsonSerializer.SerializeToElement(await DbAnalytics.SummaryAsync(db,"org"));
        Assert.Equal(JsonValueKind.Null,result.GetProperty("Cogs").ValueKind);Assert.Equal(0,result.GetProperty("CoveragePct").GetDecimal());Assert.True(result.GetProperty("IsPreliminary").GetBoolean());
    }

    [Fact]
    public async Task Kaspi_Completion_Date_Selects_Historical_Cost_Instead_Of_Creation_Date()
    {
        await using var db=CreateDb();db.Products.Add(new(){Id="p1",OrganizationId="org",Sku="SKU-1",Name="Product"});db.ProductCostHistory.AddRange(Cost(100,new(2026,8,1)),Cost(250,new(2026,8,10)));await db.SaveChangesAsync();
        var source=new KaspiOrderDto("external","CODE",1000,"COMPLETED",new DateTimeOffset(2026,8,5,0,0,0,TimeSpan.Zero),[new("line","SKU-1","Product",null,1,1000,null)],new DateTimeOffset(2026,8,12,0,0,0,TimeSpan.Zero));await KaspiOrderImporter.UpsertAsync(db,"org",Guid.NewGuid(),[source]);await db.SaveChangesAsync();
        var result=JsonSerializer.SerializeToElement(await DbAnalytics.SummaryAsync(db,"org"));Assert.Equal(250,result.GetProperty("Cogs").GetDecimal());
    }

    [Fact]
    public async Task Complete_Cost_Filter_Excludes_Uncovered_Lines_Across_Analytics()
    {
        await using var db=CreateDb();db.Products.AddRange(new(){Id="covered",OrganizationId="org",Sku="C",Name="Covered"},new(){Id="missing",OrganizationId="org",Sku="M",Name="Missing"});db.ProductCostHistory.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",ProductId="covered",CostAmount=400,EffectiveFrom=new(2026,1,1),Source=CostSource.Manual,CreatedByUserId="user"});db.Orders.Add(new(){Id="mixed",ExternalId="mixed",OrganizationId="org",Status=OrderStatus.Completed,Date=new(2026,8,12),CompletionDate=new(2026,8,12),Lines=[new(){Id=Guid.NewGuid(),OrderId="mixed",ProductId="covered",Revenue=1000,Quantity=1},new(){Id=Guid.NewGuid(),OrderId="mixed",ProductId="missing",Revenue=2000,Quantity=1}]});await db.SaveChangesAsync();
        var unfiltered=JsonSerializer.SerializeToElement(await DbAnalytics.SummaryAsync(db,"org"));var filtered=JsonSerializer.SerializeToElement(await DbAnalytics.SummaryAsync(db,"org",completeCostsOnly:true));
        Assert.Equal(3000,unfiltered.GetProperty("Revenue").GetDecimal());Assert.True(unfiltered.GetProperty("IsPreliminary").GetBoolean());Assert.Equal(1000,filtered.GetProperty("Revenue").GetDecimal());Assert.Equal(400,filtered.GetProperty("Cogs").GetDecimal());Assert.Equal(100,filtered.GetProperty("CoveragePct").GetDecimal());Assert.False(filtered.GetProperty("IsPreliminary").GetBoolean());
        var series=JsonSerializer.SerializeToElement(await DbAnalytics.TimeSeriesAsync(db,"org",completeCostsOnly:true));Assert.Equal(1000,series[0].GetProperty("revenue").GetDecimal());var abc=JsonSerializer.SerializeToElement(await DbAnalytics.AbcAsync(db,"org",completeCostsOnly:true));Assert.Single(abc.EnumerateArray());Assert.Equal("covered",abc[0].GetProperty("productId").GetString());
    }

    [Fact]
    public async Task Product_TimeSeries_Is_Tenant_Scoped_And_Explains_Daily_Result()
    {
        await using var db=CreateDb();db.Organizations.AddRange(new(){Id="org",Name="Org",AllocateOrganizationExpenses=true},new(){Id="other",Name="Other"});db.Products.AddRange(new(){Id="p1",OrganizationId="org",Sku="SKU-1",Name="Product"},new(){Id="p2",OrganizationId="org",Sku="SKU-2",Name="Second"},new(){Id="foreign",OrganizationId="other",Sku="SKU-X",Name="Foreign"});db.ProductCostHistory.Add(Cost(100,new(2026,1,1)));
        db.Orders.Add(new(){Id="o1",ExternalId="e1",OrganizationId="org",Status=OrderStatus.Completed,Date=new(2026,8,12),CompletionDate=new(2026,8,12),Lines=[new(){Id=Guid.NewGuid(),OrderId="o1",ProductId="p1",Revenue=1000,Quantity=2,FeeRate=.1m,Delivery=100},new(){Id=Guid.NewGuid(),OrderId="o1",ProductId="p2",Revenue=1000,Quantity=1}]});
        db.Expenses.AddRange(new(){Id=Guid.NewGuid(),OrganizationId="org",ProductId="p1",Type=ExpenseType.Advertising,Amount=100,Date=new(2026,8,12),CreatedByUserId="u"},new(){Id=Guid.NewGuid(),OrganizationId="org",Type=ExpenseType.Other,Amount=200,Date=new(2026,8,12),CreatedByUserId="u"},new(){Id=Guid.NewGuid(),OrganizationId="other",ProductId="foreign",Type=ExpenseType.Other,Amount=9999,Date=new(2026,8,12),CreatedByUserId="u"});await db.SaveChangesAsync();

        var result=JsonSerializer.SerializeToElement(await DbAnalytics.ProductTimeSeriesAsync(db,"org","p1",new(2026,8,12),new(2026,8,12)));

        Assert.Single(result.EnumerateArray());var day=result[0];Assert.Equal(2,day.GetProperty("units").GetInt32());Assert.Equal(1000,day.GetProperty("Revenue").GetDecimal());Assert.Equal(200,day.GetProperty("Cogs").GetDecimal());Assert.Equal(100,day.GetProperty("MarketplaceFees").GetDecimal());Assert.Equal(100,day.GetProperty("Delivery").GetDecimal());Assert.Equal(200,day.GetProperty("expenses").GetDecimal());Assert.Equal(400,day.GetProperty("OperatingProfit").GetDecimal());Assert.Equal(100,day.GetProperty("CoveragePct").GetDecimal());
    }

    [Fact]
    public async Task Dashboard_Problems_Are_Dynamic_And_Tenant_Scoped()
    {
        await using var db=CreateDb();var connection=Guid.NewGuid();var foreignConnection=Guid.NewGuid();db.Products.AddRange(new(){Id="missing",OrganizationId="org",Sku="M",Name="Missing"},new(){Id="loss",OrganizationId="org",Sku="L",Name="Loss"},new(){Id="foreign",OrganizationId="other",Sku="X",Name="Foreign"});db.ProductCostHistory.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",ProductId="loss",CostAmount=2000,EffectiveFrom=new(2026,1,1),CreatedByUserId="u"});db.Orders.AddRange(new(){Id="o1",ExternalId="o1",OrganizationId="org",Status=OrderStatus.Completed,Date=new(2026,8,12),Lines=[new(){Id=Guid.NewGuid(),OrderId="o1",ProductId="missing",Revenue=1000,Quantity=1},new(){Id=Guid.NewGuid(),OrderId="o1",ProductId="loss",Revenue=1000,Quantity=1}]},new(){Id="o2",ExternalId="o2",OrganizationId="other",Status=OrderStatus.Completed,Date=new(2026,8,12),Lines=[new(){Id=Guid.NewGuid(),OrderId="o2",ProductId="foreign",Revenue=1000,Quantity=1}]});db.MarketplaceConnections.AddRange(new(){Id=connection,OrganizationId="org",DisplayName="Main"},new(){Id=foreignConnection,OrganizationId="other",DisplayName="Foreign"});db.SyncJobs.AddRange(new(){Id=Guid.NewGuid(),OrganizationId="org",MarketplaceConnectionId=connection,Status=SyncJobStatus.RequiresAttention,ErrorCode="KASPI_429"},new(){Id=Guid.NewGuid(),OrganizationId="other",MarketplaceConnectionId=foreignConnection,Status=SyncJobStatus.RequiresAttention,ErrorCode="SECRET"});await db.SaveChangesAsync();

        var result=JsonSerializer.SerializeToElement(await DbAnalytics.DashboardProblemsAsync(db,"org",new(2026,8,1),new(2026,8,31)));

        Assert.Single(result.GetProperty("missingCosts").EnumerateArray());Assert.Equal("missing",result.GetProperty("missingCosts")[0].GetProperty("id").GetString());Assert.Single(result.GetProperty("negativeMargins").EnumerateArray());Assert.Equal("loss",result.GetProperty("negativeMargins")[0].GetProperty("id").GetString());Assert.Single(result.GetProperty("syncIssues").EnumerateArray());Assert.Equal("KASPI_429",result.GetProperty("syncIssues")[0].GetProperty("errorCode").GetString());Assert.Equal(3,result.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Csv_Import_Previews_Then_Confirms_Only_Valid_Rows()
    {
        await using var db=CreateDb();db.Products.Add(new(){Id="p1",OrganizationId="org",Sku="SKU-1",Name="Product"});await db.SaveChangesAsync();
        var csv="sku;cost;effectiveFrom\nSKU-1;1250,50;2026-08-01\nUNKNOWN;900;2026-08-01\nSKU-1;oops;2026-09-01";
        await using var stream=new MemoryStream(Encoding.UTF8.GetBytes(csv));var file=new FormFile(stream,0,stream.Length,"file","costs.csv");var service=new CostImportService(db);

        var job=await service.PreviewAsync("org","user",file,CancellationToken.None);
        var applied=await service.ConfirmAsync(job.Id,"org","user",CancellationToken.None);

        Assert.Equal(3,job.TotalRows);Assert.Equal(1,job.MatchedRows);Assert.Equal(1,job.UnmatchedRows);Assert.Equal(1,job.ErrorRows);Assert.Equal(1,applied);
        var history=await db.ProductCostHistory.SingleAsync();Assert.Equal(1250.50m,history.CostAmount);Assert.Equal(CostSource.CsvImport,history.Source);
    }

    [Fact]
    public async Task Import_Detects_Duplicate_Sku_And_Date()
    {
        await using var db=CreateDb();db.Products.Add(new(){Id="p1",OrganizationId="org",Sku="SKU-1",Name="Product"});await db.SaveChangesAsync();
        var csv="sku,cost,effectiveFrom\nSKU-1,100,2026-08-01\nSKU-1,120,2026-08-01";await using var stream=new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var job=await new CostImportService(db).PreviewAsync("org","user",new FormFile(stream,0,stream.Length,"file","costs.csv"),CancellationToken.None);
        Assert.Equal(1,job.DuplicateRows);Assert.Equal(1,job.ExpectedChanges);
    }

    private static ProductCostHistoryEntity Cost(decimal amount,DateOnly date)=>new(){Id=Guid.NewGuid(),OrganizationId="org",ProductId="p1",CostAmount=amount,EffectiveFrom=date,Source=CostSource.Manual,CreatedByUserId="user"};
    private static SellerFinanceDbContext CreateDb()=>new(new DbContextOptionsBuilder<SellerFinanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
