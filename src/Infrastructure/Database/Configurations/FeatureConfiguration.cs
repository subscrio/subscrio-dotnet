using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Subscrio.Core.Infrastructure.Database.Configurations;

public class FeatureConfiguration : IEntityTypeConfiguration<FeatureRecord>
{
    public void Configure(EntityTypeBuilder<FeatureRecord> builder)
    {
        builder.ToTable("features");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(f => f.Key)
            .HasColumnName("key")
            .IsRequired();

        builder.HasIndex(f => f.Key)
            .IsUnique();

        builder.Property(f => f.DisplayName)
            .HasColumnName("display_name")
            .IsRequired();

        builder.Property(f => f.Description)
            .HasColumnName("description");

        builder.Property(f => f.ValueType)
            .HasColumnName("value_type")
            .IsRequired();

        builder.Property(f => f.DefaultValue)
            .HasColumnName("default_value")
            .IsRequired();

        builder.Property(f => f.GroupName)
            .HasColumnName("group_name");

        builder.Property(f => f.Status)
            .HasColumnName("status")
            .IsRequired();

        builder.Property(f => f.Validator)
            .HasColumnName("validator")
            .HasColumnType("jsonb");

        builder.Property(f => f.Metadata)
            .HasColumnName("metadata")
            .HasColumnType("jsonb");

        builder.Property(f => f.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()");

        builder.Property(f => f.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()");
    }
}

