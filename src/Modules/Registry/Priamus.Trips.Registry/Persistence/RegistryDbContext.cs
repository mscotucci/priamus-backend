using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Domain.Customers.ValueObjects;
using Priamus.Trips.Registry.Domain.Drivers;
using Priamus.Trips.Registry.Domain.NightBands;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.Domain.Vehicles.ValueObjects;

namespace Priamus.Trips.Registry.Persistence;

/// <summary>
/// EF Core <see cref="DbContext"/> for the Registry module. Owns the <c>registry</c>
/// PostgreSQL schema and all migrations under <c>Persistence/Migrations/</c>.
/// </summary>
/// <remarks>
/// The <see cref="Interceptors.AuditInterceptor"/> is injected into the DbContext options
/// by the DI registration (see <c>RegistryPersistenceModuleExtensions</c>), so this class
/// does not take <see cref="Priamus.Trips.SharedKernel.Security.ICurrentUser"/> directly.
/// </remarks>
public sealed class RegistryDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of <see cref="RegistryDbContext"/>.
    /// </summary>
    /// <param name="options">Options built by the DI registration, including the <see cref="Interceptors.AuditInterceptor"/>.</param>
    public RegistryDbContext(DbContextOptions<RegistryDbContext> options)
        : base(options)
    {
    }

    /// <summary>The company's vehicles.</summary>
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    /// <summary>The company's customers.</summary>
    public DbSet<Customer> Customers => Set<Customer>();

    /// <summary>The company's drivers.</summary>
    public DbSet<Driver> Drivers => Set<Driver>();

    /// <summary>The customers' contracts.</summary>
    public DbSet<Contract> Contracts => Set<Contract>();

    /// <summary>The system night band: a single row.</summary>
    public DbSet<NightBandSetting> NightBandSettings => Set<NightBandSetting>();

    /// <summary>Audit log entries written synchronously by the <see cref="Interceptors.AuditInterceptor"/>.</summary>
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("registry");
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // LicensePlate.Unwrap() — query-side identity over the varchar column, so Contains on the
        // plate translates to SQL LIKE (the value converter hides the plate's string from LINQ).
        modelBuilder
            .HasDbFunction(typeof(LicensePlateQueryExtensions)
                .GetMethod(nameof(LicensePlateQueryExtensions.Unwrap), [typeof(LicensePlate)])!)
            .HasTranslation(args => new SqlFunctionExpression(
                functionName: string.Empty,
                arguments: args,
                nullable: false,
                argumentsPropagateNullability: new[] { false },
                type: typeof(string),
                typeMapping: null))
            .HasParameter("licensePlate")
                .HasStoreType("varchar(10)");

        // VatNumber.Unwrap() and TaxCode.Unwrap() — the same identity translation as the plate's,
        // over the nullable code columns.
        modelBuilder
            .HasDbFunction(typeof(VatNumberQueryExtensions)
                .GetMethod(nameof(VatNumberQueryExtensions.Unwrap), [typeof(VatNumber)])!)
            .HasTranslation(args => new SqlFunctionExpression(
                functionName: string.Empty,
                arguments: args,
                nullable: true,
                argumentsPropagateNullability: new[] { true },
                type: typeof(string),
                typeMapping: null))
            .HasParameter("vatNumber")
                .HasStoreType("varchar(11)");

        modelBuilder
            .HasDbFunction(typeof(TaxCodeQueryExtensions)
                .GetMethod(nameof(TaxCodeQueryExtensions.Unwrap), [typeof(TaxCode)])!)
            .HasTranslation(args => new SqlFunctionExpression(
                functionName: string.Empty,
                arguments: args,
                nullable: true,
                argumentsPropagateNullability: new[] { true },
                type: typeof(string),
                typeMapping: null))
            .HasParameter("taxCode")
                .HasStoreType("varchar(16)");

        base.OnModelCreating(modelBuilder);
    }
}
