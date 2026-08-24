using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Subscrio.Core.Infrastructure.Database.Configurations;

public class PlanFeatureConfiguration : IEntityTypeConfiguration<PlanFeatureRecord>
{
    public void Configure(EntityTypeBuilder<PlanFeatureRecord> builder)
    {
        builder.ToTable("plan_features");

        builder.HasKey(pf => pf.Id);
        builder.Property(pf => pf.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(pf => pf.PlanId)
            .HasColumnName("plan_id")
            .IsRequired();

        builder.Property(pf => pf.FeatureId)
            .HasColumnName("feature_id")
            .IsRequired();

        builder.Property(pf => pf.Value)
            .HasColumnName("value")
            .IsRequired();

        builder.Property(pf => pf.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()");

        builder.Property(pf => pf.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()");

        builder.HasOne<PlanRecord>()
            .WithMany()
            .HasForeignKey(pf => pf.PlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<FeatureRecord>()
            .WithMany()
            .HasForeignKey(pf => pf.FeatureId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(pf => new { pf.PlanId, pf.FeatureId })
            .IsUnique();
    }
}

