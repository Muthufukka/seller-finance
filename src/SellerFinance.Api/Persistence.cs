using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Npgsql;
using SellerFinance.Domain;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

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
    public DbSet<FeeRuleEntity> FeeRules => Set<FeeRuleEntity>();
    public DbSet<ActualFeeEntity> ActualFees => Set<ActualFeeEntity>();
    public DbSet<ExpenseEntity> Expenses => Set<ExpenseEntity>();
    public DbSet<FinancialImportJobEntity> FinancialImportJobs => Set<FinancialImportJobEntity>();
    public DbSet<FinancialImportRowEntity> FinancialImportRows => Set<FinancialImportRowEntity>();
    public DbSet<ExportJobEntity> ExportJobs => Set<ExportJobEntity>();
    public DbSet<TelegramConnectionEntity> TelegramConnections => Set<TelegramConnectionEntity>();
    public DbSet<NotificationRuleEntity> NotificationRules => Set<NotificationRuleEntity>();
    public DbSet<NotificationDeliveryEntity> NotificationDeliveries => Set<NotificationDeliveryEntity>();
    public DbSet<OrganizationFeatureFlagEntity> OrganizationFeatureFlags => Set<OrganizationFeatureFlagEntity>();
    public DbSet<SubscriptionEntity> Subscriptions => Set<SubscriptionEntity>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var tenants=ChangedTenants();var result=base.SaveChanges(acceptAllChangesOnSuccess);foreach(var tenant in tenants)DbAnalytics.Invalidate(tenant);return result;
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,CancellationToken cancellationToken=default)
    {
        var tenants=ChangedTenants();var result=await base.SaveChangesAsync(acceptAllChangesOnSuccess,cancellationToken);foreach(var tenant in tenants)DbAnalytics.Invalidate(tenant);return result;
    }

    private string[] ChangedTenants()=>ChangeTracker.Entries().Where(x=>x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).Select(x=>x.Entity switch{OrganizationEntity organization=>organization.Id,_=>x.Metadata.FindProperty("OrganizationId") is null?null:x.Property("OrganizationId").CurrentValue as string}).Where(x=>!String.IsNullOrWhiteSpace(x)).Distinct().Cast<string>().ToArray();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<OrganizationEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<ProductEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<ProductEntity>().HasIndex(x => new { x.OrganizationId, x.Sku }).IsUnique();
        modelBuilder.Entity<OrderEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<OrderEntity>().HasIndex(x => new { x.MarketplaceConnectionId, x.ExternalId }).IsUnique();
        modelBuilder.Entity<OrderEntity>().HasIndex(x=>new{x.OrganizationId,x.Date});
        modelBuilder.Entity<OrderEntity>().HasIndex(x=>new{x.OrganizationId,x.Status,x.CompletionDate});
        modelBuilder.Entity<OrderEntity>().Property(x=>x.TotalPrice).HasPrecision(19,4);
        modelBuilder.Entity<OrderEntity>().Property(x=>x.SellerDeliveryCost).HasPrecision(19,4);
        modelBuilder.Entity<OrderEntity>().HasOne<MarketplaceConnectionEntity>().WithMany().HasForeignKey(x=>x.MarketplaceConnectionId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<OrderLineEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<OrderLineEntity>().HasIndex(x => new { x.OrderId, x.ExternalId }).IsUnique();
        modelBuilder.Entity<OrderLineEntity>().Property(x => x.Revenue).HasPrecision(19, 4);
        modelBuilder.Entity<OrderLineEntity>().Property(x => x.BasePrice).HasPrecision(19, 4);
        modelBuilder.Entity<OrderLineEntity>().Property(x => x.ItemDeliveryCost).HasPrecision(19, 4);
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
        modelBuilder.Entity<MarketplaceConnectionEntity>().HasIndex(x => new { x.OrganizationId, x.Provider, x.DisplayName }).IsUnique();
        modelBuilder.Entity<MarketplaceConnectionEntity>().HasOne<OrganizationEntity>().WithMany().HasForeignKey(x=>x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SyncJobEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<SyncJobEntity>().HasIndex(x => new { x.Status, x.NextAttemptAt });
        modelBuilder.Entity<SyncJobEntity>().HasOne<MarketplaceConnectionEntity>().WithMany().HasForeignKey(x=>x.MarketplaceConnectionId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductCostHistoryEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<ProductCostHistoryEntity>().Property(x => x.CostAmount).HasPrecision(19,4);
        modelBuilder.Entity<ProductCostHistoryEntity>().HasIndex(x => new { x.OrganizationId, x.ProductId, x.EffectiveFrom }).IsUnique();
        modelBuilder.Entity<CostImportJobEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<CostImportRowEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<CostImportRowEntity>().Property(x => x.CostAmount).HasPrecision(19,4);
        modelBuilder.Entity<CostImportRowEntity>().HasIndex(x => new { x.ImportJobId, x.RowNumber });
        modelBuilder.Entity<FeeRuleEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<FeeRuleEntity>().Property(x => x.Value).HasPrecision(19,4);
        modelBuilder.Entity<FeeRuleEntity>().HasIndex(x => new { x.OrganizationId, x.Scope, x.ProductId, x.EffectiveFrom });
        modelBuilder.Entity<ActualFeeEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<ActualFeeEntity>().Property(x => x.Amount).HasPrecision(19,4);
        modelBuilder.Entity<ActualFeeEntity>().HasIndex(x => new { x.OrganizationId, x.OrderLineId }).IsUnique();
        modelBuilder.Entity<ExpenseEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<ExpenseEntity>().Property(x => x.Amount).HasPrecision(19,4);
        modelBuilder.Entity<ExpenseEntity>().HasIndex(x => new { x.OrganizationId, x.Date });
        modelBuilder.Entity<ExpenseEntity>().HasIndex(x => new { x.OrganizationId, x.OrderId, x.Date });
        modelBuilder.Entity<ExpenseEntity>().HasIndex(x => new { x.OrganizationId, x.ImportFingerprint }).IsUnique().HasFilter("\"ImportFingerprint\" IS NOT NULL");
        modelBuilder.Entity<FinancialImportJobEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<FinancialImportJobEntity>().HasIndex(x => new { x.OrganizationId, x.CreatedAt });
        modelBuilder.Entity<FinancialImportRowEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<FinancialImportRowEntity>().Property(x => x.Amount).HasPrecision(19,4);
        modelBuilder.Entity<FinancialImportRowEntity>().HasIndex(x => new { x.ImportJobId, x.RowNumber });
        modelBuilder.Entity<FinancialImportRowEntity>().HasOne<FinancialImportJobEntity>().WithMany().HasForeignKey(x => x.ImportJobId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ExportJobEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<ExportJobEntity>().HasIndex(x => x.DownloadTokenHash).IsUnique();
        modelBuilder.Entity<ExportJobEntity>().HasIndex(x => new { x.Status, x.CreatedAt });
        modelBuilder.Entity<TelegramConnectionEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<TelegramConnectionEntity>().HasIndex(x => x.OrganizationId).IsUnique();
        modelBuilder.Entity<TelegramConnectionEntity>().HasIndex(x => x.LinkCodeHash).IsUnique();
        modelBuilder.Entity<NotificationRuleEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<NotificationRuleEntity>().Property(x => x.Threshold).HasPrecision(19,4);
        modelBuilder.Entity<NotificationRuleEntity>().HasIndex(x => new { x.OrganizationId, x.EventType }).IsUnique();
        modelBuilder.Entity<NotificationDeliveryEntity>().HasKey(x=>x.Id);
        modelBuilder.Entity<NotificationDeliveryEntity>().Property(x=>x.Value).HasPrecision(19,4);
        modelBuilder.Entity<NotificationDeliveryEntity>().HasIndex(x=>new{x.OrganizationId,x.DeduplicationKey}).IsUnique();
        modelBuilder.Entity<NotificationDeliveryEntity>().HasIndex(x=>new{x.Status,x.NextAttemptAt});
        modelBuilder.Entity<OrganizationFeatureFlagEntity>().HasKey(x=>x.Id);
        modelBuilder.Entity<OrganizationFeatureFlagEntity>().HasIndex(x=>new{x.OrganizationId,x.Key}).IsUnique();
        modelBuilder.Entity<OrganizationFeatureFlagEntity>().HasOne<OrganizationEntity>().WithMany().HasForeignKey(x=>x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SubscriptionEntity>().HasKey(x=>x.Id);
        modelBuilder.Entity<SubscriptionEntity>().HasIndex(x=>x.OrganizationId).IsUnique();
        modelBuilder.Entity<SubscriptionEntity>().HasOne<OrganizationEntity>().WithOne().HasForeignKey<SubscriptionEntity>(x=>x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public enum ExportJobStatus { Queued, Running, Succeeded, Failed, Expired }
public enum SubscriptionPlan { Trial, Start, Pro, Business }
public enum SubscriptionStatus { Trialing, Active, Suspended, Expired }
public enum NotificationEventType { MissingCost, NegativeMargin, SyncRequiresAttention }
public enum NotificationDeliveryStatus { Queued, Sending, Sent, RetryScheduled, Failed, Suppressed }

public sealed class ExportJobEntity
{
    public Guid Id { get; set; }
    public string OrganizationId { get; set; } = "";
    public string CreatedByUserId { get; set; } = "";
    public string ReportType { get; set; } = "Products";
    public string Format { get; set; } = "xlsx";
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
    public bool CompleteCostsOnly { get; set; }
    public ExportJobStatus Status { get; set; } = ExportJobStatus.Queued;
    public int RowCount { get; set; }
    public byte[]? FileContent { get; set; }
    public string? ContentType { get; set; }
    public string? FileName { get; set; }
    public string DownloadTokenHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddHours(1);
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ErrorCode { get; set; }
}

public sealed class TelegramConnectionEntity
{
    public Guid Id { get; set; }
    public string OrganizationId { get; set; } = "";
    public string LinkCodeHash { get; set; } = "";
    public long? ChatId { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTimeOffset LinkCodeExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LinkedAt { get; set; }
}

public sealed class NotificationRuleEntity
{
    public Guid Id { get; set; }
    public string OrganizationId { get; set; } = "";
    public NotificationEventType EventType { get; set; }
    public bool Enabled { get; set; } = true;
    public decimal? Threshold { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class NotificationDeliveryEntity
{
    public Guid Id { get; set; }
    public string OrganizationId { get; set; } = "";
    public NotificationEventType EventType { get; set; }
    public string DeduplicationKey { get; set; } = "";
    public string Message { get; set; } = "";
    public decimal? Value { get; set; }
    public NotificationDeliveryStatus Status { get; set; } = NotificationDeliveryStatus.Queued;
    public int Attempt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SentAt { get; set; }
    public string? ErrorCode { get; set; }
}

public sealed class OrganizationFeatureFlagEntity
{
    public Guid Id { get; set; }
    public string OrganizationId { get; set; } = "";
    public string Key { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string UpdatedByUserId { get; set; } = "";
}

public sealed class SubscriptionEntity
{
    public Guid Id { get; set; }
    public string OrganizationId { get; set; } = "";
    public SubscriptionPlan Plan { get; set; } = SubscriptionPlan.Trial;
    public string BillingPeriod { get; set; } = "Monthly";
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Trialing;
    public DateTimeOffset PeriodStart { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset PeriodEnd { get; set; } = DateTimeOffset.UtcNow.AddDays(14);
    public DateTimeOffset? TrialEndsAt { get; set; } = DateTimeOffset.UtcNow.AddDays(14);
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum FeeRuleScope { Default, Category, Product }
public enum FeeValueType { Percentage, Fixed }
public enum ExpenseType { Advertising, Packaging, Fulfillment, Services, Other }
public enum ExpenseSource { Manual, Import }
public enum FinancialImportType { Expenses, ActualFees }
public enum FinancialImportStatus { Preview, Applied, Rejected, Expired }

public sealed class FeeRuleEntity
{
    public Guid Id { get; set; }
    public string OrganizationId { get; set; } = "";
    public FeeRuleScope Scope { get; set; }
    public string? ProductId { get; set; }
    public string? Category { get; set; }
    public FeeValueType ValueType { get; set; }
    public decimal Value { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string CreatedByUserId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ActualFeeEntity
{
    public Guid Id { get; set; }
    public string OrganizationId { get; set; } = "";
    public Guid OrderLineId { get; set; }
    public decimal Amount { get; set; }
    public string Source { get; set; } = "Manual";
    public Guid? ImportJobId { get; set; }
    public string? ExternalRef { get; set; }
    public string CreatedByUserId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ExpenseEntity
{
    public Guid Id { get; set; }
    public string OrganizationId { get; set; } = "";
    public ExpenseType Type { get; set; }
    public decimal Amount { get; set; }
    public DateOnly Date { get; set; }
    public DateOnly? PeriodEnd { get; set; }
    public string? ProductId { get; set; }
    public string? OrderId { get; set; }
    public string? Comment { get; set; }
    public ExpenseSource Source { get; set; } = ExpenseSource.Manual;
    public Guid? ImportJobId { get; set; }
    public string? ImportFingerprint { get; set; }
    public string CreatedByUserId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class FinancialImportJobEntity
{
    public Guid Id { get; set; }
    public string OrganizationId { get; set; } = "";
    public string CreatedByUserId { get; set; } = "";
    public FinancialImportType Type { get; set; }
    public string FileNameSafe { get; set; } = "";
    public FinancialImportStatus Status { get; set; } = FinancialImportStatus.Preview;
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int UpdateRows { get; set; }
    public int DuplicateRows { get; set; }
    public int ErrorRows { get; set; }
    public int ExpectedChanges { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddHours(24);
    public DateTimeOffset? AppliedAt { get; set; }
}

public sealed class FinancialImportRowEntity
{
    public Guid Id { get; set; }
    public Guid ImportJobId { get; set; }
    public int RowNumber { get; set; }
    public string Status { get; set; } = "Valid";
    public string? Error { get; set; }
    public ExpenseType? ExpenseType { get; set; }
    public decimal? Amount { get; set; }
    public DateOnly? Date { get; set; }
    public DateOnly? PeriodEnd { get; set; }
    public string? ProductId { get; set; }
    public string? OrderId { get; set; }
    public Guid? OrderLineId { get; set; }
    public string? Comment { get; set; }
    public string? ExternalRef { get; set; }
    public string? Fingerprint { get; set; }
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
    public string DisplayName { get; set; } = "Kaspi Магазин";
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
    public bool AllocateOrganizationExpenses { get; set; }
    public string Status { get; set; } = "Active";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ProductEntity
{
    public string Id { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Category { get; set; }
    public string? ExternalProductId { get; set; }
    public string Status { get; set; } = "Active";
}

public sealed class OrderEntity
{
    public string Id { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public Guid MarketplaceConnectionId { get; set; }
    public string ExternalId { get; set; } = "";
    public string Code { get; set; } = "";
    public decimal TotalPrice { get; set; }
    public string? PaymentMode { get; set; }
    public decimal SellerDeliveryCost { get; set; }
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
    public string? ExternalId { get; set; }
    public string ProductId { get; set; } = "";
    public decimal? BasePrice { get; set; }
    public decimal? ItemDeliveryCost { get; set; }
    public decimal Revenue { get; set; }
    public int Quantity { get; set; }
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
    private static readonly Guid DemoConnectionId=Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static async Task InitializeAsync(SellerFinanceDbContext db)
    {
        if(db.Database.IsRelational())await db.Database.MigrateAsync();else await db.Database.EnsureCreatedAsync();
        if (await db.Organizations.AnyAsync()){await EnsureNotificationRulesAsync(db);return;}

        db.Organizations.Add(new() { Id = DemoTenantId, Name = "Aspan Market" });
        db.Subscriptions.Add(new(){Id=Guid.NewGuid(),OrganizationId=DemoTenantId});
        db.MarketplaceConnections.Add(new(){Id=DemoConnectionId,OrganizationId=DemoTenantId,DisplayName="Demo data",Status=MarketplaceConnectionStatus.Disabled});
        db.Products.AddRange(
            new ProductEntity { Id="p1", OrganizationId=DemoTenantId, Sku="HOME-101", Name="Органайзер для кухни" },
            new ProductEntity { Id="p2", OrganizationId=DemoTenantId, Sku="BEAUTY-220", Name="Набор косметичек" },
            new ProductEntity { Id="p3", OrganizationId=DemoTenantId, Sku="TECH-044", Name="Настольная LED-лампа" },
            new ProductEntity { Id="p4", OrganizationId=DemoTenantId, Sku="KIDS-018", Name="Развивающий набор" });

        db.ProductCostHistory.AddRange(new(){Id=Guid.NewGuid(),OrganizationId=DemoTenantId,ProductId="p1",CostAmount=7200m,EffectiveFrom=new(2026,1,1),Source=CostSource.Legacy,CreatedByUserId="seed"},new(){Id=Guid.NewGuid(),OrganizationId=DemoTenantId,ProductId="p2",CostAmount=8900m,EffectiveFrom=new(2026,1,1),Source=CostSource.Legacy,CreatedByUserId="seed"},new(){Id=Guid.NewGuid(),OrganizationId=DemoTenantId,ProductId="p3",CostAmount=9100m,EffectiveFrom=new(2026,1,1),Source=CostSource.Legacy,CreatedByUserId="seed"});
        db.Orders.AddRange(ToEntity("KSP-10482", new(2026,8,6), "p1", 24990m,2,null,.109m,700m),
            ToEntity("KSP-10497", new(2026,8,7), "p2",18490m,1,null,.109m,450m),
            ToEntity("KSP-10511", new(2026,8,8), "p3",42900m,3,4200m,.109m,900m),
            ToEntity("KSP-10529", new(2026,8,9), "p4",12990m,1,null,.12m,350m),
            ToEntity("KSP-10543", new(2026,8,10), "p1",37485m,3,null,.109m,800m),
            ToEntity("KSP-10561", new(2026,8,11), "p2",36980m,2,null,.109m,650m));
        await db.SaveChangesAsync();
        await EnsureNotificationRulesAsync(db);
    }

    private static async Task EnsureNotificationRulesAsync(SellerFinanceDbContext db)
    {
        var organizations=await db.Organizations.Select(x=>x.Id).ToArrayAsync();var existing=await db.NotificationRules.Select(x=>new{x.OrganizationId,x.EventType}).ToArrayAsync();foreach(var organizationId in organizations)foreach(var type in Enum.GetValues<NotificationEventType>())if(!existing.Any(x=>x.OrganizationId==organizationId&&x.EventType==type))db.NotificationRules.Add(new(){Id=Guid.NewGuid(),OrganizationId=organizationId,EventType=type,Enabled=true,Threshold=type==NotificationEventType.NegativeMargin?0m:null});try{await db.SaveChangesAsync();}catch(DbUpdateException ex)when(ex.InnerException is PostgresException{SqlState:PostgresErrorCodes.UniqueViolation}){db.ChangeTracker.Clear();}
    }

    private static OrderEntity ToEntity(string id, DateOnly date, string productId, decimal revenue, int quantity,
        decimal? actualFee, decimal feeRate, decimal delivery) => new()
        {
            Id=id, ExternalId=id, OrganizationId=DemoTenantId, MarketplaceConnectionId=DemoConnectionId,Status=OrderStatus.Completed, Date=date, CompletionDate=date,
            Lines=[new() { Id=Guid.NewGuid(), OrderId=id, ProductId=productId, Revenue=revenue, Quantity=quantity, ActualFee=actualFee, FeeRate=feeRate, Delivery=delivery }]
        };
}

public static class DbAnalytics
{
    private readonly record struct FactsCacheKey(string Database,string Tenant,DateOnly? From,DateOnly? To,bool CompleteCostsOnly,long Version);
    private static readonly ConcurrentDictionary<string,long> Versions=new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<FactsCacheKey,Lazy<Task<IReadOnlyList<OrderFact>>>> FactsCache=[];

    public static void Invalidate(string tenant)
    {
        Versions.AddOrUpdate(tenant,1,(_,version)=>version+1);
        if(FactsCache.Count>256)foreach(var key in FactsCache.Keys.Where(x=>x.Version<Versions.GetValueOrDefault(x.Tenant)-1))FactsCache.TryRemove(key,out _);
    }

    public static async Task<object> SummaryAsync(SellerFinanceDbContext db, string tenant,DateOnly? from=null,DateOnly? to=null,bool completeCostsOnly=false)
    {
        var facts = await FactsAsync(db, tenant,from,to,completeCostsOnly);
        var completedOrderIds=await db.Orders.AsNoTracking().Where(x=>x.OrganizationId==tenant&&x.Status==OrderStatus.Completed&&(!from.HasValue||(x.CompletionDate??x.Date)>=from)&&(!to.HasValue||(x.CompletionDate??x.Date)<=to)).Select(x=>x.Id).ToArrayAsync();var expenseRows=await db.Expenses.AsNoTracking().Where(x=>x.OrganizationId==tenant&&(x.OrderId==null||!completedOrderIds.Contains(x.OrderId))&&(!from.HasValue||(x.PeriodEnd??x.Date)>=from)&&(!to.HasValue||x.Date<=to)).ToArrayAsync();var expenses=expenseRows.Sum(x=>ExpenseRecognition.Amount(x,from,to));
        var result = FinanceCalculator.Calculate(facts, expenses);
        return new { result.Revenue, orders=facts.Count(x=>x.Status==OrderStatus.Completed), units=facts.Where(x=>x.Status==OrderStatus.Completed).SelectMany(x=>x.Lines).Sum(x=>x.Quantity), result.Cogs, result.GrossProfit, result.MarketplaceFees, result.Delivery, result.OperatingExpenses, result.OperatingProfit, result.OperatingMarginPct, result.CoveragePct, result.IsPreliminary };
    }

    public static async Task<object[]> TimeSeriesAsync(SellerFinanceDbContext db, string tenant,DateOnly? from=null,DateOnly? to=null,bool completeCostsOnly=false)
    {
        var completedOrderIds=await db.Orders.AsNoTracking().Where(x=>x.OrganizationId==tenant&&x.Status==OrderStatus.Completed&&(!from.HasValue||(x.CompletionDate??x.Date)>=from)&&(!to.HasValue||(x.CompletionDate??x.Date)<=to)).Select(x=>x.Id).ToArrayAsync();var expenseRows=await db.Expenses.AsNoTracking().Where(x=>x.OrganizationId==tenant&&(x.OrderId==null||!completedOrderIds.Contains(x.OrderId))&&(!from.HasValue||(x.PeriodEnd??x.Date)>=from)&&(!to.HasValue||x.Date<=to)).ToArrayAsync();var expenses=ExpenseRecognition.ByDay(expenseRows,from,to);
        var groups=(await FactsAsync(db,tenant,from,to,completeCostsOnly)).Where(x=>x.Status==OrderStatus.Completed).GroupBy(x=>x.Date).ToDictionary(x=>x.Key,x=>(IEnumerable<OrderFact>)x);
        return groups.Keys.Union(expenses.Keys).OrderBy(x=>x).Select(date=>{var f=FinanceCalculator.Calculate(groups.GetValueOrDefault(date)??[],expenses.GetValueOrDefault(date));return(object)new{date,revenue=f.Revenue,profit=f.OperatingProfit};}).ToArray();
    }

    public static async Task<object[]> ProductTimeSeriesAsync(SellerFinanceDbContext db,string tenant,string productId,DateOnly? from=null,DateOnly? to=null)
    {
        var facts=(await FactsAsync(db,tenant,from,to)).Where(x=>x.Status==OrderStatus.Completed).ToArray();
        var productFacts=facts.Select(x=>x with{Lines=x.Lines.Where(y=>y.ProductId==productId).ToArray()}).Where(x=>x.Lines.Count>0).ToArray();
        var directRows=await db.Expenses.AsNoTracking().Where(x=>x.OrganizationId==tenant&&x.ProductId==productId&&x.OrderId==null&&(!from.HasValue||(x.PeriodEnd??x.Date)>=from)&&(!to.HasValue||x.Date<=to)).ToArrayAsync();
        var directByDay=ExpenseRecognition.ByDay(directRows,from,to);
        Dictionary<DateOnly,decimal> allocatedByDay=[];
        var allocate=await db.Organizations.AsNoTracking().Where(x=>x.Id==tenant).Select(x=>x.AllocateOrganizationExpenses).SingleOrDefaultAsync();
        if(allocate)
        {
            var organizationRows=await db.Expenses.AsNoTracking().Where(x=>x.OrganizationId==tenant&&x.ProductId==null&&x.OrderId==null&&(!from.HasValue||(x.PeriodEnd??x.Date)>=from)&&(!to.HasValue||x.Date<=to)).ToArrayAsync();
            foreach(var expense in ExpenseRecognition.ByDay(organizationRows,from,to))
            {
                var revenues=facts.Where(x=>x.Date==expense.Key).SelectMany(x=>x.Lines).GroupBy(x=>x.ProductId).OrderBy(x=>x.Key).Select(x=>new{ProductId=x.Key,Revenue=x.Sum(y=>y.Revenue)}).Where(x=>x.Revenue>0).ToArray();
                if(revenues.Length==0)continue;
                var allocations=FinanceCalculator.AllocateByRevenue(expense.Value,revenues.Select(x=>x.Revenue).ToArray());
                var index=Array.FindIndex(revenues,x=>x.ProductId==productId);if(index>=0)allocatedByDay[expense.Key]=allocations[index];
            }
        }
        var groups=productFacts.GroupBy(x=>x.Date).ToDictionary(x=>x.Key,x=>(IEnumerable<OrderFact>)x);
        return groups.Keys.Union(directByDay.Keys).Union(allocatedByDay.Keys).OrderBy(x=>x).Select(date=>
        {
            var orders=groups.GetValueOrDefault(date)?.ToArray()??[];var expenses=directByDay.GetValueOrDefault(date)+allocatedByDay.GetValueOrDefault(date);var result=FinanceCalculator.Calculate(orders,expenses);var lines=orders.SelectMany(x=>x.Lines).ToArray();
            return(object)new{date,units=lines.Sum(x=>x.Quantity),result.Revenue,result.Cogs,result.MarketplaceFees,result.Delivery,otherVariableCosts=result.VariableCosts-result.MarketplaceFees-result.Delivery,expenses,result.OperatingProfit,result.OperatingMarginPct,result.CoveragePct,result.IsPreliminary};
        }).ToArray();
    }

    public static async Task<object> DashboardProblemsAsync(SellerFinanceDbContext db,string tenant,DateOnly? from=null,DateOnly? to=null)
    {
        var products=(await ProductsAsync(db,tenant,from,to)).Select(x=>JsonSerializer.SerializeToElement(x)).ToArray();
        var active=products.Where(x=>!String.Equals(x.GetProperty("productStatus").GetString(),"Archived",StringComparison.OrdinalIgnoreCase)).ToArray();
        var missingAll=active.Where(x=>x.GetProperty("revenue").GetDecimal()>0&&x.GetProperty("coveragePct").GetDecimal()<100m).OrderByDescending(x=>x.GetProperty("revenue").GetDecimal()).ToArray();var missing=missingAll.Take(10).Select(x=>new{id=x.GetProperty("id").GetString(),sku=x.GetProperty("sku").GetString(),name=x.GetProperty("name").GetString(),revenue=x.GetProperty("revenue").GetDecimal(),coveragePct=x.GetProperty("coveragePct").GetDecimal()}).ToArray();
        var negativeAll=active.Where(x=>x.GetProperty("profit").ValueKind==JsonValueKind.Number&&x.GetProperty("profit").GetDecimal()<0).OrderBy(x=>x.GetProperty("profit").GetDecimal()).ToArray();var negative=negativeAll.Take(10).Select(x=>new{id=x.GetProperty("id").GetString(),sku=x.GetProperty("sku").GetString(),name=x.GetProperty("name").GetString(),profit=x.GetProperty("profit").GetDecimal(),margin=x.GetProperty("margin").ValueKind==JsonValueKind.Number?x.GetProperty("margin").GetDecimal():(decimal?)null}).ToArray();
        var syncQuery=from job in db.SyncJobs.AsNoTracking() join connection in db.MarketplaceConnections.AsNoTracking() on job.MarketplaceConnectionId equals connection.Id where job.OrganizationId==tenant&&connection.OrganizationId==tenant&&job.Status==SyncJobStatus.RequiresAttention select new{job.Id,connectionId=connection.Id,storeName=connection.DisplayName,errorCode=job.ErrorCode??connection.LastErrorCode,job.CreatedAt};var syncCount=await syncQuery.CountAsync();var sync=await syncQuery.OrderByDescending(x=>x.CreatedAt).Take(10).ToArrayAsync();
        return new{missingCosts=missing,negativeMargins=negative,syncIssues=sync,missingCostCount=missingAll.Length,negativeMarginCount=negativeAll.Length,syncIssueCount=syncCount,totalCount=missingAll.Length+negativeAll.Length+syncCount};
    }

    public static async Task<object> OrdersAsync(SellerFinanceDbContext db, string tenant, string? status=null,
        DateOnly? from=null, DateOnly? to=null, string? productId=null, decimal? profitFrom=null,
        decimal? profitTo=null, string? search=null, int page=1, int pageSize=50,bool completeCostsOnly=false)
    {
        page=Math.Max(1,page);pageSize=Math.Clamp(pageSize,1,100);
        var entities=await db.Orders.AsNoTracking().Where(x=>x.OrganizationId==tenant).ToDictionaryAsync(x=>x.Id);
        var stores=await db.MarketplaceConnections.AsNoTracking().Where(x=>x.OrganizationId==tenant).ToDictionaryAsync(x=>x.Id,x=>x.DisplayName);
        var rows=(await FactsAsync(db,tenant,from,to)).Select(x=>{var f=FinanceCalculator.Calculate([x]);var entity=entities[x.Id];var other=f.VariableCosts-f.MarketplaceFees-f.Delivery;return new OrderListRow(x.Id,entity.ExternalId,entity.MarketplaceConnectionId,stores.GetValueOrDefault(entity.MarketplaceConnectionId,"Kaspi"),x.Date,x.Status.ToString().ToUpperInvariant(),x.Lines.Sum(y=>y.Revenue),x.Lines.Sum(y=>y.Quantity),f.Cogs,f.MarketplaceFees,x.Lines.Sum(y=>y.Delivery),other,f.Cogs.HasValue?f.Cogs.Value+f.VariableCosts:(decimal?)null,f.OperatingProfit,f.CoveragePct,x.Lines.All(y=>y.UnitCost.HasValue),entity.CalculationDateFallback,x.Lines.Select(y=>y.ProductId).ToArray());});
        if(!String.IsNullOrWhiteSpace(status))rows=rows.Where(x=>String.Equals(x.Status,status.Trim(),StringComparison.OrdinalIgnoreCase));
        if(!String.IsNullOrWhiteSpace(productId))rows=rows.Where(x=>x.ProductIds.Contains(productId));
        if(profitFrom.HasValue)rows=rows.Where(x=>x.Profit.HasValue&&x.Profit>=profitFrom);
        if(profitTo.HasValue)rows=rows.Where(x=>x.Profit.HasValue&&x.Profit<=profitTo);
        if(!String.IsNullOrWhiteSpace(search))rows=rows.Where(x=>x.ExternalId.Contains(search.Trim(),StringComparison.OrdinalIgnoreCase));
        if(completeCostsOnly)rows=rows.Where(x=>x.Complete);
        var filtered=rows.OrderByDescending(x=>x.Date).ThenByDescending(x=>x.ExternalId).ToArray();var total=filtered.Length;
        var items=filtered.Skip((page-1)*pageSize).Take(pageSize).Select(x=>new{id=x.Id,externalId=x.ExternalId,connectionId=x.ConnectionId,storeName=x.StoreName,date=x.Date,status=x.Status,amount=x.Amount,items=x.Items,cogs=x.Cogs,fees=x.Fees,delivery=x.Delivery,otherExpenses=x.OtherExpenses,totalExpenses=x.TotalExpenses,profit=x.Profit,coveragePct=x.CoveragePct,complete=x.Complete,calculationDateFallback=x.CalculationDateFallback}).ToArray();
        return new{items,page,pageSize,totalCount=total,totalPages=(int)Math.Ceiling(total/(decimal)pageSize)};
    }

    private sealed record OrderListRow(string Id,string ExternalId,Guid ConnectionId,string StoreName,DateOnly Date,string Status,decimal Amount,int Items,decimal? Cogs,decimal Fees,decimal Delivery,decimal OtherExpenses,decimal? TotalExpenses,decimal? Profit,decimal CoveragePct,bool Complete,bool CalculationDateFallback,string[] ProductIds);

    public static async Task<object[]> ProductsAsync(SellerFinanceDbContext db, string tenant,DateOnly? from=null,DateOnly? to=null,bool completeCostsOnly=false)
    {
        var products = await db.Products.AsNoTracking().Where(x=>x.OrganizationId==tenant).ToArrayAsync();
        var histories = await db.ProductCostHistory.AsNoTracking().Where(x=>x.OrganizationId==tenant).ToArrayAsync();
        var facts = await FactsAsync(db, tenant,from,to,completeCostsOnly);
        var productExpenseRows=await db.Expenses.AsNoTracking().Where(x=>x.OrganizationId==tenant&&x.ProductId!=null&&x.OrderId==null&&(!from.HasValue||(x.PeriodEnd??x.Date)>=from)&&(!to.HasValue||x.Date<=to)).ToArrayAsync();var productExpenses=productExpenseRows.GroupBy(x=>x.ProductId!).ToDictionary(x=>x.Key,x=>x.Sum(y=>ExpenseRecognition.Amount(y,from,to)));
        var lines = facts.Where(x=>x.Status==OrderStatus.Completed).SelectMany(x=>x.Lines).ToArray();
        var allocate=await db.Organizations.AsNoTracking().Where(x=>x.Id==tenant).Select(x=>x.AllocateOrganizationExpenses).SingleOrDefaultAsync();var allocatedExpenses=allocate?await AllocateOrganizationExpensesAsync(db,tenant,lines,from,to):[];
        return products.Select(p=>
        {
            var own=lines.Where(x=>x.ProductId==p.Id).ToArray();
            var revenue=own.Sum(x=>x.Revenue);
            var complete=own.All(x=>x.UnitCost.HasValue);
            var cogs=complete ? own.Sum(x=>x.UnitCost!.Value*x.Quantity) : (decimal?)null;
            var marketplaceFees=own.Sum(x=>x.ActualFee ?? Decimal.Round(x.Revenue*x.FeeRate,4));var delivery=own.Sum(x=>x.Delivery);var orderExpenses=own.Sum(x=>x.OtherVariableCosts);
            var directExpenses=productExpenses.GetValueOrDefault(p.Id);var allocatedOrganizationExpenses=allocatedExpenses.GetValueOrDefault(p.Id);var otherExpenses=orderExpenses+directExpenses+allocatedOrganizationExpenses;var profit=cogs.HasValue ? revenue-cogs.Value-marketplaceFees-delivery-otherExpenses : (decimal?)null;
            var margin=profit.HasValue&&revenue!=0 ? Decimal.Round(profit.Value/revenue*100m,1) : (decimal?)null;
            var current=histories.Where(x=>x.ProductId==p.Id&&x.EffectiveFrom<=DateOnly.FromDateTime(DateTime.UtcNow)).OrderByDescending(x=>x.EffectiveFrom).FirstOrDefault()?.CostAmount;
            return (object)new { id=p.Id, sku=p.Sku, name=p.Name, p.Category, p.ExternalProductId, productStatus=p.Status, units=own.Sum(x=>x.Quantity), revenue, cogs, marketplaceFees, delivery, orderExpenses, directExpenses, allocatedOrganizationExpenses, otherExpenses, profit, margin, cost=current, coveragePct=revenue==0?100m:Decimal.Round(own.Where(x=>x.UnitCost.HasValue).Sum(x=>x.Revenue)/revenue*100m,2), status=p.Status=="Archived"?"archived":current.HasValue?"profitable":"missing-cost" };
        }).ToArray();
    }

    public static async Task<object[]> AbcAsync(SellerFinanceDbContext db,string tenant,string metric="profit",DateOnly? from=null,DateOnly? to=null,bool completeCostsOnly=false)
    {
        var products=await db.Products.AsNoTracking().Where(x=>x.OrganizationId==tenant).ToDictionaryAsync(x=>x.Id);var expenseRows=await db.Expenses.AsNoTracking().Where(x=>x.OrganizationId==tenant&&x.ProductId!=null&&x.OrderId==null&&(!from.HasValue||(x.PeriodEnd??x.Date)>=from)&&(!to.HasValue||x.Date<=to)).ToArrayAsync();var expenses=expenseRows.GroupBy(x=>x.ProductId!).ToDictionary(x=>x.Key,x=>x.Sum(y=>ExpenseRecognition.Amount(y,from,to)));
        var facts=await FactsAsync(db,tenant,from,to,completeCostsOnly);var allLines=facts.Where(x=>x.Status==OrderStatus.Completed).SelectMany(x=>x.Lines).ToArray();var allocate=await db.Organizations.AsNoTracking().Where(x=>x.Id==tenant).Select(x=>x.AllocateOrganizationExpenses).SingleOrDefaultAsync();var allocations=allocate?await AllocateOrganizationExpensesAsync(db,tenant,allLines,from,to):[];var values=allLines.GroupBy(x=>x.ProductId).Select(g=>{var finance=FinanceCalculator.Calculate([new OrderFact("abc",tenant,OrderStatus.Completed,from??DateOnly.MinValue,g.ToArray())],expenses.GetValueOrDefault(g.Key)+allocations.GetValueOrDefault(g.Key));var value=metric switch{"revenue"=>finance.Revenue,"units"=>g.Sum(x=>x.Quantity),"grossProfit"=>finance.GrossProfit,_=>finance.OperatingProfit};return new{ProductId=g.Key,Value=value,Revenue=finance.Revenue,Profit=finance.OperatingProfit,Units=g.Sum(x=>x.Quantity)};}).OrderByDescending(x=>x.Value).ToArray();
        var total=values.Where(x=>x.Value>0).Sum(x=>x.Value);decimal cumulative=0;return values.Select(x=>{if(x.Value>0)cumulative+=x.Value;var share=total==0?0:Decimal.Round(cumulative/total*100m,2);var group=share<=80?"A":share<=95?"B":"C";products.TryGetValue(x.ProductId,out var p);return(object)new{productId=x.ProductId,sku=p?.Sku??x.ProductId,name=p?.Name??"Несопоставленный товар",x.Value,x.Revenue,x.Profit,x.Units,cumulativePct=share,group};}).ToArray();
    }

    public static async Task<object?> OrderDetailAsync(SellerFinanceDbContext db,string tenant,string id)
    {
        var entity=await db.Orders.AsNoTracking().Include(x=>x.Lines).SingleOrDefaultAsync(x=>x.Id==id&&x.OrganizationId==tenant);if(entity is null)return null;
        var fact=(await FactsAsync(db,tenant)).Single(x=>x.Id==id);var result=FinanceCalculator.Calculate([fact]);var products=await db.Products.AsNoTracking().Where(x=>x.OrganizationId==tenant).ToDictionaryAsync(x=>x.Id);
        var storeName=await db.MarketplaceConnections.AsNoTracking().Where(x=>x.Id==entity.MarketplaceConnectionId&&x.OrganizationId==tenant).Select(x=>x.DisplayName).SingleOrDefaultAsync();return new{entity.Id,entity.ExternalId,entity.Code,entity.TotalPrice,entity.PaymentMode,entity.SellerDeliveryCost,entity.CompletionDate,connectionId=entity.MarketplaceConnectionId,storeName=storeName??"Kaspi",date=fact.Date,status=entity.Status.ToString(),entity.CalculationDateFallback,result.Revenue,result.Cogs,result.MarketplaceFees,result.Delivery,result.VariableCosts,result.OperatingProfit,result.OperatingMarginPct,result.CoveragePct,lines=fact.Lines.Select((x,i)=>{products.TryGetValue(x.ProductId,out var p);var fee=x.ActualFee??Decimal.Round(x.Revenue*x.FeeRate,4);var cogs=x.UnitCost*x.Quantity;return new{id=entity.Lines[i].Id,x.ProductId,sku=p?.Sku,name=p?.Name,externalProductId=p?.ExternalProductId,x.Quantity,x.Revenue,entity.Lines[i].BasePrice,entity.Lines[i].ItemDeliveryCost,x.UnitCost,cogs,fee,x.Delivery,x.OtherVariableCosts,profit=cogs.HasValue?x.Revenue-cogs.Value-fee-x.Delivery-x.OtherVariableCosts:(decimal?)null};})};
    }

    private static async Task<IReadOnlyList<OrderFact>> FactsAsync(SellerFinanceDbContext db, string tenant,DateOnly? from=null,DateOnly? to=null,bool completeCostsOnly=false)
    {
        var database=db.Database.IsRelational()?Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(db.Database.GetConnectionString()??db.Database.ProviderName??"relational"))):db.ContextId.InstanceId.ToString("N");var key=new FactsCacheKey(database,tenant,from,to,completeCostsOnly,Versions.GetValueOrDefault(tenant));var lazy=FactsCache.GetOrAdd(key,_=>new(()=>LoadFactsAsync(db,tenant,from,to,completeCostsOnly),LazyThreadSafetyMode.ExecutionAndPublication));
        try{return await lazy.Value;}catch{FactsCache.TryRemove(key,out _);throw;}
    }

    private static async Task<IReadOnlyList<OrderFact>> LoadFactsAsync(SellerFinanceDbContext db, string tenant,DateOnly? from,DateOnly? to,bool completeCostsOnly)
    {
        var orders=await db.Orders.AsNoTracking().Include(x=>x.Lines).Where(x=>x.OrganizationId==tenant&&(!from.HasValue||(x.CompletionDate??x.Date)>=from)&&(!to.HasValue||(x.CompletionDate??x.Date)<=to)).ToArrayAsync();
        var costs=(await db.ProductCostHistory.AsNoTracking().Where(x=>x.OrganizationId==tenant).ToArrayAsync()).GroupBy(x=>x.ProductId).ToDictionary(x=>x.Key,x=>x.OrderByDescending(y=>y.EffectiveFrom).ToArray());
        var actualFees=await db.ActualFees.AsNoTracking().Where(x=>x.OrganizationId==tenant).ToDictionaryAsync(x=>x.OrderLineId,x=>x.Amount);
        var ruleRows=await db.FeeRules.AsNoTracking().Where(x=>x.OrganizationId==tenant).ToArrayAsync();var defaultRules=ruleRows.Where(x=>x.Scope==FeeRuleScope.Default).OrderByDescending(x=>x.EffectiveFrom).ToArray();var productRules=ruleRows.Where(x=>x.Scope==FeeRuleScope.Product&&x.ProductId!=null).GroupBy(x=>x.ProductId!).ToDictionary(x=>x.Key,x=>x.OrderByDescending(y=>y.EffectiveFrom).ToArray());var categoryRules=ruleRows.Where(x=>x.Scope==FeeRuleScope.Category&&x.Category!=null).GroupBy(x=>x.Category!).ToDictionary(x=>x.Key,x=>x.OrderByDescending(y=>y.EffectiveFrom).ToArray());var products=await db.Products.AsNoTracking().Where(x=>x.OrganizationId==tenant).ToDictionaryAsync(x=>x.Id);
        var completedOrderIds=orders.Where(x=>x.Status==OrderStatus.Completed).Select(x=>x.Id).ToHashSet();var orderExpenseRows=await db.Expenses.AsNoTracking().Where(x=>x.OrganizationId==tenant&&x.OrderId!=null&&(!from.HasValue||(x.PeriodEnd??x.Date)>=from)&&(!to.HasValue||x.Date<=to)).ToArrayAsync();var orderExpenses=orderExpenseRows.Where(x=>completedOrderIds.Contains(x.OrderId!)).GroupBy(x=>x.OrderId!).ToDictionary(x=>x.Key,x=>x.Sum(y=>ExpenseRecognition.Amount(y,from,to)));
        FeeRuleEntity? Active(FeeRuleEntity[] source,DateOnly date)=>source.FirstOrDefault(x=>x.EffectiveFrom<=date&&(!x.EffectiveTo.HasValue||x.EffectiveTo>=date));
        return orders.Select(x=>{var calculationDate=x.CompletionDate??x.Date;var expenseTotal=orderExpenses.GetValueOrDefault(x.Id);var lines=x.Lines.Select(y=>{products.TryGetValue(y.ProductId,out var product);FeeRuleEntity? rule=null;if(productRules.TryGetValue(y.ProductId,out var perProduct))rule=Active(perProduct,calculationDate);if(rule is null&&product?.Category is not null&&categoryRules.TryGetValue(product.Category,out var perCategory))rule=Active(perCategory,calculationDate);rule??=Active(defaultRules,calculationDate);decimal? actual=actualFees.TryGetValue(y.Id,out var imported)?imported:y.ActualFee;var rate=y.FeeRate;if(actual is null&&rule is not null){if(rule.ValueType==FeeValueType.Fixed)actual=rule.Value;else rate=rule.Value/100m;}costs.TryGetValue(y.ProductId,out var productCosts);var cost=productCosts?.FirstOrDefault(c=>c.EffectiveFrom<=calculationDate)?.CostAmount;return new OrderLine(y.ProductId,y.Revenue,y.Quantity,cost,actual,rate,y.Delivery,y.OtherVariableCosts);}).ToArray();if(completeCostsOnly)lines=lines.Where(y=>y.UnitCost.HasValue).ToArray();var allocations=FinanceCalculator.AllocateByRevenue(expenseTotal,lines.Select(y=>y.Revenue).ToArray());lines=lines.Select((line,index)=>line with{OtherVariableCosts=line.OtherVariableCosts+allocations[index]}).ToArray();return new OrderFact(x.Id,x.OrganizationId,x.Status,calculationDate,lines);}).Where(x=>!completeCostsOnly||x.Lines.Count>0).ToArray();
    }

    private static async Task<Dictionary<string,decimal>> AllocateOrganizationExpensesAsync(SellerFinanceDbContext db,string tenant,IReadOnlyList<OrderLine> lines,DateOnly? from,DateOnly? to)
    {
        var expenses=await db.Expenses.AsNoTracking().Where(x=>x.OrganizationId==tenant&&x.ProductId==null&&x.OrderId==null&&(!from.HasValue||(x.PeriodEnd??x.Date)>=from)&&(!to.HasValue||x.Date<=to)).ToArrayAsync();var total=expenses.Sum(x=>ExpenseRecognition.Amount(x,from,to));
        var revenues=lines.GroupBy(x=>x.ProductId).Select(x=>new{ProductId=x.Key,Revenue=x.Sum(y=>y.Revenue)}).Where(x=>x.Revenue>0).OrderBy(x=>x.ProductId).ToArray();if(total==0||revenues.Length==0)return [];
        var allocated=FinanceCalculator.AllocateByRevenue(total,revenues.Select(x=>x.Revenue).ToArray());return revenues.Select((x,index)=>(x.ProductId,Amount:allocated[index])).ToDictionary(x=>x.ProductId,x=>x.Amount);
    }
}
