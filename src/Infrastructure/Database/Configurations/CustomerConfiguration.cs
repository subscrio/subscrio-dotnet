using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Subscrio.Core.Infrastructure.Database.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<CustomerRecord>
{
    public void Configure(EntityTypeBuilder<CustomerRecord> builder)
    {
        builder.ToTable("customers");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(c => c.Key)
            .HasColumnName("key")
            .IsRequired();

        builder.HasIndex(c => c.Key)
            .IsUnique();

        builder.Property(c => c.DisplayName)
            .HasColumnName("display_name");

        builder.Property(c => c.Email)
            .HasColumnName("email");

        builder.Property(c => c.ExternalBillingId)
            .HasColumnName("external_billing_id");

        builder.HasIndex(c => c.ExternalBillingId)
            .IsUnique()
            .HasFilter("external_billing_id IS NOT NULL");

        builder.Property(c => c.Status)
            .HasColumnName("status")
            .IsRequired();

        builder.Property(c => c.Metadata)
            .HasColumnName("metadata")
            .HasColumnType("jsonb");

        builder.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()");

        builder.Property(c => c.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()");
    }
}

