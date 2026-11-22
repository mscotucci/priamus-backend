using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.Persistence.Converters;

namespace Priamus.Trips.Registry.Persistence.Configurations;

/// <summary>
/// EF Core Fluent configuration for the <see cref="Location"/> aggregate root.
/// Maps to the <c>registry.locations</c> table.
/// </summary>
internal sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("locations", "registry", t =>
        {
            t.HasCheckConstraint(
                "chk_locations_status",
                "status IN ('Active', 'Deactivated')");
        });

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id)
            .HasConversion(new LocationIdConverter())
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(l => l.Name)
            .HasColumnName("name")
            .HasColumnType("varchar(100)")
            .IsRequired();

        builder.Property(l => l.Municipality)
            .HasColumnName("municipality")
            .HasColumnType("varchar(100)")
            .IsRequired();

        builder.Property(l => l.Address)
            .HasColumnName("address")
            .HasColumnType("varchar(200)");

        // Derived comparison keys (Location.KeyOf): the unique index decides a duplicate, also
        // between two saves that arrive at the same moment.
        builder.Property(l => l.NameKey)
            .HasColumnName("name_key")
            .HasColumnType("varchar(100)")
            .IsRequired();

        builder.Property(l => l.MunicipalityKey)
            .HasColumnName("municipality_key")
            .HasColumnType("varchar(100)")
            .IsRequired();

        builder.HasIndex(l => new { l.NameKey, l.MunicipalityKey })
            .IsUnique()
            .HasDatabaseName("uq_locations_name_municipality_key");

        builder.Property(l => l.Status)
            .HasConversion<string>()
            .HasColumnName("status")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(l => l.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(l => l.CreatedBy)
            .HasConversion(new UserIdConverter())
            .HasColumnName("created_by")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(l => l.LastModifiedAt)
            .HasColumnName("last_modified_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(l => l.LastModifiedBy)
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
