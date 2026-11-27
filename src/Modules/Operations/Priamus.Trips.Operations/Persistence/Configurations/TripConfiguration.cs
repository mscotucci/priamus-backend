using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Priamus.Trips.Operations.Domain.Trips;
using Priamus.Trips.Operations.Persistence.Converters;

namespace Priamus.Trips.Operations.Persistence.Configurations;

/// <summary>
/// EF Core Fluent configuration for the <see cref="Trip"/> aggregate root.
/// Maps to the <c>operations.trips</c> table. The ids of other modules are plain uuid columns without
/// a foreign key: no key crosses a schema. The pricing is one column per member (it is later summed
/// and filtered), so it is a complex property, not a json column.
/// </summary>
internal sealed class TripConfiguration : IEntityTypeConfiguration<Trip>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Trip> builder)
    {
        builder.ToTable("trips", "operations", t =>
        {
            t.HasCheckConstraint(
                "chk_trips_measure_kind",
                "measure_kind IN ('StartAndEnd', 'Weight', 'Distance', 'StartOnly')");
            t.HasCheckConstraint(
                "chk_trips_rate_source",
                "rate_source IN ('Contract', 'Vehicle', 'Location', 'LocationPair')");
            t.HasCheckConstraint(
                "chk_trips_minimum_kind",
                "minimum_kind IN ('None', 'FixedMinimum', 'TieredOverage')");
            t.HasCheckConstraint(
                "chk_trips_started",
                "(day IS NULL) = (start_time IS NULL)");
            t.HasCheckConstraint(
                "chk_trips_ended",
                "(end_time IS NULL AND ended_at IS NULL AND number IS NULL AND contract_id IS NULL " +
                "AND measure_kind IS NULL AND rate_source IS NULL AND total IS NULL) " +
                "OR (start_time IS NOT NULL AND end_time IS NOT NULL AND ended_at IS NOT NULL " +
                "AND number IS NOT NULL AND contract_id IS NOT NULL AND measure_kind IS NOT NULL " +
                "AND rate_source IS NOT NULL AND total IS NOT NULL)");
            t.HasCheckConstraint(
                "chk_trips_deleted",
                "(deleted_at IS NULL) = (deleted_by IS NULL) AND (deleted_at IS NULL OR end_time IS NULL)");
        });

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .HasConversion(new TripIdConverter())
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        // The trip number: taken from the sequence when the trip is ended, so it is empty before.
        builder.Property(t => t.Number)
            .HasColumnName("number")
            .HasColumnType("bigint")
            .ValueGeneratedNever();

        builder.HasIndex(t => t.Number)
            .IsUnique()
            .HasDatabaseName("uq_trips_number");

        builder.Property(t => t.DriverId)
            .HasColumnName("driver_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(t => t.CustomerId)
            .HasColumnName("customer_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(t => t.ContractId)
            .HasColumnName("contract_id")
            .HasColumnType("uuid");

        builder.Property(t => t.LeadingVehicleId)
            .HasColumnName("leading_vehicle_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(t => t.TrailerId)
            .HasColumnName("trailer_id")
            .HasColumnType("uuid");

        builder.Property(t => t.PickupLocationId)
            .HasColumnName("pickup_location_id")
            .HasColumnType("uuid");

        builder.Property(t => t.DeliveryLocationId)
            .HasColumnName("delivery_location_id")
            .HasColumnType("uuid");

        builder.Property(t => t.Day)
            .HasColumnName("day")
            .HasColumnType("date");

        builder.Property(t => t.Start)
            .HasColumnName("start_time")
            .HasColumnType("time");

        builder.Property(t => t.End)
            .HasColumnName("end_time")
            .HasColumnType("time");

        builder.Property(t => t.EndedAt)
            .HasColumnName("ended_at")
            .HasColumnType("timestamptz");

        builder.Property(t => t.Kilograms)
            .HasColumnName("kilograms")
            .HasColumnType("integer");

        builder.Property(t => t.Kilometres)
            .HasColumnName("kilometres")
            .HasColumnType("integer");

        builder.Property(t => t.MeasureKind)
            .HasConversion<string>()
            .HasColumnName("measure_kind")
            .HasColumnType("text");

        builder.Property(t => t.RequiredDocuments)
            .HasConversion(new RequiredDocumentsConverter(), new RequiredDocumentsComparer())
            .HasColumnName("required_documents")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(t => t.AddedAt)
            .HasColumnName("added_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(t => t.AddedOn)
            .HasColumnName("added_on")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(t => t.AddedBy)
            .HasConversion(new UserIdConverter())
            .HasColumnName("added_by")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(t => t.DeletedAt)
            .HasColumnName("deleted_at")
            .HasColumnType("timestamptz");

        builder.Property(t => t.DeletedBy)
            .HasConversion(new UserIdConverter())
            .HasColumnName("deleted_by")
            .HasColumnType("uuid");

        // A deleted trip stays in the table; no query of the module returns it.
        builder.HasQueryFilter(t => t.DeletedAt == null);

        // Optimistic concurrency: every act that updates a trip (start, end, delete) increments the version.
        // No screen edits a trip, so the client never sends a version: the token is the one the handler loaded,
        // and a handler that loses a race reads the trip again.
        builder.Property(t => t.Version)
            .HasColumnName("version")
            .HasColumnType("integer")
            .IsConcurrencyToken()
            .HasDefaultValue(0);

        builder.ComplexProperty(t => t.Pricing, pricing =>
        {
            pricing.Property(p => p.RateSource)
                .HasConversion<string>()
                .HasColumnName("rate_source")
                .HasColumnType("text");

            pricing.Property(p => p.RateVehicleId)
                .HasColumnName("rate_vehicle_id")
                .HasColumnType("uuid");

            pricing.Property(p => p.RatePickupLocationId)
                .HasColumnName("rate_pickup_location_id")
                .HasColumnType("uuid");

            pricing.Property(p => p.RateDeliveryLocationId)
                .HasColumnName("rate_delivery_location_id")
                .HasColumnType("uuid");

            pricing.Property(p => p.DayPrice)
                .HasColumnName("day_price")
                .HasColumnType("numeric(12,2)");

            pricing.Property(p => p.NightPrice)
                .HasColumnName("night_price")
                .HasColumnType("numeric(12,2)");

            pricing.Property(p => p.NightCallFee)
                .HasColumnName("night_call_fee")
                .HasColumnType("numeric(12,2)");

            pricing.Property(p => p.MinimumKind)
                .HasConversion<string>()
                .HasColumnName("minimum_kind")
                .HasColumnType("text");

            pricing.Property(p => p.MinimumAmount)
                .HasColumnName("minimum_amount")
                .HasColumnType("numeric(12,2)");

            pricing.Property(p => p.MinimumThreshold)
                .HasColumnName("minimum_threshold")
                .HasColumnType("numeric(12,2)");

            pricing.Property(p => p.MinimumOveragePrice)
                .HasColumnName("minimum_overage_price")
                .HasColumnType("numeric(12,2)");

            pricing.Property(p => p.NightBandStart)
                .HasColumnName("night_band_start")
                .HasColumnType("time");

            pricing.Property(p => p.NightBandEnd)
                .HasColumnName("night_band_end")
                .HasColumnType("time");

            pricing.Property(p => p.NightSurcharge)
                .HasColumnName("night_surcharge")
                .HasColumnType("boolean");

            pricing.Property(p => p.IsNight)
                .HasColumnName("is_night")
                .HasColumnType("boolean");

            pricing.Property(p => p.Quantity)
                .HasColumnName("quantity")
                .HasColumnType("integer");

            pricing.Property(p => p.UnitPrice)
                .HasColumnName("unit_price")
                .HasColumnType("numeric(12,2)");

            pricing.Property(p => p.BaseAmount)
                .HasColumnName("base_amount")
                .HasColumnType("numeric(12,2)");

            pricing.Property(p => p.AmountAfterMinimum)
                .HasColumnName("amount_after_minimum")
                .HasColumnType("numeric(12,2)");

            pricing.Property(p => p.MinimumApplied)
                .HasColumnName("minimum_applied")
                .HasColumnType("boolean");

            pricing.Property(p => p.AppliedNightCallFee)
                .HasColumnName("applied_night_call_fee")
                .HasColumnType("numeric(12,2)");

            pricing.Property(p => p.Total)
                .HasColumnName("total")
                .HasColumnType("numeric(12,2)");
        });

        builder.HasIndex(t => t.DriverId)
            .HasDatabaseName("ix_trips_driver_id");

        // One trip in progress per driver, also against two starts that arrive at the same moment.
        builder.HasIndex(t => t.DriverId)
            .IsUnique()
            .HasFilter("start_time IS NOT NULL AND end_time IS NULL AND deleted_at IS NULL")
            .HasDatabaseName("uq_trips_driver_id_in_progress");
    }
}
