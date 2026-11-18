using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Customers;
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
}
