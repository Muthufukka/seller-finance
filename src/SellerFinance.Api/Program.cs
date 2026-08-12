using SellerFinance.Domain;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://localhost:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()));
builder.Services.AddSingleton<DemoStore>();

var app = builder.Build();
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.Use(async (context, next) =>
{
    var tenantId = context.Request.Headers["X-Organization-Id"].ToString();
    if (context.Request.Path.StartsWithSegments("/api/v1") && String.IsNullOrWhiteSpace(tenantId))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { title = "Не выбрана организация", status = 400 });
        return;
    }
    context.Items["tenant"] = tenantId;
    await next();
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "SellerFinance.Api" }));

var api = app.MapGroup("/api/v1");
api.MapGet("/session", (HttpContext ctx, DemoStore store) =>
    store.HasTenant(ctx.Tenant()) ? Results.Ok(store.Session) : Results.NotFound());
api.MapGet("/analytics/summary", (HttpContext ctx, DemoStore store) =>
    store.HasTenant(ctx.Tenant()) ? Results.Ok(store.Summary()) : Results.NotFound());
api.MapGet("/analytics/timeseries", (HttpContext ctx, DemoStore store) =>
    store.HasTenant(ctx.Tenant()) ? Results.Ok(store.TimeSeries) : Results.NotFound());
api.MapGet("/analytics/products", (HttpContext ctx, DemoStore store) =>
    store.HasTenant(ctx.Tenant()) ? Results.Ok(store.Products) : Results.NotFound());
api.MapGet("/analytics/abc", (HttpContext ctx, DemoStore store) =>
    store.HasTenant(ctx.Tenant()) ? Results.Ok(store.Abc()) : Results.NotFound());
api.MapGet("/orders", (HttpContext ctx, DemoStore store) =>
    store.HasTenant(ctx.Tenant()) ? Results.Ok(store.Orders) : Results.NotFound());
api.MapPost("/integrations/kaspi/verify", (HttpContext ctx, KaspiTokenRequest request, DemoStore store) =>
{
    if (!store.HasTenant(ctx.Tenant())) return Results.NotFound();
    if (String.IsNullOrWhiteSpace(request.Token) || request.Token.Length < 16)
        return Results.BadRequest(new { title = "Токен имеет неверный формат" });
    return Results.Ok(new { status = "verified", maskedToken = $"••••{request.Token[^4..]}" });
});
api.MapPost("/integrations/kaspi/sync", (HttpContext ctx, DemoStore store) =>
    store.HasTenant(ctx.Tenant())
        ? Results.Accepted(value: new { jobId = Guid.NewGuid(), status = "queued", message = "Синхронизация поставлена в очередь" })
        : Results.NotFound());

app.MapFallbackToFile("index.html");

app.Run();

record KaspiTokenRequest(string Token);

static class TenantContext
{
    public static string Tenant(this HttpContext context) => context.Items["tenant"]?.ToString() ?? "";
}

sealed class DemoStore
{
    public const string TenantId = "demo-organization";
    public object Session => new { organizationId = TenantId, organizationName = "Aspan Market", userName = "Алия", role = "Owner", plan = "Pro" };

    public IReadOnlyList<OrderFact> Facts { get; } =
    [
        new("KSP-10482", TenantId, OrderStatus.Completed, new(2026, 8, 6), [new("p1", 24990m, 2, 7200m, null, .109m, 700m)]),
        new("KSP-10497", TenantId, OrderStatus.Completed, new(2026, 8, 7), [new("p2", 18490m, 1, 8900m, null, .109m, 450m)]),
        new("KSP-10511", TenantId, OrderStatus.Completed, new(2026, 8, 8), [new("p3", 42900m, 3, 9100m, 4200m, .109m, 900m)]),
        new("KSP-10529", TenantId, OrderStatus.Completed, new(2026, 8, 9), [new("p4", 12990m, 1, null, null, .12m, 350m)]),
        new("KSP-10543", TenantId, OrderStatus.Completed, new(2026, 8, 10), [new("p1", 37485m, 3, 7200m, null, .109m, 800m)]),
        new("KSP-10561", TenantId, OrderStatus.Completed, new(2026, 8, 11), [new("p2", 36980m, 2, 8900m, null, .109m, 650m)]),
        new("KSP-10566", TenantId, OrderStatus.Returned, new(2026, 8, 11), [new("p3", 14300m, 1, 9100m, null, .109m, 350m)])
    ];

    public object[] Products =>
    [
        new { id="p1", sku="HOME-101", name="Органайзер для кухни", units=5, revenue=62475m, cogs=36000m, profit=18865m, margin=30.2m, cost=7200m, status="profitable" },
        new { id="p2", sku="BEAUTY-220", name="Набор косметичек", units=3, revenue=55470m, cogs=26700m, profit=22173m, margin=40.0m, cost=8900m, status="profitable" },
        new { id="p3", sku="TECH-044", name="Настольная LED-лампа", units=3, revenue=42900m, cogs=27300m, profit=10400m, margin=24.2m, cost=9100m, status="profitable" },
        new { id="p4", sku="KIDS-018", name="Развивающий набор", units=1, revenue=12990m, cogs=(decimal?)null, profit=(decimal?)null, margin=(decimal?)null, cost=(decimal?)null, status="missing-cost" }
    ];

    public object[] Orders => Facts.Select(x => new
    {
        id=x.Id, date=x.Date, status=x.Status.ToString().ToUpperInvariant(),
        amount=x.Lines.Sum(y => y.Revenue), items=x.Lines.Sum(y => y.Quantity),
        complete=x.Lines.All(y => y.UnitCost.HasValue)
    }).Cast<object>().ToArray();

    public object[] TimeSeries => Facts.Where(x => x.Status == OrderStatus.Completed).GroupBy(x => x.Date)
        .Select(g => { var f=FinanceCalculator.Calculate(g); return new { date=g.Key, revenue=f.Revenue, profit=f.OperatingProfit }; })
        .Cast<object>().ToArray();

    public object Summary()
    {
        var result = FinanceCalculator.Calculate(Facts, 12500m);
        return new { result.Revenue, orders=Facts.Count(x => x.Status == OrderStatus.Completed), units=Facts.Where(x=>x.Status==OrderStatus.Completed).SelectMany(x=>x.Lines).Sum(x=>x.Quantity), result.Cogs, result.GrossProfit, result.MarketplaceFees, result.Delivery, result.OperatingProfit, result.OperatingMarginPct, result.CoveragePct, result.IsPreliminary };
    }

    public object[] Abc()
    {
        var rows = Products.OrderByDescending(x => (decimal?)x.GetType().GetProperty("profit")!.GetValue(x) ?? 0m).ToArray();
        var total = rows.Sum(x => (decimal?)x.GetType().GetProperty("profit")!.GetValue(x) ?? 0m);
        decimal running = 0;
        return rows.Select(x => { running += (decimal?)x.GetType().GetProperty("profit")!.GetValue(x) ?? 0m; var pct=total == 0 ? 0 : running/total*100; return new { product=x, group=pct <= 80 ? "A" : pct <= 95 ? "B" : "C", cumulative=Decimal.Round(pct,1) }; }).Cast<object>().ToArray();
    }

    public bool HasTenant(string tenantId) => tenantId == TenantId;
}

public partial class Program { }
