using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SellerFinance.Api;
using SellerFinance.Domain;

namespace SellerFinance.Tests;

public sealed class FinancialRulesTests
{
    [Fact]
    public async Task Product_Fee_Rule_Overrides_Default_Rule()
    {
        await using var db=CreateDb();SeedOrder(db);
        db.FeeRules.AddRange(Rule(FeeRuleScope.Default,10),Rule(FeeRuleScope.Product,15,"p1"));await db.SaveChangesAsync();
        Assert.Equal(150m,await SummaryValue(db,"MarketplaceFees"));
    }

    [Fact]
    public async Task Actual_Fee_Overrides_Calculated_Rule()
    {
        await using var db=CreateDb();var line=SeedOrder(db);db.FeeRules.Add(Rule(FeeRuleScope.Default,10));db.ActualFees.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",OrderLineId=line.Id,Amount=77,CreatedByUserId="user"});await db.SaveChangesAsync();
        Assert.Equal(77m,await SummaryValue(db,"MarketplaceFees"));
    }

    [Fact]
    public async Task Latest_Effective_Rule_Is_Used_For_Historical_Date()
    {
        await using var db=CreateDb();SeedOrder(db);db.FeeRules.AddRange(Rule(FeeRuleScope.Default,5),new FeeRuleEntity{Id=Guid.NewGuid(),OrganizationId="org",Scope=FeeRuleScope.Default,ValueType=FeeValueType.Percentage,Value=20,EffectiveFrom=new(2026,8,1),CreatedByUserId="user"});await db.SaveChangesAsync();
        Assert.Equal(200m,await SummaryValue(db,"MarketplaceFees"));
    }

    [Fact]
    public async Task Period_Expense_Reduces_Operating_Profit()
    {
        await using var db=CreateDb();SeedOrder(db);db.Expenses.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",Type=ExpenseType.Advertising,Amount=100,Date=new(2026,8,1),CreatedByUserId="user"});await db.SaveChangesAsync();
        Assert.Equal(900m,await SummaryValue(db,"OperatingProfit"));
        Assert.Equal(1000m,await SummaryValue(db,"OperatingProfit",new(2026,8,2),new(2026,8,3)));
    }

    [Fact]
    public async Task Organization_Expenses_Are_Allocated_By_Revenue_Only_When_Enabled()
    {
        await using var db=CreateDb();db.Organizations.Add(new(){Id="org",Name="Org",AllocateOrganizationExpenses=true});db.Products.AddRange(new(){Id="p1",OrganizationId="org",Sku="A",Name="A"},new(){Id="p2",OrganizationId="org",Sku="B",Name="B"});db.ProductCostHistory.AddRange(new(){Id=Guid.NewGuid(),OrganizationId="org",ProductId="p1",CostAmount=100,EffectiveFrom=new(2020,1,1),CreatedByUserId="user"},new(){Id=Guid.NewGuid(),OrganizationId="org",ProductId="p2",CostAmount=100,EffectiveFrom=new(2020,1,1),CreatedByUserId="user"});db.Orders.Add(new(){Id="o",ExternalId="o",OrganizationId="org",Status=OrderStatus.Completed,Date=new(2026,8,2),CompletionDate=new(2026,8,2),Lines=[new(){Id=Guid.NewGuid(),OrderId="o",ProductId="p1",Revenue=1000,Quantity=1},new(){Id=Guid.NewGuid(),OrderId="o",ProductId="p2",Revenue=2000,Quantity=1}]});db.Expenses.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",Type=ExpenseType.Services,Amount=999,Date=new(2026,8,2),CreatedByUserId="user"});await db.SaveChangesAsync();
        var products=JsonSerializer.SerializeToElement(await DbAnalytics.ProductsAsync(db,"org"));var allocations=products.EnumerateArray().ToDictionary(x=>x.GetProperty("id").GetString()!,x=>x.GetProperty("allocatedOrganizationExpenses").GetDecimal());Assert.Equal(999,allocations.Values.Sum());Assert.Equal(333,allocations["p1"]);Assert.Equal(666,allocations["p2"]);
        var productProfit=products.EnumerateArray().Sum(x=>x.GetProperty("profit").GetDecimal());Assert.Equal(1801,productProfit);Assert.Equal(1801,await SummaryValue(db,"OperatingProfit"));
        (await db.Organizations.SingleAsync()).AllocateOrganizationExpenses=false;await db.SaveChangesAsync();products=JsonSerializer.SerializeToElement(await DbAnalytics.ProductsAsync(db,"org"));Assert.All(products.EnumerateArray(),x=>Assert.Equal(0,x.GetProperty("allocatedOrganizationExpenses").GetDecimal()));
    }

    [Fact]
    public async Task Period_Expense_Is_Recognized_Only_For_Overlapping_Days()
    {
        await using var db=CreateDb();SeedOrder(db);db.Expenses.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",Type=ExpenseType.Advertising,Amount=1000,Date=new(2026,8,1),PeriodEnd=new(2026,8,10),CreatedByUserId="user"});await db.SaveChangesAsync();
        Assert.Equal(500,await SummaryValue(db,"OperatingExpenses",new(2026,8,1),new(2026,8,5)));Assert.Equal(500,await SummaryValue(db,"OperatingExpenses",new(2026,8,6),new(2026,8,10)));Assert.Equal(0,await SummaryValue(db,"OperatingExpenses",new(2026,8,11),new(2026,8,12)));
        var daily=JsonSerializer.SerializeToElement(await DbAnalytics.TimeSeriesAsync(db,"org",new(2026,8,1),new(2026,8,10)));Assert.Equal(10,daily.GetArrayLength());Assert.Equal(0,daily.EnumerateArray().Sum(x=>x.GetProperty("profit").GetDecimal()));Assert.Equal(-100,daily[0].GetProperty("profit").GetDecimal());Assert.Equal(900,daily[1].GetProperty("profit").GetDecimal());
    }

