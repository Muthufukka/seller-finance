using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Npgsql;
using SellerFinance.Domain;

namespace SellerFinance.Api;

public sealed class SellerFinanceDbContext(DbContextOptions<SellerFinanceDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<OrganizationEntity> Organizations => Set<OrganizationEntity>();
    public DbSet<ProductEntity> Products => Set<ProductEntity>();
    public DbSet<OrderEntity> Orders => Set<OrderEntity>();
    public DbSet<OrderLineEntity> OrderLines => Set<OrderLineEntity>();
    public DbSet<OrganizationUserEntity> OrganizationUsers => Set<OrganizationUserEntity>();
    public DbSet<OrganizationInvitationEntity> OrganizationInvitations => Set<OrganizationInvitationEntity>();
    public DbSet<AuditLogEntity> AuditLogs => Set<AuditLogEntity>();
    public DbSet<MarketplaceConnectionEntity> MarketplaceConnections => Set<MarketplaceConnectionEntity>();
    public DbSet<SyncJobEntity> SyncJobs => Set<SyncJobEntity>();
    public DbSet<ProductCostHistoryEntity> ProductCostHistory => Set<ProductCostHistoryEntity>();
    public DbSet<CostImportJobEntity> CostImportJobs => Set<CostImportJobEntity>();
    public DbSet<CostImportRowEntity> CostImportRows => Set<CostImportRowEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<OrganizationEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<ProductEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<ProductEntity>().HasIndex(x => new { x.OrganizationId, x.Sku }).IsUnique();
        modelBuilder.Entity<OrderEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<OrderEntity>().HasIndex(x => new { x.OrganizationId, x.ExternalId }).IsUnique();
        modelBuilder.Entity<OrderLineEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<OrderLineEntity>().Property(x => x.Revenue).HasPrecision(19, 4);
        modelBuilder.Entity<OrderLineEntity>().Property(x => x.UnitCost).HasPrecision(19, 4);
        modelBuilder.Entity<OrderLineEntity>().Property(x => x.ActualFee).HasPrecision(19, 4);
        modelBuilder.Entity<OrderLineEntity>().Property(x => x.FeeRate).HasPrecision(9, 6);
        modelBuilder.Entity<OrderLineEntity>().Property(x => x.Delivery).HasPrecision(19, 4);
        modelBuilder.Entity<OrderLineEntity>().Property(x => x.OtherVariableCosts).HasPrecision(19, 4);
        modelBuilder.Entity<OrderEntity>().HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<OrganizationUserEntity>().HasKey(x => new { x.OrganizationId, x.UserId });
        modelBuilder.Entity<OrganizationUserEntity>().HasIndex(x => x.UserId);
        modelBuilder.Entity<OrganizationInvitationEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<OrganizationInvitationEntity>().HasIndex(x => x.TokenHash).IsUnique();
        modelBuilder.Entity<AuditLogEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<AuditLogEntity>().HasIndex(x => new { x.OrganizationId, x.CreatedAt });
        modelBuilder.Entity<MarketplaceConnectionEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<MarketplaceConnectionEntity>().HasIndex(x => new { x.OrganizationId, x.Provider }).IsUnique();
        modelBuilder.Entity<SyncJobEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<SyncJobEntity>().HasIndex(x => new { x.Status, x.NextAttemptAt });
        modelBuilder.Entity<ProductCostHistoryEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<ProductCostHistoryEntity>().Property(x => x.CostAmount).HasPrecision(19,4);
        modelBuilder.Entity<ProductCostHistoryEntity>().HasIndex(x => new { x.OrganizationId, x.ProductId, x.EffectiveFrom }).IsUnique();
        modelBuilder.Entity<CostImportJobEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<CostImportRowEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<CostImportRowEntity>().Property(x => x.CostAmount).HasPrecision(19,4);
        modelBuilder.Entity<CostImportRowEntity>().HasIndex(x => new { x.ImportJobId, x.RowNumber });
    }
}

public enum CostSource { Manual, CsvImport, XlsxImport, Legacy }
public enum CostImportStatus { Preview, Applied, Rejected, Expired }

