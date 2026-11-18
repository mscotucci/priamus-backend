using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Persistence.Converters;

namespace Priamus.Trips.Registry.Persistence.Configurations;

/// <summary>
/// EF Core Fluent configuration for the <see cref="Customer"/> aggregate root.
/// Maps to the <c>registry.customers</c> table.
/// </summary>
internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers", "registry", t =>
        {
            t.HasCheckConstraint(
                "chk_customers_status",
                "status IN ('Active', 'Deactivated')");
        });

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasConversion(new CustomerIdConverter())
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(c => c.CompanyName)
            .HasColumnName("company_name")
            .HasColumnType("varchar(150)")
            .IsRequired();

        // Nullable column: PostgreSQL lets several rows hold NULL under the unique index.
        builder.Property(c => c.VatNumber)
            .HasConversion<VatNumberConverter>()
            .HasColumnName("vat_number")
            .HasColumnType("varchar(11)");

        builder.HasIndex(c => c.VatNumber)
            .IsUnique()
            .HasDatabaseName("uq_customers_vat_number");

        builder.Property(c => c.TaxCode)
            .HasConversion<TaxCodeConverter>()
            .HasColumnName("tax_code")
            .HasColumnType("varchar(16)");

        builder.Property(c => c.Address)
            .HasColumnName("address")
            .HasColumnType("varchar(200)");

        builder.Property(c => c.Status)
            .HasConversion<string>()
            .HasColumnName("status")
            .HasColumnType("text")
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
