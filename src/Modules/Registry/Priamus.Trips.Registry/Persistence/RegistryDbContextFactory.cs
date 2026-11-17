using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Priamus.Trips.Registry.Persistence;

/// <summary>
/// Design-time factory used exclusively by the EF Core CLI (<c>dotnet ef migrations add</c>
/// / <c>dotnet ef database update</c>). Not used at runtime.
/// </summary>
/// <remarks>
/// The connection string is a dummy; it is only required to satisfy EF's provider setup
/// during migration scaffolding — no actual database connection is made.
/// </remarks>
public sealed class RegistryDbContextFactory : IDesignTimeDbContextFactory<RegistryDbContext>
{
    /// <inheritdoc />
    public RegistryDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<RegistryDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=priamus_trips_design;Username=designtime;Password=designtime",
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "registry"))
            .Options;

        return new RegistryDbContext(options);
    }
}
