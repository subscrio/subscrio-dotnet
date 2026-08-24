using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Subscrio.Core.Infrastructure.Database.Configurations;

public class SubscriptionConfiguration : IEntityTypeConfiguration<SubscriptionRecord>
{
    public void Configure(EntityTypeBuilder<SubscriptionRecord> builder)
    {
        builder.ToTable("subscriptions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(s => s.Key)
            .HasColumnName("key")
            .IsRequired();

        builder.HasIndex(s => s.Key)
            .IsUnique();

        builder.Property(s => s.CustomerId)
            .HasColumnName("customer_id")
            .IsRequired();

        builder.Property(s => s.PlanId)
            .HasColumnName("plan_id")
            .IsRequired();

        builder.Property(s => s.BillingCycleId)
            .HasColumnName("billing_cycle_id")
            .IsRequired();

        builder.Property(s => s.ActivationDate)
            .HasColumnName("activation_date")
            .HasColumnType("timestamp with time zone");

        builder.Property(s => s.ExpirationDate)
            .HasColumnName("expiration_date")
            .HasColumnType("timestamp with time zone");

        builder.Property(s => s.CancellationDate)
            .HasColumnName("cancellation_date")
            .HasColumnType("timestamp with time zone");

        builder.Property(s => s.TrialEndDate)
            .HasColumnName("trial_end_date")
            .HasColumnType("timestamp with time zone");

        builder.Property(s => s.CurrentPeriodStart)
            .HasColumnName("current_period_start")
            .HasColumnType("timestamp with time zone");

        builder.Property(s => s.CurrentPeriodEnd)
            .HasColumnName("current_period_end")
            .HasColumnType("timestamp with time zone");

        builder.Property(s => s.StripeSubscriptionId)
            .HasColumnName("stripe_subscription_id");

        builder.HasIndex(s => s.StripeSubscriptionId)
            .IsUnique()
            .HasFilter("stripe_subscription_id IS NOT NULL");

        builder.Property(s => s.Metadata)
            .HasColumnName("metadata")
            .HasColumnType("jsonb");

        builder.Property(s => s.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()");

        builder.Property(s => s.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()");

        builder.Property(s => s.IsArchived)
            .HasColumnName("is_archived")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(s => s.TransitionedAt)
            .HasColumnName("transitioned_at")
            .HasColumnType("timestamp with time zone");

        builder.HasOne<CustomerRecord>()
            .WithMany()
            .HasForeignKey(s => s.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<PlanRecord>()
            .WithMany()
            .HasForeignKey(s => s.PlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<BillingCycleRecord>()
            .WithMany()
            .HasForeignKey(s => s.BillingCycleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

