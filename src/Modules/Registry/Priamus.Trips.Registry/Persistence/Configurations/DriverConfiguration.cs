using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Priamus.Trips.Registry.Domain.Drivers;
using Priamus.Trips.Registry.Persistence.Converters;

namespace Priamus.Trips.Registry.Persistence.Configurations;

/// <summary>
/// EF Core Fluent configuration for the <see cref="Driver"/> aggregate root.
/// Maps to the <c>registry.drivers</c> table.
/// </summary>
internal sealed class DriverConfiguration : IEntityTypeConfiguration<Driver>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Driver> builder)
    {
        builder.ToTable("drivers", "registry", t =>
        {
            t.HasCheckConstraint(
                "chk_drivers_status",
                "status IN ('Active', 'Deactivated')");
        });

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id)
            .HasConversion(new DriverIdConverter())
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(d => d.FirstName)
            .HasColumnName("first_name")
            .HasColumnType("varchar(60)")
            .IsRequired();

        builder.Property(d => d.LastName)
            .HasColumnName("last_name")
            .HasColumnType("varchar(60)")
            .IsRequired();

        builder.Property(d => d.Phone)
            .HasColumnName("phone")
            .HasColumnType("varchar(20)");

        // Bare id of an Identity user: no cross-schema foreign key. Nullable column: PostgreSQL
        // lets several rows hold NULL under the unique index.
        builder.Property(d => d.UserId)
            .HasConversion(new UserIdConverter())
            .HasColumnName("user_id")
            .HasColumnType("uuid");

        builder.HasIndex(d => d.UserId)
            .IsUnique()
            .HasDatabaseName("uq_drivers_user_id");

        builder.Property(d => d.Status)
            .HasConversion<string>()
            .HasColumnName("status")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(d => d.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(d => d.CreatedBy)
            .HasConversion(new UserIdConverter())
            .HasColumnName("created_by")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(d => d.LastModifiedAt)
            .HasColumnName("last_modified_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(d => d.LastModifiedBy)
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
