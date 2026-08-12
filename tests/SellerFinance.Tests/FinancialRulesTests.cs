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

    private static OrderLineEntity SeedOrder(SellerFinanceDbContext db)
    {
        db.Products.Add(new(){Id="p1",OrganizationId="org",Sku="SKU",Name="Product"});db.ProductCostHistory.Add(new(){Id=Guid.NewGuid(),OrganizationId="org",ProductId="p1",CostAmount=0.01m,EffectiveFrom=new(2020,1,1),Source=CostSource.Manual,CreatedByUserId="user"});
        var line=new OrderLineEntity{Id=Guid.NewGuid(),OrderId="o1",ProductId="p1",Revenue=1000,Quantity=0};db.Orders.Add(new(){Id="o1",ExternalId="e1",OrganizationId="org",Status=OrderStatus.Completed,Date=new(2026,8,2),CompletionDate=new(2026,8,2),Lines=[line]});return line;
    }
    private static FeeRuleEntity Rule(FeeRuleScope scope,decimal value,string? product=null)=>new(){Id=Guid.NewGuid(),OrganizationId="org",Scope=scope,ProductId=product,ValueType=FeeValueType.Percentage,Value=value,EffectiveFrom=new(2020,1,1),CreatedByUserId="user"};
    private static async Task<decimal> SummaryValue(SellerFinanceDbContext db,string property,DateOnly? from=null,DateOnly? to=null){var json=JsonSerializer.Serialize(await DbAnalytics.SummaryAsync(db,"org",from,to));using var document=JsonDocument.Parse(json);return document.RootElement.GetProperty(property).GetDecimal();}
    private static SellerFinanceDbContext CreateDb()=>new(new DbContextOptionsBuilder<SellerFinanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