public sealed class ProductCostHistoryEntity
{
    public Guid Id { get; set; }
    public string OrganizationId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public decimal CostAmount { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public CostSource Source { get; set; }
    public Guid? ImportJobId { get; set; }
    public string CreatedByUserId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CostImportJobEntity
{
    public Guid Id { get; set; }
    public string OrganizationId { get; set; } = "";
    public string CreatedByUserId { get; set; } = "";
    public string FileNameSafe { get; set; } = "";
    public CostSource Source { get; set; }
    public CostImportStatus Status { get; set; } = CostImportStatus.Preview;
    public int TotalRows { get; set; }
    public int MatchedRows { get; set; }
    public int UnmatchedRows { get; set; }
    public int ErrorRows { get; set; }
    public int DuplicateRows { get; set; }
    public int ExpectedChanges { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddHours(24);
    public DateTimeOffset? AppliedAt { get; set; }
}

public sealed class CostImportRowEntity
{
    public Guid Id { get; set; }
    public Guid ImportJobId { get; set; }
    public int RowNumber { get; set; }
    public string Sku { get; set; } = "";
    public string? ProductId { get; set; }
    public decimal? CostAmount { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public string Status { get; set; } = "Valid";
    public string? Error { get; set; }
}

public enum MarketplaceConnectionStatus { PendingVerification, Active, RequiresAttention, Disabled }
public enum SyncJobStatus { Queued, Running, Succeeded, RetryScheduled, RequiresAttention }

public sealed class MarketplaceConnectionEntity
{
    public Guid Id { get; set; }
    public string OrganizationId { get; set; } = "";
    public string Provider { get; set; } = "Kaspi";
    public byte[] TokenCiphertext { get; set; } = [];
    public byte[] TokenNonce { get; set; } = [];
    public byte[] TokenTag { get; set; } = [];
    public MarketplaceConnectionStatus Status { get; set; } = MarketplaceConnectionStatus.PendingVerification;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastVerifiedAt { get; set; }
    public DateTimeOffset? LastSuccessfulSyncAt { get; set; }
    public string? LastErrorCode { get; set; }
}

public sealed class SyncJobEntity
{
    public Guid Id { get; set; }
    public string OrganizationId { get; set; } = "";
    public Guid MarketplaceConnectionId { get; set; }
    public SyncJobStatus Status { get; set; } = SyncJobStatus.Queued;
    public DateTimeOffset WindowFrom { get; set; }
    public DateTimeOffset WindowTo { get; set; }
    public int Attempt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int ImportedOrders { get; set; }
    public string? ErrorCode { get; set; }
}

public sealed class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
    public string Status { get; set; } = "Active";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum OrganizationRole { Owner, Admin, Analyst, Viewer }

public sealed class OrganizationUserEntity
{
    public string OrganizationId { get; set; } = "";
    public string UserId { get; set; } = "";
    public OrganizationRole Role { get; set; }
    public DateTimeOffset InvitedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? JoinedAt { get; set; }
}

public sealed class OrganizationInvitationEntity
{
    public Guid Id { get; set; }
    public string OrganizationId { get; set; } = "";
    public string Email { get; set; } = "";
    public OrganizationRole Role { get; set; }
    public string TokenHash { get; set; } = "";
    public string InvitedByUserId { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
}

public sealed class AuditLogEntity
{
    public Guid Id { get; set; }
    public string? OrganizationId { get; set; }
    public string? UserId { get; set; }
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string? EntityId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string MetadataSafe { get; set; } = "{}";
}

public sealed class OrganizationEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string TimeZone { get; set; } = "Asia/Almaty";
    public string Currency { get; set; } = "KZT";
}

public sealed class ProductEntity
{
    public string Id { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal? CurrentCost { get; set; }
}

public sealed class OrderEntity
{
    public string Id { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public OrderStatus Status { get; set; }
    public DateOnly Date { get; set; }
    public DateOnly? CompletionDate { get; set; }
    public bool CalculationDateFallback { get; set; }
    public List<OrderLineEntity> Lines { get; set; } = [];
}

public sealed class OrderLineEntity
{
    public Guid Id { get; set; }
    public string OrderId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public decimal Revenue { get; set; }
    public int Quantity { get; set; }
    public decimal? UnitCost { get; set; }
    public decimal? ActualFee { get; set; }
    public decimal FeeRate { get; set; }
    public decimal Delivery { get; set; }
    public decimal OtherVariableCosts { get; set; }
}

public static class DatabaseConfiguration
{
    public static string? GetConnectionString(IConfiguration configuration)
    {
        var value = configuration["DATABASE_URL"] ?? configuration.GetConnectionString("Postgres");
        if (String.IsNullOrWhiteSpace(value)) return null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "postgres" && uri.Scheme != "postgresql")) return value;

        var credentials = uri.UserInfo.Split(':', 2);
        return new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : "",
            SslMode = uri.Host.Contains("render.com", StringComparison.OrdinalIgnoreCase) ? SslMode.Require : SslMode.Prefer
        }.ConnectionString;
    }
}

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SellerFinanceDbContext>
{
    public SellerFinanceDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SellerFinanceDbContext>()
            .UseNpgsql("Host=localhost;Database=seller_finance;Username=postgres;Password=postgres")
            .Options;
        return new(options);
    }
}

public static class DatabaseSeed
{
    private const string DemoTenantId = "demo-organization";
    public static async Task InitializeAsync(SellerFinanceDbContext db)
    {
        await db.Database.MigrateAsync();
        if (await db.Organizations.AnyAsync()) return;

        db.Organizations.Add(new() { Id = DemoTenantId, Name = "Aspan Market" });
        db.Products.AddRange(
            new ProductEntity { Id="p1", OrganizationId=DemoTenantId, Sku="HOME-101", Name="Органайзер для кухни", CurrentCost=7200m },
            new ProductEntity { Id="p2", OrganizationId=DemoTenantId, Sku="BEAUTY-220", Name="Набор косметичек", CurrentCost=8900m },
            new ProductEntity { Id="p3", OrganizationId=DemoTenantId, Sku="TECH-044", Name="Настольная LED-лампа", CurrentCost=9100m },
            new ProductEntity { Id="p4", OrganizationId=DemoTenantId, Sku="KIDS-018", Name="Развивающий набор" });

        db.Orders.AddRange(ToEntity("KSP-10482", new(2026,8,6), "p1", 24990m,2,7200m,null,.109m,700m),
            ToEntity("KSP-10497", new(2026,8,7), "p2",18490m,1,8900m,null,.109m,450m),
            ToEntity("KSP-10511", new(2026,8,8), "p3",42900m,3,9100m,4200m,.109m,900m),
            ToEntity("KSP-10529", new(2026,8,9), "p4",12990m,1,null,null,.12m,350m),
            ToEntity("KSP-10543", new(2026,8,10), "p1",37485m,3,7200m,null,.109m,800m),
            ToEntity("KSP-10561", new(2026,8,11), "p2",36980m,2,8900m,null,.109m,650m));
        await db.SaveChangesAsync();
    }

    private static OrderEntity ToEntity(string id, DateOnly date, string productId, decimal revenue, int quantity,
        decimal? cost, decimal? actualFee, decimal feeRate, decimal delivery) => new()
        {
            Id=id, ExternalId=id, OrganizationId=DemoTenantId, Status=OrderStatus.Completed, Date=date, CompletionDate=date,
            Lines=[new() { Id=Guid.NewGuid(), OrderId=id, ProductId=productId, Revenue=revenue, Quantity=quantity, UnitCost=cost, ActualFee=actualFee, FeeRate=feeRate, Delivery=delivery }]
        };
}

public static class DbAnalytics
{
    public static async Task<object> SummaryAsync(SellerFinanceDbContext db, string tenant)
    {
        var facts = await FactsAsync(db, tenant);
        var result = FinanceCalculator.Calculate(facts, 12500m);
        return new { result.Revenue, orders=facts.Count(x=>x.Status==OrderStatus.Completed), units=facts.Where(x=>x.Status==OrderStatus.Completed).SelectMany(x=>x.Lines).Sum(x=>x.Quantity), result.Cogs, result.GrossProfit, result.MarketplaceFees, result.Delivery, result.OperatingProfit, result.OperatingMarginPct, result.CoveragePct, result.IsPreliminary };
    }

    public static async Task<object[]> TimeSeriesAsync(SellerFinanceDbContext db, string tenant) =>
        (await FactsAsync(db, tenant)).Where(x=>x.Status==OrderStatus.Completed).GroupBy(x=>x.Date)
            .Select(g=>{var f=FinanceCalculator.Calculate(g); return (object)new { date=g.Key, revenue=f.Revenue, profit=f.OperatingProfit };}).ToArray();

    public static async Task<object[]> OrdersAsync(SellerFinanceDbContext db, string tenant)
    {
        var entities=await db.Orders.AsNoTracking().Where(x=>x.OrganizationId==tenant).ToDictionaryAsync(x=>x.Id);
        return (await FactsAsync(db,tenant)).Select(x=>(object)new { id=x.Id, date=x.Date, status=x.Status.ToString().ToUpperInvariant(), amount=x.Lines.Sum(y=>y.Revenue), items=x.Lines.Sum(y=>y.Quantity), complete=x.Lines.All(y=>y.UnitCost.HasValue), calculationDateFallback=entities[x.Id].CalculationDateFallback }).ToArray();
    }

    public static async Task<object[]> ProductsAsync(SellerFinanceDbContext db, string tenant)
    {
        var products = await db.Products.AsNoTracking().Where(x=>x.OrganizationId==tenant).ToArrayAsync();
        var histories = await db.ProductCostHistory.AsNoTracking().Where(x=>x.OrganizationId==tenant).ToArrayAsync();
        var facts = await FactsAsync(db, tenant);
        var lines = facts.Where(x=>x.Status==OrderStatus.Completed).SelectMany(x=>x.Lines).ToArray();
        return products.Select(p=>
        {
            var own=lines.Where(x=>x.ProductId==p.Id).ToArray();
            var revenue=own.Sum(x=>x.Revenue);
            var complete=own.All(x=>x.UnitCost.HasValue);
            var cogs=complete ? own.Sum(x=>x.UnitCost!.Value*x.Quantity) : (decimal?)null;
            var costs=own.Sum(x=>(x.ActualFee ?? Decimal.Round(x.Revenue*x.FeeRate,4))+x.Delivery+x.OtherVariableCosts);
            var profit=cogs.HasValue ? revenue-cogs.Value-costs : (decimal?)null;
            var margin=profit.HasValue&&revenue!=0 ? Decimal.Round(profit.Value/revenue*100m,1) : (decimal?)null;
            var current=histories.Where(x=>x.ProductId==p.Id&&x.EffectiveFrom<=DateOnly.FromDateTime(DateTime.UtcNow)).OrderByDescending(x=>x.EffectiveFrom).FirstOrDefault()?.CostAmount;
            return (object)new { id=p.Id, sku=p.Sku, name=p.Name, units=own.Sum(x=>x.Quantity), revenue, cogs, profit, margin, cost=current, coveragePct=revenue==0?100m:Decimal.Round(own.Where(x=>x.UnitCost.HasValue).Sum(x=>x.Revenue)/revenue*100m,2), status=current.HasValue?"profitable":"missing-cost" };
        }).ToArray();
    }

    private static async Task<IReadOnlyList<OrderFact>> FactsAsync(SellerFinanceDbContext db, string tenant)
    {
        var orders=await db.Orders.AsNoTracking().Include(x=>x.Lines).Where(x=>x.OrganizationId==tenant).ToArrayAsync();
        var costs=await db.ProductCostHistory.AsNoTracking().Where(x=>x.OrganizationId==tenant).OrderByDescending(x=>x.EffectiveFrom).ToArrayAsync();
        return orders.Select(x=>{var calculationDate=x.CompletionDate??x.Date;return new OrderFact(x.Id,x.OrganizationId,x.Status,calculationDate,x.Lines.Select(y=>new OrderLine(y.ProductId,y.Revenue,y.Quantity,costs.FirstOrDefault(c=>c.ProductId==y.ProductId&&c.EffectiveFrom<=calculationDate)?.CostAmount??y.UnitCost,y.ActualFee,y.FeeRate,y.Delivery,y.OtherVariableCosts)).ToArray());}).ToArray();
    }
}
