using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.Persistence.Converters;

namespace Priamus.Trips.Registry.Persistence.Configurations;

/// <summary>
/// EF Core Fluent configuration for the <see cref="Contract"/> aggregate root.
/// Maps to the <c>registry.contracts</c> table. The rule that a customer's validities never overlap
/// is the exclusion constraint <c>ex_contracts_customer_validity</c>, added by the migration in raw
/// SQL because EF Core cannot model it.
/// </summary>
internal sealed class ContractConfiguration : IEntityTypeConfiguration<Contract>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Contract> builder)
    {
        builder.ToTable("contracts", "registry");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasConversion(new ContractIdConverter())
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(c => c.CustomerId)
            .HasConversion(new CustomerIdConverter())
            .HasColumnName("customer_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(c => c.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => c.CustomerId)
            .HasDatabaseName("ix_contracts_customer_id");

        builder.ComplexProperty(c => c.Validity, validity =>
        {
            validity.Property(v => v.ValidFrom)
                .HasColumnName("valid_from")
                .HasColumnType("date");

            validity.Property(v => v.ValidTo)
                .HasColumnName("valid_to")
                .HasColumnType("date");

            validity.Ignore(v => v.IsOpen);
        });

        builder.Property(c => c.BillingLevel)
            .HasConversion<string>()
            .HasColumnName("billing_level")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(c => c.PricingMode)
            .HasConversion<string>()
            .HasColumnName("pricing_mode")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(c => c.RequiredDocuments)
            .HasConversion(new RequiredDocumentsConverter(), new RequiredDocumentsComparer())
            .HasColumnName("required_documents")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(c => c.NightSurcharge)
            .HasColumnName("night_surcharge")
            .HasColumnType("boolean")
            .IsRequired();

        // The rate is rebuilt from seven nullable columns held in private fields of the aggregate.
        builder.Ignore(c => c.Rate);

        // The vehicle rates are rebuilt from the rows of their own table, held in a private field.
        builder.Ignore(c => c.VehicleRates);

        // The location rates are rebuilt from the rows of their own table, held in a private field.
        builder.Ignore(c => c.LocationRates);

        builder.Property<decimal?>("_dayPrice")
            .HasColumnName("day_price")
            .HasColumnType("numeric(12,2)");

        builder.Property<decimal?>("_nightPrice")
            .HasColumnName("night_price")
            .HasColumnType("numeric(12,2)");

        builder.Property<decimal?>("_nightCallFee")
            .HasColumnName("night_call_fee")
            .HasColumnType("numeric(12,2)");

        builder.Property<MinimumRuleKind?>("_minimumRule")
            .HasConversion<string>()
            .HasColumnName("minimum_rule")
            .HasColumnType("text");

        builder.Property<decimal?>("_minimumAmount")
            .HasColumnName("minimum_amount")
            .HasColumnType("numeric(12,2)");

        builder.Property<decimal?>("_minimumThreshold")
            .HasColumnName("minimum_threshold")
            .HasColumnType("numeric(12,2)");

        builder.Property<decimal?>("_minimumOveragePrice")
            .HasColumnName("minimum_overage_price")
            .HasColumnType("numeric(12,2)");

        builder.OwnsMany<StoredVehicleRate>("_storedVehicleRates", rates =>
        {
            rates.ToTable("contract_vehicle_rates", "registry");

            rates.WithOwner().HasForeignKey("ContractId");

            rates.Property<ContractId>("ContractId")
                .HasConversion(new ContractIdConverter())
                .HasColumnName("contract_id")
                .HasColumnType("uuid");

            rates.Property<int>("Id")
                .HasColumnName("id")
                .HasColumnType("integer")
                .ValueGeneratedOnAdd();

            rates.HasKey("ContractId", "Id");

            rates.Property(r => r.VehicleId)
                .HasConversion(new VehicleIdConverter())
                .HasColumnName("vehicle_id")
                .HasColumnType("uuid");

            rates.Property(r => r.PickupLocationId)
                .HasConversion(new LocationIdConverter())
                .HasColumnName("pickup_location_id")
                .HasColumnType("uuid");

            rates.Property(r => r.DeliveryLocationId)
                .HasConversion(new LocationIdConverter())
                .HasColumnName("delivery_location_id")
                .HasColumnType("uuid");

            rates.HasOne<Vehicle>()
                .WithMany()
                .HasForeignKey(r => r.VehicleId)
                .OnDelete(DeleteBehavior.Restrict);

            rates.HasOne<Location>()
                .WithMany()
                .HasForeignKey(r => r.PickupLocationId)
                .OnDelete(DeleteBehavior.Restrict);

            rates.HasOne<Location>()
                .WithMany()
                .HasForeignKey(r => r.DeliveryLocationId)
                .OnDelete(DeleteBehavior.Restrict);

            rates.HasIndex(r => r.VehicleId)
                .HasDatabaseName("ix_contract_vehicle_rates_vehicle_id");

            rates.HasIndex(r => r.PickupLocationId)
                .HasDatabaseName("ix_contract_vehicle_rates_pickup_location_id");

            rates.HasIndex(r => r.DeliveryLocationId)
                .HasDatabaseName("ix_contract_vehicle_rates_delivery_location_id");

            // A vehicle has one rate per location or ordered pair; a null side counts as a value.
            rates.HasIndex("ContractId", nameof(StoredVehicleRate.VehicleId), nameof(StoredVehicleRate.PickupLocationId), nameof(StoredVehicleRate.DeliveryLocationId))
                .IsUnique()
                .AreNullsDistinct(false)
                .HasDatabaseName("uq_contract_vehicle_rates_vehicle_locations");

            MapRateColumns(rates);
        });

        builder.OwnsMany<StoredLocationRate>("_storedLocationRates", rates =>
        {
            rates.ToTable("contract_location_rates", "registry");

            rates.WithOwner().HasForeignKey("ContractId");

            rates.Property<ContractId>("ContractId")
                .HasConversion(new ContractIdConverter())
                .HasColumnName("contract_id")
                .HasColumnType("uuid");

            rates.Property<int>("Id")
                .HasColumnName("id")
                .HasColumnType("integer")
                .ValueGeneratedOnAdd();

            rates.HasKey("ContractId", "Id");

            rates.Property(r => r.PickupLocationId)
                .HasConversion(new LocationIdConverter())
                .HasColumnName("pickup_location_id")
                .HasColumnType("uuid");

            rates.Property(r => r.DeliveryLocationId)
                .HasConversion(new LocationIdConverter())
                .HasColumnName("delivery_location_id")
                .HasColumnType("uuid");

            rates.HasOne<Location>()
                .WithMany()
                .HasForeignKey(r => r.PickupLocationId)
                .OnDelete(DeleteBehavior.Restrict);

            rates.HasOne<Location>()
                .WithMany()
                .HasForeignKey(r => r.DeliveryLocationId)
                .OnDelete(DeleteBehavior.Restrict);

            rates.HasIndex(r => r.PickupLocationId)
                .HasDatabaseName("ix_contract_location_rates_pickup_location_id");

            rates.HasIndex(r => r.DeliveryLocationId)
                .HasDatabaseName("ix_contract_location_rates_delivery_location_id");

            // A location or ordered pair has one rate per contract; a null side counts as a value.
            rates.HasIndex("ContractId", nameof(StoredLocationRate.PickupLocationId), nameof(StoredLocationRate.DeliveryLocationId))
                .IsUnique()
                .AreNullsDistinct(false)
                .HasDatabaseName("uq_contract_location_rates_locations");

            MapRateColumns(rates);
        });

        builder.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(c => c.CreatedBy)
            .HasConversion(new UserIdConverter())
            .HasColumnName("created_by")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(c => c.LastModifiedAt)
            .HasColumnName("last_modified_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(c => c.LastModifiedBy)
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

    /// <summary>Maps the seven columns of the rate held by a row owned by the contract.</summary>
    private static void MapRateColumns<TRow>(OwnedNavigationBuilder<Contract, TRow> rates)
        where TRow : StoredRate
    {
        rates.Property(r => r.DayPrice)
            .HasColumnName("day_price")
            .HasColumnType("numeric(12,2)");

        rates.Property(r => r.NightPrice)
            .HasColumnName("night_price")
            .HasColumnType("numeric(12,2)");

        rates.Property(r => r.NightCallFee)
            .HasColumnName("night_call_fee")
            .HasColumnType("numeric(12,2)");

        rates.Property(r => r.MinimumRule)
            .HasConversion<string>()
            .HasColumnName("minimum_rule")
            .HasColumnType("text");

        rates.Property(r => r.MinimumAmount)
            .HasColumnName("minimum_amount")
            .HasColumnType("numeric(12,2)");

        rates.Property(r => r.MinimumThreshold)
            .HasColumnName("minimum_threshold")
            .HasColumnType("numeric(12,2)");

        rates.Property(r => r.MinimumOveragePrice)
            .HasColumnName("minimum_overage_price")
            .HasColumnType("numeric(12,2)");
    }
}
