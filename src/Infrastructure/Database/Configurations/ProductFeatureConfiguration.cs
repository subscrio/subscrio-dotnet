using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Subscrio.Core.Infrastructure.Database.Configurations;

public class ProductFeatureConfiguration : IEntityTypeConfiguration<ProductFeatureRecord>
{
    public void Configure(EntityTypeBuilder<ProductFeatureRecord> builder)
    {
        builder.ToTable("product_features");

        builder.HasKey(pf => pf.Id);
        builder.Property(pf => pf.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(pf => pf.ProductId)
            .HasColumnName("product_id")
            .IsRequired();

        builder.Property(pf => pf.FeatureId)
            .HasColumnName("feature_id")
            .IsRequired();

        builder.Property(pf => pf.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()");

        builder.HasOne<ProductRecord>()
            .WithMany()
            .HasForeignKey(pf => pf.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<FeatureRecord>()
            .WithMany()
            .HasForeignKey(pf => pf.FeatureId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(pf => new { pf.ProductId, pf.FeatureId })
            .IsUnique();
    }
}

