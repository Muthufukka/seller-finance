using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;
using SellerFinance.Domain;

namespace SellerFinance.Api;

public sealed class SellerFinanceDbContext(DbContextOptions<SellerFinanceDbContext> options) : DbContext(options)
{
    public DbSet<OrganizationEntity> Organizations => Set<OrganizationEntity>();
    public DbSet<ProductEntity> Products => Set<ProductEntity>();
    public DbSet<OrderEntity> Orders => Set<OrderEntity>();
    public DbSet<OrderLineEntity> OrderLines => Set<OrderLineEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
    }
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
    public static async Task InitializeAsync(SellerFinanceDbContext db)
    {
        await db.Database.MigrateAsync();
        if (await db.Organizations.AnyAsync()) return;

        db.Organizations.Add(new() { Id = DemoStore.TenantId, Name = "Aspan Market" });
        db.Products.AddRange(
            new ProductEntity { Id="p1", OrganizationId=DemoStore.TenantId, Sku="HOME-101", Name="Органайзер для кухни", CurrentCost=7200m },
            new ProductEntity { Id="p2", OrganizationId=DemoStore.TenantId, Sku="BEAUTY-220", Name="Набор косметичек", CurrentCost=8900m },
            new ProductEntity { Id="p3", OrganizationId=DemoStore.TenantId, Sku="TECH-044", Name="Настольная LED-лампа", CurrentCost=9100m },
            new ProductEntity { Id="p4", OrganizationId=DemoStore.TenantId, Sku="KIDS-018", Name="Развивающий набор" });

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
            Id=id, ExternalId=id, OrganizationId=DemoStore.TenantId, Status=OrderStatus.Completed, Date=date,
            Lines=[new() { Id=Guid.NewGuid(), OrderId=id, ProductId=productId, Revenue=revenue, Quantity=quantity, UnitCost=cost, ActualFee=actualFee, FeeRate=feeRate, Delivery=delivery }]
        };
}