    [Fact]
    public void Period_Expense_Daily_Rounding_Preserves_Exact_Total()
    {
        var expense=new ExpenseEntity{Amount=100,Date=new(2026,8,1),PeriodEnd=new(2026,8,3)};var days=ExpenseRecognition.ByDay([expense]);Assert.Equal(100,days.Values.Sum());Assert.Equal(3,days.Count);Assert.Equal(ExpenseRecognition.Amount(expense,new(2026,8,2),new(2026,8,3)),days[new(2026,8,2)]+days[new(2026,8,3)]);
    }

    [Fact]
    public async Task Order_Linked_Expense_Is_A_Variable_Cost_And_Is_Not_Deducted_Twice()
    {
        await using var db=CreateDb();db.Products.AddRange(new(){Id="p1",OrganizationId="org",Sku="A",Name="A"},new(){Id="p2",OrganizationId="org",Sku="B",Name="B"});db.ProductCostHistory.AddRange(new(){Id=Guid.NewGuid(),OrganizationId="org",ProductId="p1",CostAmount=100,EffectiveFrom=new(2020,1,1),CreatedByUserId="user"},new(){Id=Guid.NewGuid(),OrganizationId="org",ProductId="p2",CostAmount=100,EffectiveFrom=new(2020,1,1),CreatedByUserId="user"});db.Orders.Add(new(){Id="o",ExternalId="o",OrganizationId="org",Status=OrderStatus.Completed,Date=new(2026,8,2),CompletionDate=new(2026,8,2),Lines=[new(){Id=Guid.NewGuid(),OrderId="o",ProductId="p1",Revenue=1000,Quantity=1},new(){Id=Guid.NewGuid(),OrderId="o",ProductId="p2",Revenue=2000,Quantity=1}]});db.Expenses.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",OrderId="o",Type=ExpenseType.Fulfillment,Amount=999,Date=new(2026,8,2),CreatedByUserId="user"});await db.SaveChangesAsync();
        var summary=JsonSerializer.SerializeToElement(await DbAnalytics.SummaryAsync(db,"org"));Assert.Equal(0,summary.GetProperty("OperatingExpenses").GetDecimal());Assert.Equal(1801,summary.GetProperty("OperatingProfit").GetDecimal());var detail=JsonSerializer.SerializeToElement(await DbAnalytics.OrderDetailAsync(db,"org","o"));Assert.Equal(999,detail.GetProperty("VariableCosts").GetDecimal());Assert.Equal(999,detail.GetProperty("lines").EnumerateArray().Sum(x=>x.GetProperty("OtherVariableCosts").GetDecimal()));Assert.Equal(333,detail.GetProperty("lines")[0].GetProperty("OtherVariableCosts").GetDecimal());Assert.Equal(666,detail.GetProperty("lines")[1].GetProperty("OtherVariableCosts").GetDecimal());
    }

    [Fact]
    public async Task Expense_Linked_To_Noncompleted_Order_Remains_An_Operating_Expense()
    {
        await using var db=CreateDb();db.Orders.Add(new(){Id="cancelled",ExternalId="cancelled",OrganizationId="org",Status=OrderStatus.Cancelled,Date=new(2026,8,2)});db.Expenses.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",OrderId="cancelled",Type=ExpenseType.Fulfillment,Amount=500,Date=new(2026,8,2),CreatedByUserId="user"});await db.SaveChangesAsync();var summary=JsonSerializer.SerializeToElement(await DbAnalytics.SummaryAsync(db,"org"));Assert.Equal(500,summary.GetProperty("OperatingExpenses").GetDecimal());Assert.Equal(-500,summary.GetProperty("OperatingProfit").GetDecimal());
    }

    private static OrderLineEntity SeedOrder(SellerFinanceDbContext db)
    {
        db.Products.Add(new(){Id="p1",OrganizationId="org",Sku="SKU",Name="Product"});db.ProductCostHistory.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",ProductId="p1",CostAmount=0.01m,EffectiveFrom=new(2020,1,1),Source=CostSource.Manual,CreatedByUserId="user"});
        var line=new OrderLineEntity{Id=Guid.NewGuid(),OrderId="o1",ProductId="p1",Revenue=1000,Quantity=0};db.Orders.Add(new(){Id="o1",ExternalId="e1",OrganizationId="org",Status=OrderStatus.Completed,Date=new(2026,8,2),CompletionDate=new(2026,8,2),Lines=[line]});return line;
    }
    private static FeeRuleEntity Rule(FeeRuleScope scope,decimal value,string? product=null)=>new(){Id=Guid.NewGuid(),OrganizationId="org",Scope=scope,ProductId=product,ValueType=FeeValueType.Percentage,Value=value,EffectiveFrom=new(2020,1,1),CreatedByUserId="user"};
    private static async Task<decimal> SummaryValue(SellerFinanceDbContext db,string property,DateOnly? from=null,DateOnly? to=null){var json=JsonSerializer.Serialize(await DbAnalytics.SummaryAsync(db,"org",from,to));using var document=JsonDocument.Parse(json);return document.RootElement.GetProperty(property).GetDecimal();}
    private static SellerFinanceDbContext CreateDb()=>new(new DbContextOptionsBuilder<SellerFinanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
