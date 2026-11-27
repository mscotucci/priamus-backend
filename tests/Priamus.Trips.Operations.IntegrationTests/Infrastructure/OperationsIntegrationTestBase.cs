using Npgsql;
using Xunit;

namespace Priamus.Trips.Operations.IntegrationTests.Infrastructure;

/// <summary>
/// Base class for Operations integration tests. Receives the shared <see cref="PostgresFixture"/>
/// and creates an isolated per-test database cloned from the migrated template, so the host a test
/// builds with <c>ApiFactory</c> starts on a clean schema.
/// </summary>
[Collection("OperationsIntegrationTests")]
public abstract class OperationsIntegrationTestBase : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly string _databaseName = "test_" + Guid.NewGuid().ToString("N")[..16];

    /// <summary>Connection string scoped to this test's isolated database.</summary>
    protected string ConnectionString { get; private set; } = null!;

    protected OperationsIntegrationTestBase(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        await using var adminConn = new NpgsqlConnection(_fixture.AdminConnectionString);
        await adminConn.OpenAsync();

        await using var cmd = adminConn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{_databaseName}\" TEMPLATE {_fixture.TemplateDatabaseName};";
        await cmd.ExecuteNonQueryAsync();

        // Pooling=false prevents Npgsql from holding connections in the pool, which would make
        // DROP DATABASE fail with "database is being accessed by other users".
        ConnectionString = new NpgsqlConnectionStringBuilder(_fixture.AdminConnectionString)
        {
            Database = _databaseName,
            Pooling = false,
        }.ConnectionString;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await using var adminConn = new NpgsqlConnection(_fixture.AdminConnectionString);
        await adminConn.OpenAsync();

        await using var cmd = adminConn.CreateCommand();
        cmd.CommandText = $"DROP DATABASE \"{_databaseName}\" WITH (FORCE);";
        await cmd.ExecuteNonQueryAsync();
    }
}
