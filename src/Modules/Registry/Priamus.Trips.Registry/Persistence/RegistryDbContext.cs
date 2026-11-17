using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
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

        base.OnModelCreating(modelBuilder);
    }
}
