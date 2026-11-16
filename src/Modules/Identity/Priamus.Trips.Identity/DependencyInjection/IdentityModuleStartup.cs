using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Priamus.Trips.Identity.Application.Services;
using Priamus.Trips.Identity.Persistence;

namespace Priamus.Trips.Identity.DependencyInjection;

/// <summary>
/// Startup steps of the Identity module, called by the host right after the application is built.
/// </summary>
public static class IdentityModuleStartup
{
    /// <summary>
    /// Applies the pending migrations of <see cref="IdentityDbContext"/> in its own DI scope.
    /// The host calls it in Development only; other environments migrate through a pre-deploy job.
    /// </summary>
    /// <param name="services">The application's root service provider.</param>
    /// <param name="cancellationToken">Cancels the migration.</param>
    public static async Task MigrateIdentityDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>
    /// Creates the first administrator from <c>Identity:BootstrapAdmin</c> when no user exists, in
    /// its own DI scope. The host calls it in every environment, after the migration step.
    /// </summary>
    /// <param name="services">The application's root service provider.</param>
    /// <param name="cancellationToken">Cancels the bootstrap.</param>
    /// <exception cref="InvalidOperationException">
    /// The users table is empty and the configured user name or password breaks its rule.
    /// </exception>
    public static async Task BootstrapAdministratorAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var bootstrap = scope.ServiceProvider.GetRequiredService<AdminBootstrapService>();
        await bootstrap.RunAsync(cancellationToken);
    }
}
