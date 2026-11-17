using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.Persistence.Converters;

namespace Priamus.Trips.Registry.Persistence.Configurations;

/// <summary>
/// EF Core Fluent configuration for the <see cref="Vehicle"/> aggregate root.
/// Maps to the <c>registry.vehicles</c> table.
/// </summary>
internal sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.ToTable("vehicles", "registry", t =>
        {
            t.HasCheckConstraint(
                "chk_vehicles_type",
                "type IN ('Tractor', 'Trailer', 'StandaloneVehicle')");

            t.HasCheckConstraint(
                "chk_vehicles_status",
                "status IN ('Active', 'Decommissioned')");

            t.HasCheckConstraint(
                "chk_vehicles_requires_trailer_tractor",
                "requires_trailer = false OR type = 'Tractor'");
        });

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id)
            .HasConversion(new VehicleIdConverter())
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(v => v.LicensePlate)
            .HasConversion(new LicensePlateConverter())
            .HasColumnName("license_plate")
            .HasColumnType("varchar(10)")
            .IsRequired();

        builder.HasIndex(v => v.LicensePlate)
            .IsUnique()
            .HasDatabaseName("uq_vehicles_license_plate");

        builder.Property(v => v.Description)
            .HasColumnName("description")
            .HasColumnType("varchar(100)")
            .IsRequired();

        builder.Property(v => v.Type)
            .HasConversion<string>()
            .HasColumnName("type")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(v => v.RequiresTrailer)
            .HasColumnName("requires_trailer")
            .HasColumnType("boolean")
            .IsRequired();

        builder.Property(v => v.Status)
            .HasConversion<string>()
            .HasColumnName("status")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(v => v.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(v => v.CreatedBy)
            .HasConversion(new UserIdConverter())
            .HasColumnName("created_by")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(v => v.LastModifiedAt)
            .HasColumnName("last_modified_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(v => v.LastModifiedBy)
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
