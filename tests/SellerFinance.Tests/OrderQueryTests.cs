using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SellerFinance.Api;
using SellerFinance.Domain;

namespace SellerFinance.Tests;

public sealed class OrderQueryTests
{
    [Fact]
    public async Task OrdersAsync_Filters_And_Paginates_Inside_Tenant()
    {
        await using var db=CreateDb();
        db.Products.AddRange(Product("p1","org-a","A"),Product("p2","org-a","B"),Product("foreign","org-b","X"));
        db.Orders.AddRange(Order("a-1","org-a","p1",new(2026,8,1),OrderStatus.Completed,1000),Order("a-2","org-a","p2",new(2026,8,2),OrderStatus.Cancelled,2000),Order("b-1","org-b","foreign",new(2026,8,3),OrderStatus.Completed,9999));
        await db.SaveChangesAsync();

        var result=JsonSerializer.SerializeToElement(await DbAnalytics.OrdersAsync(db,"org-a",status:"COMPLETED",productId:"p1",page:1,pageSize:1));

        Assert.Equal(1,result.GetProperty("totalCount").GetInt32());
        var item=result.GetProperty("items").EnumerateArray().Single();
        Assert.Equal("a-1",item.GetProperty("externalId").GetString());
        Assert.DoesNotContain("b-1",result.ToString());
    }

    [Fact]
    public async Task OrdersAsync_Applies_Date_And_Profit_Range()
    {
        await using var db=CreateDb();db.Products.Add(Product("p1","org","A"));
        db.Orders.AddRange(Order("low","org","p1",new(2026,8,1),OrderStatus.Completed,1000),Order("high","org","p1",new(2026,8,10),OrderStatus.Completed,5000));await db.SaveChangesAsync();

        var result=JsonSerializer.SerializeToElement(await DbAnalytics.OrdersAsync(db,"org",from:new(2026,8,5),profitFrom:4000,pageSize:50));

        Assert.Equal(1,result.GetProperty("totalCount").GetInt32());
        Assert.Equal("high",result.GetProperty("items")[0].GetProperty("externalId").GetString());
    }

    private static ProductEntity Product(string id,string org,string sku)=>new(){Id=id,OrganizationId=org,Sku=sku,Name=sku};
    private static OrderEntity Order(string id,string org,string product,DateOnly date,OrderStatus status,decimal revenue)=>new(){Id=id,ExternalId=id,OrganizationId=org,Date=date,CompletionDate=date,Status=status,Lines=[new(){Id=Guid.NewGuid(),OrderId=id,ProductId=product,Revenue=revenue,Quantity=1,UnitCost=100m}]};
    private static SellerFinanceDbContext CreateDb()=>new(new DbContextOptionsBuilder<SellerFinanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
