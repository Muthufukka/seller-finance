namespace SellerFinance.Api;

public static class ExpenseRecognition
{
    public static decimal Amount(ExpenseEntity expense,DateOnly? from=null,DateOnly? to=null)
    {
        var start=expense.Date;var end=expense.PeriodEnd??start;if(end<start)return 0m;
        var overlapStart=from.HasValue&&from>start?from.Value:start;var overlapEnd=to.HasValue&&to<end?to.Value:end;if(overlapEnd<overlapStart)return 0m;
        var allocations=DailyAmounts(expense.Amount,start,end);var offset=overlapStart.DayNumber-start.DayNumber;var count=overlapEnd.DayNumber-overlapStart.DayNumber+1;return allocations.Skip(offset).Take(count).Sum();
    }

    public static IReadOnlyDictionary<DateOnly,decimal> ByDay(IEnumerable<ExpenseEntity> expenses,DateOnly? from=null,DateOnly? to=null)
    {
        var result=new Dictionary<DateOnly,decimal>();foreach(var expense in expenses){var start=expense.Date;var end=expense.PeriodEnd??start;if(end<start)continue;var values=DailyAmounts(expense.Amount,start,end);for(var index=0;index<values.Count;index++){var date=start.AddDays(index);if(from.HasValue&&date<from||to.HasValue&&date>to)continue;result[date]=result.GetValueOrDefault(date)+values[index];}}return result;
    }

    private static IReadOnlyList<decimal> DailyAmounts(decimal amount,DateOnly start,DateOnly end)
    {
        var days=end.DayNumber-start.DayNumber+1;return SellerFinance.Domain.FinanceCalculator.AllocateByRevenue(amount,Enumerable.Repeat(1m,days).ToArray());
    }
}
