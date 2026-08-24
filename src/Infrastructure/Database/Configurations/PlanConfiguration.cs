using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Subscrio.Core.Infrastructure.Database.Configurations;

public class PlanConfiguration : IEntityTypeConfiguration<PlanRecord>
{
    public void Configure(EntityTypeBuilder<PlanRecord> builder)
    {
        builder.ToTable("plans");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(p => p.ProductId)
            .HasColumnName("product_id")
            .IsRequired();

        builder.Property(p => p.Key)
            .HasColumnName("key")
            .IsRequired();

        builder.Property(p => p.DisplayName)
            .HasColumnName("display_name")
            .IsRequired();

        builder.Property(p => p.Description)
            .HasColumnName("description");

        builder.Property(p => p.Status)
            .HasColumnName("status")
            .IsRequired();

        builder.Property(p => p.OnExpireTransitionToBillingCycleId)
            .HasColumnName("on_expire_transition_to_billing_cycle_id");

        builder.Property(p => p.Metadata)
            .HasColumnName("metadata")
            .HasColumnType("jsonb");

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()");

        builder.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()");

        builder.HasOne<ProductRecord>()
            .WithMany()
            .HasForeignKey(p => p.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // Foreign key to billing_cycles (optional, nullable)
        builder.HasOne<BillingCycleRecord>()
            .WithMany()
            .HasForeignKey(p => p.OnExpireTransitionToBillingCycleId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasIndex(p => new { p.ProductId, p.Key })
            .IsUnique();
    }
}

