using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.Identity.Persistence.DependencyInjection;
using Priamus.Trips.SharedKernel.Security;
using Testcontainers.PostgreSql;
using Xunit;

namespace Priamus.Trips.Identity.IntegrationTests.Infrastructure;

/// <summary>
/// Base class for Identity integration tests. Spins up a Testcontainers PostgreSQL 16
/// instance, applies migrations, and provides a factory for creating fresh
/// <see cref="IServiceScope"/> instances so each test works in an isolated EF Core scope.
/// </summary>
public abstract class IdentityIntegrationTestBase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private ServiceProvider _rootProvider = null!;

    /// <summary>
    /// Creates a new <see cref="IServiceScope"/> backed by the running Postgres container.
    /// Callers must dispose the scope when done.
    /// </summary>
    protected IServiceScope CreateScope() => _rootProvider.CreateScope();

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        var services = new ServiceCollection();

        // Register the test stub so the AuditInterceptor gets a known actor id.
        services.AddScoped<ICurrentUser, FakeCurrentUser>();

        services.AddIdentityPersistence(_postgres.GetConnectionString());

        _rootProvider = services.BuildServiceProvider();

        // Apply migrations (never EnsureCreated).
        await using var scope = _rootProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _rootProvider.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
