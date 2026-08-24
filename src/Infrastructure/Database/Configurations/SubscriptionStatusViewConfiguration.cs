using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Subscrio.Core.Infrastructure.Database;

namespace Subscrio.Core.Infrastructure.Database.Configurations;

public class SubscriptionStatusViewConfiguration : IEntityTypeConfiguration<SubscriptionStatusViewRecord>
{
    public void Configure(EntityTypeBuilder<SubscriptionStatusViewRecord> builder)
    {
        // Map to the database view
        builder.ToView("subscription_status_view", "subscrio");
        
        // Views don't have keys in EF Core, but we need to mark it as keyless
        builder.HasNoKey();

        // Map all properties to their column names
        builder.Property(v => v.Id)
            .HasColumnName("id");

        builder.Property(v => v.Key)
            .HasColumnName("key");

        builder.Property(v => v.CustomerId)
            .HasColumnName("customer_id");

        builder.Property(v => v.PlanId)
            .HasColumnName("plan_id");

        builder.Property(v => v.BillingCycleId)
            .HasColumnName("billing_cycle_id");

        builder.Property(v => v.ActivationDate)
            .HasColumnName("activation_date")
            .HasColumnType("timestamp with time zone");

        builder.Property(v => v.ExpirationDate)
            .HasColumnName("expiration_date")
            .HasColumnType("timestamp with time zone");

        builder.Property(v => v.CancellationDate)
            .HasColumnName("cancellation_date")
            .HasColumnType("timestamp with time zone");

        builder.Property(v => v.TrialEndDate)
            .HasColumnName("trial_end_date")
            .HasColumnType("timestamp with time zone");

        builder.Property(v => v.CurrentPeriodStart)
            .HasColumnName("current_period_start")
            .HasColumnType("timestamp with time zone");

        builder.Property(v => v.CurrentPeriodEnd)
            .HasColumnName("current_period_end")
            .HasColumnType("timestamp with time zone");

        builder.Property(v => v.StripeSubscriptionId)
            .HasColumnName("stripe_subscription_id");

        builder.Property(v => v.Metadata)
            .HasColumnName("metadata")
            .HasColumnType("jsonb");

        builder.Property(v => v.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(v => v.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(v => v.IsArchived)
            .HasColumnName("is_archived");

        builder.Property(v => v.TransitionedAt)
            .HasColumnName("transitioned_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(v => v.ComputedStatus)
            .HasColumnName("computed_status")
            .IsRequired();
    }
}

