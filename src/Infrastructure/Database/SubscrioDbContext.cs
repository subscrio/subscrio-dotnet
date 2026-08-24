using Microsoft.EntityFrameworkCore;
using Subscrio.Core.Domain.ValueObjects;

namespace Subscrio.Core.Infrastructure.Database;

public class SubscrioDbContext : DbContext
{
    public SubscrioDbContext(DbContextOptions<SubscrioDbContext> options) : base(options)
    {
    }

    public DbSet<ProductRecord> Products { get; set; } = null!;
    public DbSet<FeatureRecord> Features { get; set; } = null!;
    public DbSet<ProductFeatureRecord> ProductFeatures { get; set; } = null!;
    public DbSet<PlanRecord> Plans { get; set; } = null!;
    public DbSet<PlanFeatureRecord> PlanFeatures { get; set; } = null!;
    public DbSet<CustomerRecord> Customers { get; set; } = null!;
    public DbSet<SubscriptionRecord> Subscriptions { get; set; } = null!;
    public DbSet<SubscriptionFeatureOverrideRecord> SubscriptionFeatureOverrides { get; set; } = null!;
    public DbSet<SubscriptionStatusViewRecord> SubscriptionStatusView { get; set; } = null!;
    public DbSet<BillingCycleRecord> BillingCycles { get; set; } = null!;
    public DbSet<SystemConfigRecord> SystemConfig { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all entity configurations
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SubscrioDbContext).Assembly);

        // Configure schema
        modelBuilder.HasDefaultSchema("subscrio");
    }
}

// Database record classes (snake_case properties)
public class ProductRecord
{
    public long Id { get; set; }
    public string Key { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Description { get; set; }
    public string Status { get; set; } = null!;
    public Dictionary<string, object?>? Metadata { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class FeatureRecord
{
    public long Id { get; set; }
    public string Key { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Description { get; set; }
    public string ValueType { get; set; } = null!;
    public string DefaultValue { get; set; } = null!;
    public string? GroupName { get; set; }
    public string Status { get; set; } = null!;
    public Dictionary<string, object?>? Validator { get; set; }
    public Dictionary<string, object?>? Metadata { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ProductFeatureRecord
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public long FeatureId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class PlanRecord
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public string Key { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Description { get; set; }
    public string Status { get; set; } = null!;
    public long? OnExpireTransitionToBillingCycleId { get; set; }
    public Dictionary<string, object?>? Metadata { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class PlanFeatureRecord
{
    public long Id { get; set; }
    public long PlanId { get; set; }
    public long FeatureId { get; set; }
    public string Value { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CustomerRecord
{
    public long Id { get; set; }
    public string Key { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? ExternalBillingId { get; set; }
    public string Status { get; set; } = null!;
    public Dictionary<string, object?>? Metadata { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SubscriptionRecord
{
    public long Id { get; set; }
    public string Key { get; set; } = null!;
    public long CustomerId { get; set; }
    public long PlanId { get; set; }
    public long BillingCycleId { get; set; }
    public DateTime? ActivationDate { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public DateTime? CancellationDate { get; set; }
    public DateTime? TrialEndDate { get; set; }
    public DateTime? CurrentPeriodStart { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public Dictionary<string, object?>? Metadata { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsArchived { get; set; }
    public DateTime? TransitionedAt { get; set; }
}

public class SubscriptionStatusViewRecord
{
    public long Id { get; set; }
    public string Key { get; set; } = null!;
    public long CustomerId { get; set; }
    public long PlanId { get; set; }
    public long BillingCycleId { get; set; }
    public DateTime? ActivationDate { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public DateTime? CancellationDate { get; set; }
    public DateTime? TrialEndDate { get; set; }
    public DateTime? CurrentPeriodStart { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public Dictionary<string, object?>? Metadata { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsArchived { get; set; }
    public DateTime? TransitionedAt { get; set; }
    public string ComputedStatus { get; set; } = null!;
}

public class SubscriptionFeatureOverrideRecord
{
    public long Id { get; set; }
    public long SubscriptionId { get; set; }
    public long FeatureId { get; set; }
    public string Value { get; set; } = null!;
    public string OverrideType { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}

public class BillingCycleRecord
{
    public long Id { get; set; }
    public long PlanId { get; set; }
    public string Key { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Description { get; set; }
    public string Status { get; set; } = null!;
    public int? DurationValue { get; set; }
    public string DurationUnit { get; set; } = null!;
    public string? ExternalProductId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SystemConfigRecord
{
    public long Id { get; set; }
    public string ConfigKey { get; set; } = null!;
    public string ConfigValue { get; set; } = null!;
    public bool Encrypted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

