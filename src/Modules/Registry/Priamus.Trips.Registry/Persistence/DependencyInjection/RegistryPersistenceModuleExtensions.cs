using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Priamus.Trips.Registry.Persistence.Interceptors;

namespace Priamus.Trips.Registry.Persistence.DependencyInjection;

/// <summary>
/// Extension methods for registering Registry module persistence services.
/// </summary>
public static class RegistryPersistenceModuleExtensions
{
    /// <summary>
    /// Registers <see cref="RegistryDbContext"/> (scoped) and the <see cref="AuditInterceptor"/>
    /// with the DI container. Per ADR 0009 there are no repository abstractions —
    /// handlers depend on <see cref="RegistryDbContext"/> directly.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="connectionString">The Npgsql connection string of the application database.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddRegistryPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        // The interceptor is scoped because ICurrentUser is scoped (one per HTTP request).
        services.AddScoped<AuditInterceptor>();

        services.AddDbContext<RegistryDbContext>((provider, options) =>
        {
            var interceptor = provider.GetRequiredService<AuditInterceptor>();

            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "registry"));

            options.AddInterceptors(interceptor);
        });

        return services;
    }
}
