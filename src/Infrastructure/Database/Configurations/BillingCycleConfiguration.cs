using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Subscrio.Core.Infrastructure.Database.Configurations;

public class BillingCycleConfiguration : IEntityTypeConfiguration<BillingCycleRecord>
{
    public void Configure(EntityTypeBuilder<BillingCycleRecord> builder)
    {
        builder.ToTable("billing_cycles");

        builder.HasKey(bc => bc.Id);
        builder.Property(bc => bc.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(bc => bc.PlanId)
            .HasColumnName("plan_id")
            .IsRequired();

        builder.Property(bc => bc.Key)
            .HasColumnName("key")
            .IsRequired();

        builder.Property(bc => bc.DisplayName)
            .HasColumnName("display_name")
            .IsRequired();

        builder.Property(bc => bc.Description)
            .HasColumnName("description");

        builder.Property(bc => bc.Status)
            .HasColumnName("status")
            .IsRequired()
            .HasDefaultValue("active");

        builder.Property(bc => bc.DurationValue)
            .HasColumnName("duration_value");

        builder.Property(bc => bc.DurationUnit)
            .HasColumnName("duration_unit")
            .IsRequired();

        builder.Property(bc => bc.ExternalProductId)
            .HasColumnName("external_product_id");

        builder.Property(bc => bc.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()");

        builder.Property(bc => bc.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()");

        builder.HasOne<PlanRecord>()
            .WithMany()
            .HasForeignKey(bc => bc.PlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(bc => new { bc.PlanId, bc.Key })
            .IsUnique();
    }
}

