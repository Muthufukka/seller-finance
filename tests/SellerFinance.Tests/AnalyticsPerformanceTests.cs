using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SellerFinance.Api;
using SellerFinance.Domain;
using Xunit.Abstractions;

namespace SellerFinance.Tests;

public sealed class AnalyticsPerformanceTests
{
    private readonly ITestOutputHelper output;
    private const int OrderCount=10_000;
    private const int LinesPerOrder=10;

    public AnalyticsPerformanceTests(ITestOutputHelper output)=>this.output=output;

    [Fact]
    [Trait("Category","Performance")]
    public async Task Warm_Dashboard_For_One_Hundred_Thousand_Order_Items_Completes_Within_Two_Seconds()
    {
        await using var db=new SellerFinanceDbContext(new DbContextOptionsBuilder<SellerFinanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        Seed(db);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await DbAnalytics.SummaryAsync(db,"perf",new(2026,1,1),new(2026,12,31));
        var watch=Stopwatch.StartNew();
        var summary=await DbAnalytics.SummaryAsync(db,"perf",new(2026,1,1),new(2026,12,31));
        watch.Stop();

        Assert.NotNull(summary);
        output.WriteLine("Warm dashboard: {0:N0} ms for {1:N0} order items",watch.Elapsed.TotalMilliseconds,OrderCount*LinesPerOrder);
        Assert.True(watch.Elapsed<TimeSpan.FromSeconds(2),$"Warm dashboard took {watch.Elapsed.TotalMilliseconds:N0} ms for {OrderCount*LinesPerOrder:N0} order items");
    }

    [Fact]
    public async Task Saving_Tenant_Data_Invalidates_The_Warm_Analytics_Snapshot()
    {
        await using var db=new SellerFinanceDbContext(new DbContextOptionsBuilder<SellerFinanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);db.Products.Add(new(){Id="p",OrganizationId="cache",Sku="P",Name="P"});db.ProductCostHistory.Add(new(){Id=Guid.NewGuid(),OrganizationId="cache",ProductId="p",CostAmount=1,EffectiveFrom=new(2025,1,1),CreatedByUserId="test"});db.Orders.Add(Order("first",100));await db.SaveChangesAsync();
        Assert.Equal(100,Revenue(await DbAnalytics.SummaryAsync(db,"cache")));Assert.Equal(100,Revenue(await DbAnalytics.SummaryAsync(db,"cache")));
        db.Orders.Add(Order("second",250));await db.SaveChangesAsync();
        Assert.Equal(350,Revenue(await DbAnalytics.SummaryAsync(db,"cache")));
    }

    private static void Seed(SellerFinanceDbContext db)
    {
        const int productCount=100;
        for(var productIndex=0;productIndex<productCount;productIndex++)
        {
            var productId=$"p-{productIndex}";
            db.Products.Add(new(){Id=productId,OrganizationId="perf",Sku=$"SKU-{productIndex}",Name=$"Product {productIndex}",Category=$"Category {productIndex%10}"});
            db.ProductCostHistory.Add(new(){Id=Guid.NewGuid(),OrganizationId="perf",ProductId=productId,CostAmount=400,EffectiveFrom=new(2025,1,1),Source=CostSource.Manual,CreatedByUserId="perf"});
        }
        db.FeeRules.Add(new(){Id=Guid.NewGuid(),OrganizationId="perf",Scope=FeeRuleScope.Default,ValueType=FeeValueType.Percentage,Value=10,EffectiveFrom=new(2025,1,1),CreatedByUserId="perf"});
        for(var orderIndex=0;orderIndex<OrderCount;orderIndex++)
        {
            var orderId=$"o-{orderIndex}";var date=new DateOnly(2026,1,1).AddDays(orderIndex%365);var order=new OrderEntity{Id=orderId,ExternalId=orderId,OrganizationId="perf",Status=OrderStatus.Completed,Date=date,CompletionDate=date};
            for(var lineIndex=0;lineIndex<LinesPerOrder;lineIndex++)order.Lines.Add(new(){Id=Guid.NewGuid(),OrderId=orderId,ProductId=$"p-{(orderIndex*LinesPerOrder+lineIndex)%productCount}",Revenue=1000,Quantity=1,Delivery=50});
            db.Orders.Add(order);
        }
    }

    private static OrderEntity Order(string id,decimal revenue)=>new(){Id=id,ExternalId=id,OrganizationId="cache",Status=OrderStatus.Completed,Date=new(2026,1,1),CompletionDate=new(2026,1,1),Lines=[new(){Id=Guid.NewGuid(),OrderId=id,ProductId="p",Revenue=revenue,Quantity=1}]};
    private static decimal Revenue(object value)=>System.Text.Json.JsonSerializer.SerializeToElement(value).GetProperty("Revenue").GetDecimal();
}
