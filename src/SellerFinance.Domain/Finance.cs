namespace SellerFinance.Domain;

public enum OrderStatus { Completed, Returned, Cancelled, Pending }

public sealed record OrderLine(
    string ProductId,
    decimal Revenue,
    int Quantity,
    decimal? UnitCost,
    decimal? ActualFee,
    decimal FeeRate,
    decimal Delivery,
    decimal OtherVariableCosts = 0m);

public sealed record OrderFact(string Id, string OrganizationId, OrderStatus Status, DateOnly Date, IReadOnlyList<OrderLine> Lines);

public sealed record FinanceResult(
    decimal Revenue,
    decimal? Cogs,
    decimal GrossProfit,
    decimal MarketplaceFees,
    decimal Delivery,
    decimal VariableCosts,
    decimal ContributionProfit,
    decimal OperatingExpenses,
    decimal OperatingProfit,
    decimal? GrossMarginPct,
    decimal? OperatingMarginPct,
    decimal CoveragePct,
    bool IsPreliminary);

public static class FinanceCalculator
{
    public static FinanceResult Calculate(IEnumerable<OrderFact> orders, decimal operatingExpenses = 0m)
    {
        var lines = orders.Where(x => x.Status == OrderStatus.Completed).SelectMany(x => x.Lines).ToArray();
        var revenue = lines.Sum(x => x.Revenue);
        var coveredRevenue = lines.Where(x => x.UnitCost.HasValue).Sum(x => x.Revenue);
        var cogs = lines.Where(x => x.UnitCost.HasValue).Sum(x => x.UnitCost!.Value * x.Quantity);
        var fees = lines.Sum(x => x.ActualFee ?? Decimal.Round(x.Revenue * x.FeeRate, 4, MidpointRounding.AwayFromZero));
        var delivery = lines.Sum(x => x.Delivery);
        var variableCosts = fees + delivery + lines.Sum(x => x.OtherVariableCosts);
        var grossProfit = revenue - cogs;
        var contribution = revenue - cogs - variableCosts;
        var operatingProfit = contribution - operatingExpenses;
        var coverage = revenue == 0 ? 100m : Decimal.Round(coveredRevenue / revenue * 100m, 2);

        return new(
            revenue,
            coverage == 100m ? cogs : null,
            grossProfit,
            fees,
            delivery,
            variableCosts,
            contribution,
            operatingExpenses,
            operatingProfit,
            Percent(grossProfit, revenue),
            Percent(operatingProfit, revenue),
            coverage,
            coverage < 100m);
    }

    public static IReadOnlyList<decimal> AllocateByRevenue(decimal amount, IReadOnlyList<decimal> revenues)
    {
        if (revenues.Count == 0) return [];
        var total = revenues.Sum();
        if (total <= 0) return Enumerable.Repeat(0m, revenues.Count).ToArray();
        var result = new decimal[revenues.Count];
        decimal allocated = 0;
        for (var i = 0; i < revenues.Count - 1; i++)
        {
            result[i] = Decimal.Round(amount * revenues[i] / total, 4, MidpointRounding.AwayFromZero);
            allocated += result[i];
        }
        result[^1] = amount - allocated;
        return result;
    }

    private static decimal? Percent(decimal value, decimal revenue) =>
        revenue == 0 ? null : Decimal.Round(value / revenue * 100m, 2);
}
