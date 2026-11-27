using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Priamus.Trips.Operations.Persistence;

namespace Priamus.Trips.Operations.DependencyInjection;

/// <summary>
/// Startup steps of the Operations module, called by the host right after the application is built.
/// </summary>
public static class OperationsModuleStartup
{
    /// <summary>
    /// Applies the pending migrations of <see cref="OperationsDbContext"/> in its own DI scope.
    /// The host calls it in Development only; other environments migrate through a pre-deploy job.
    /// </summary>
    /// <param name="services">The application's root service provider.</param>
    /// <param name="cancellationToken">Cancels the migration.</param>
    public static async Task MigrateOperationsDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
