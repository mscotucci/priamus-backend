using Microsoft.EntityFrameworkCore;
using Npgsql;
using Priamus.Trips.Identity.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Priamus.Trips.IntegrationTesting;

/// <summary>
/// Assembly-scoped fixture that owns a single PostgreSQL container for the full test run.
/// At initialization it applies migrations once to a template database; each test then
/// creates its own database from that template (cheap Postgres file-system clone).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    /// <summary>Connection string to the admin database (postgres default db).</summary>
    public string AdminConnectionString { get; private set; } = null!;

    /// <summary>Name of the migrated template database.</summary>
    public string TemplateDatabaseName => "identity_template";

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        AdminConnectionString = _container.GetConnectionString();

        await using var adminConn = new NpgsqlConnection(AdminConnectionString);
        await adminConn.OpenAsync();

        await using (var cmd = adminConn.CreateCommand())
        {
            cmd.CommandText = $"CREATE DATABASE {TemplateDatabaseName};";
            await cmd.ExecuteNonQueryAsync();
        }

        // Pooling=false ensures no Npgsql pool connections remain open on identity_template
        // after migration completes. Without this, concurrent CREATE DATABASE ... TEMPLATE
        // calls from test InitializeAsync will fail with "source database is being accessed
        // by other users".
        // 
        // Test-only setting: production code MUST use default pooling. Disabling the pool
        // under real load would saturate Postgres with direct TCP connections per request..
        var templateConnStr = new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Database = TemplateDatabaseName,
            Pooling = false,
        }.ConnectionString;

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(templateConnStr, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "identity"))
            .Options;

        await using var templateCtx = new IdentityDbContext(options);
        await templateCtx.Database.MigrateAsync();

        // Belt-and-suspenders: clear any remaining pooled connections to guarantee that
        // the template database has zero open backends before tests begin cloning it.
        NpgsqlConnection.ClearAllPools();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
