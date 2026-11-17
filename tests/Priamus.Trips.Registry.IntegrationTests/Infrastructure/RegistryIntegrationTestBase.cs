using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.Registry.Persistence.DependencyInjection;
using Priamus.Trips.SharedKernel.Security;
using Xunit;

namespace Priamus.Trips.Registry.IntegrationTests.Infrastructure;

/// <summary>
/// Base class for Registry integration tests. Receives the shared <see cref="PostgresFixture"/>
/// and creates an isolated per-test database cloned from the migrated template, giving each
/// test a clean schema with zero migration overhead.
/// </summary>
[Collection("RegistryIntegrationTests")]
public abstract class RegistryIntegrationTestBase : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly string _databaseName = "test_" + Guid.NewGuid().ToString("N")[..16];
    private ServiceProvider _rootProvider = null!;

    /// <summary>Connection string scoped to this test's isolated database.</summary>
    protected string ConnectionString { get; private set; } = null!;

    protected RegistryIntegrationTestBase(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Returns a fresh <see cref="RegistryDbContext"/> from DI, configured against this
    /// test's isolated database.
    /// </summary>
    protected RegistryDbContext CreateDbContext()
    {
        var scope = _rootProvider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<RegistryDbContext>();
    }

    /// <summary>
    /// Creates a new <see cref="IServiceScope"/> backed by this test's isolated database.
    /// Callers must dispose the scope when done.
    /// </summary>
    protected IServiceScope CreateScope() => _rootProvider.CreateScope();

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
        services.AddRegistryPersistence(ConnectionString);
        _rootProvider = services.BuildServiceProvider();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _rootProvider.DisposeAsync();

        await using var adminConn = new NpgsqlConnection(_fixture.AdminConnectionString);
        await adminConn.OpenAsync();

        await using var cmd = adminConn.CreateCommand();
        cmd.CommandText = $"DROP DATABASE \"{_databaseName}\" WITH (FORCE);";
        await cmd.ExecuteNonQueryAsync();
    }
}
