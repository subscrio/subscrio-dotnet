using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Subscrio.Core.Infrastructure.Database.Configurations;

public class SubscriptionFeatureOverrideConfiguration : IEntityTypeConfiguration<SubscriptionFeatureOverrideRecord>
{
    public void Configure(EntityTypeBuilder<SubscriptionFeatureOverrideRecord> builder)
    {
        builder.ToTable("subscription_feature_overrides");

        builder.HasKey(sfo => sfo.Id);
        builder.Property(sfo => sfo.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(sfo => sfo.SubscriptionId)
            .HasColumnName("subscription_id")
            .IsRequired();

        builder.Property(sfo => sfo.FeatureId)
            .HasColumnName("feature_id")
            .IsRequired();

        builder.Property(sfo => sfo.Value)
            .HasColumnName("value")
            .IsRequired();

        builder.Property(sfo => sfo.OverrideType)
            .HasColumnName("override_type")
            .IsRequired();

        builder.Property(sfo => sfo.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()");

        builder.HasOne<SubscriptionRecord>()
            .WithMany()
            .HasForeignKey(sfo => sfo.SubscriptionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<FeatureRecord>()
            .WithMany()
            .HasForeignKey(sfo => sfo.FeatureId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(sfo => new { sfo.SubscriptionId, sfo.FeatureId })
            .IsUnique();
    }
}

