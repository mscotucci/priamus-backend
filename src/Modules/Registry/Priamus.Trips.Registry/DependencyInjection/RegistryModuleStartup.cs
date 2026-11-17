using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Priamus.Trips.Registry.Persistence;

namespace Priamus.Trips.Registry.DependencyInjection;

/// <summary>
/// Startup steps of the Registry module, called by the host right after the application is built.
/// </summary>
public static class RegistryModuleStartup
{
    /// <summary>
    /// Applies the pending migrations of <see cref="RegistryDbContext"/> in its own DI scope.
    /// The host calls it in Development only; other environments migrate through a pre-deploy job.
    /// </summary>
    /// <param name="services">The application's root service provider.</param>
    /// <param name="cancellationToken">Cancels the migration.</param>
    public static async Task MigrateRegistryDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RegistryDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
