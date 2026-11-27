using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Priamus.Trips.Operations.Persistence.DependencyInjection;

/// <summary>
/// Extension methods for registering Operations module persistence services.
/// </summary>
public static class OperationsPersistenceModuleExtensions
{
    /// <summary>
    /// Registers <see cref="OperationsDbContext"/> (scoped) with the DI container. Per ADR 0009 there
    /// are no repository abstractions — handlers depend on <see cref="OperationsDbContext"/> directly.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="connectionString">The Npgsql connection string of the application database.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddOperationsPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<OperationsDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "operations")));

        return services;
    }
}
