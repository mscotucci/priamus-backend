using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.Identity.Persistence.DependencyInjection;
using Priamus.Trips.SharedKernel.Security;
using Xunit;

namespace Priamus.Trips.Identity.IntegrationTests.Infrastructure;

/// <summary>
/// Base class for Identity integration tests. Receives the shared <see cref="PostgresFixture"/>
/// and creates an isolated per-test database cloned from the migrated template, giving each
/// test a clean schema with zero migration overhead.
/// </summary>
[Collection("IdentityIntegrationTests")]
public abstract class IdentityIntegrationTestBase : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly string _databaseName = "test_" + Guid.NewGuid().ToString("N")[..16];
    private ServiceProvider _rootProvider = null!;

    /// <summary>Connection string scoped to this test's isolated database.</summary>
    protected string ConnectionString { get; private set; } = null!;

    protected IdentityIntegrationTestBase(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Returns a fresh <see cref="IdentityDbContext"/> from DI, configured against this
    /// test's isolated database. Callers are responsible for disposing the scope.
    /// </summary>
    protected IdentityDbContext CreateDbContext()
    {
        var scope = _rootProvider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    }

    /// <summary>
    /// Creates a new <see cref="IServiceScope"/> backed by this test's isolated database.
    /// Callers must dispose the scope when done.
    /// </summary>
    protected IServiceScope CreateScope() => _rootProvider.CreateScope();

    /// <summary>
    /// Creates a new async-disposable scope backed by this test's isolated database.
    /// Use with <c>await using var scope = CreateAsyncScope();</c>.
    /// </summary>
    protected AsyncServiceScope CreateAsyncScope() => _rootProvider.CreateAsyncScope();

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        await using var adminConn = new NpgsqlConnection(_fixture.AdminConnectionString);
        await adminConn.OpenAsync();

        await using (var cmd = adminConn.CreateCommand())
        {
            cmd.CommandText =
                $"CREATE DATABASE \"{_databaseName}\" TEMPLATE {_fixture.TemplateDatabaseName};";
            await cmd.ExecuteNonQueryAsync();
        }

        // Pooling=false prevents Npgsql from holding connections in the pool across scopes,
        // which would cause DROP DATABASE to fail with "database is being accessed by other users".
        ConnectionString = new NpgsqlConnectionStringBuilder(_fixture.AdminConnectionString)
        {
            Database = _databaseName,
            Pooling = false,
        }.ConnectionString;

        var services = new ServiceCollection();
        services.AddScoped<ICurrentUser, FakeCurrentUser>();
        services.AddIdentityPersistence(ConnectionString);
        _rootProvider = services.BuildServiceProvider();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _rootProvider.DisposeAsync();

        await using var adminConn = new NpgsqlConnection(_fixture.AdminConnectionString);
        await adminConn.OpenAsync();

        await using var cmd = adminConn.CreateCommand();
        // WITH (FORCE) terminates any remaining backend connections before dropping (Postgres 13+).
        // The Postgres 16 image used by PostgresFixture supports this option.
        cmd.CommandText = $"DROP DATABASE \"{_databaseName}\" WITH (FORCE);";
        await cmd.ExecuteNonQueryAsync();
    }
}
