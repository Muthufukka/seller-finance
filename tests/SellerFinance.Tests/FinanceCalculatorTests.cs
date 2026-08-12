using SellerFinance.Domain;

namespace SellerFinance.Tests;

public class FinanceCalculatorTests
{
    [Fact]
    public void Fin01_CalculatesExpectedContribution()
    {
        var order = new OrderFact("1", "tenant-a", OrderStatus.Completed, new(2026, 8, 1),
            [new("sku", 20_000m, 2, 6_000m, null, .10m, 500m)]);
        var result = FinanceCalculator.Calculate([order]);
        Assert.Equal(20_000m, result.Revenue);
        Assert.Equal(12_000m, result.Cogs);
        Assert.Equal(2_000m, result.MarketplaceFees);
        Assert.Equal(5_500m, result.ContributionProfit);
    }

    [Fact]
    public void MissingCost_IsNotSubstitutedWithZero()
    {
        var order = new OrderFact("1", "tenant-a", OrderStatus.Completed, new(2026, 8, 1),
            [new("sku", 10_000m, 1, null, null, .10m, 0m)]);
        var result = FinanceCalculator.Calculate([order]);
        Assert.Null(result.Cogs);
        Assert.True(result.IsPreliminary);
        Assert.Equal(0m, result.CoveragePct);
    }

    [Fact]
    public void ReturnedOrder_IsExcludedFromFact()
    {
        var returned = new OrderFact("1", "tenant-a", OrderStatus.Returned, new(2026, 8, 1),
            [new("sku", 10_000m, 1, 5_000m, null, .10m, 0m)]);
        Assert.Equal(0m, FinanceCalculator.Calculate([returned]).Revenue);
    }

    [Fact]
    public void DeliveryAllocation_PreservesExactTotal()
    {
        var allocated = FinanceCalculator.AllocateByRevenue(1_000m, [10m, 20m, 30m]);
        Assert.Equal(1_000m, allocated.Sum());
        Assert.Equal(3, allocated.Count);
    }
}
