using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Subscrio.Core.Infrastructure.Database.Configurations;

public class SystemConfigConfiguration : IEntityTypeConfiguration<SystemConfigRecord>
{
    public void Configure(EntityTypeBuilder<SystemConfigRecord> builder)
    {
        builder.ToTable("system_config");

        builder.HasKey(sc => sc.Id);
        builder.Property(sc => sc.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(sc => sc.ConfigKey)
            .HasColumnName("config_key")
            .IsRequired();

        builder.HasIndex(sc => sc.ConfigKey)
            .IsUnique();

        builder.Property(sc => sc.ConfigValue)
            .HasColumnName("config_value")
            .IsRequired();

        builder.Property(sc => sc.Encrypted)
            .HasColumnName("encrypted")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(sc => sc.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()");

        builder.Property(sc => sc.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()");
    }
}

