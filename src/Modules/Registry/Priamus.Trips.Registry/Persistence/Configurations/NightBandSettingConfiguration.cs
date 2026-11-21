using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Priamus.Trips.Registry.Domain.NightBands;
using Priamus.Trips.Registry.Persistence.Converters;

namespace Priamus.Trips.Registry.Persistence.Configurations;

/// <summary>
/// EF Core Fluent configuration for the <see cref="NightBandSetting"/> aggregate root.
/// Maps to the <c>registry.night_band_settings</c> table, which holds a single row seeded by the migration.
/// </summary>
internal sealed class NightBandSettingConfiguration : IEntityTypeConfiguration<NightBandSetting>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<NightBandSetting> builder)
    {
        builder.ToTable("night_band_settings", "registry", t =>
            t.HasCheckConstraint(
                "chk_night_band_settings_single_row",
                "id = '00000000-0000-0000-0000-000000000001'"));

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        // The band is rebuilt from two time columns held in private fields of the aggregate.
        builder.Ignore(s => s.Band);

        builder.Property<TimeOnly>("_start")
            .HasColumnName("start_time")
            .HasColumnType("time")
            .IsRequired();

        builder.Property<TimeOnly>("_end")
            .HasColumnName("end_time")
            .HasColumnType("time")
            .IsRequired();

        builder.Property(s => s.LastModifiedAt)
            .HasColumnName("last_modified_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(s => s.LastModifiedBy)
            .HasConversion(new UserIdConverter())
            .HasColumnName("last_modified_by")
            .HasColumnType("uuid")
            .IsRequired();

        // Optimistic concurrency — explicit int column (not xmin), incremented by the aggregate.
        builder.Property<int>("Version")
            .HasColumnName("version")
            .HasColumnType("integer")
            .IsConcurrencyToken()
            .HasDefaultValue(0);
    }
}
