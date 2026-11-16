using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Priamus.Trips.Identity.Persistence.Interceptors;

namespace Priamus.Trips.Identity.Persistence.DependencyInjection;

/// <summary>
/// Extension methods for registering Identity module persistence services.
/// </summary>
public static class IdentityPersistenceModuleExtensions
{
    /// <summary>
    /// Registers <see cref="IdentityDbContext"/> (scoped) and the <see cref="AuditInterceptor"/>
    /// with the DI container. Per ADR 0009 there are no repository abstractions —
    /// handlers depend on <see cref="IdentityDbContext"/> directly.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="connectionString">The Npgsql connection string of the application database.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddIdentityPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        // The interceptor is scoped because ICurrentUser is scoped (one per HTTP request).
        services.AddScoped<AuditInterceptor>();

        services.AddDbContext<IdentityDbContext>((provider, options) =>
        {
            var interceptor = provider.GetRequiredService<AuditInterceptor>();

            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "identity"));

            options.AddInterceptors(interceptor);
        });

        return services;
    }
}
