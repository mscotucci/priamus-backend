using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.IntegrationTesting;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Identity.IntegrationTests;

/// <summary>
/// Proves the <c>Identity_EnumsByName</c> migration rewrites existing rows: the database is migrated to
/// the initial migration, rows are written with the old integer values, then migrated to the latest.
/// </summary>
[Collection("IdentityIntegrationTests")]
public sealed class UserEnumsMigrationTests
{
    private const string InitialMigration = "20260512160054_Identity_Initial";

    private readonly PostgresFixture _fixture;

    public UserEnumsMigrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Migration_RewritesIntegerRolesAndStatusesToMemberNames()
    {
        // Every existing user keeps its role and status through the migration.
        var ct = TestContext.Current.CancellationToken;
        var databaseName = "test_" + Guid.NewGuid().ToString("N")[..16];

        await using (var adminConn = new NpgsqlConnection(_fixture.AdminConnectionString))
        {
            await adminConn.OpenAsync(ct);
            await using var create = adminConn.CreateCommand();
            create.CommandText = $"CREATE DATABASE \"{databaseName}\";";
            await create.ExecuteNonQueryAsync(ct);
        }

        try
        {
            var connectionString = new NpgsqlConnectionStringBuilder(_fixture.AdminConnectionString)
            {
                Database = databaseName,
                Pooling = false,
            }.ConnectionString;

            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable("__ef_migrations_history", "identity"))
                .Options;

            await using var db = new IdentityDbContext(options);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(InitialMigration, ct);

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(ct);

            (int Role, int Status)[] rows = [(1, 1), (2, 1), (3, 1), (1, 2), (2, 2), (3, 2)];
            for (var i = 0; i < rows.Length; i++)
            {
                await using var insert = conn.CreateCommand();
                insert.CommandText =
                    "INSERT INTO identity.users (id, username, password_hash, role, status, created_at, " +
                    "created_by, last_modified_at, last_modified_by) VALUES " +
                    $"(gen_random_uuid(), 'user{i}', 'x', {rows[i].Role}, {rows[i].Status}, now(), " +
                    "gen_random_uuid(), now(), gen_random_uuid())";
                await insert.ExecuteNonQueryAsync(ct);
            }

            await migrator.MigrateAsync(null, ct);

            await using var select = conn.CreateCommand();
            select.CommandText = "SELECT username, role, status FROM identity.users ORDER BY username";
            await using var reader = await select.ExecuteReaderAsync(ct);
            var stored = new List<string>();
            while (await reader.ReadAsync(ct))
            {
                stored.Add($"{reader.GetString(0)}:{reader.GetString(1)}:{reader.GetString(2)}");
            }

            stored.ShouldBe(
            [
                "user0:Driver:Active",
                "user1:BackOffice:Active",
                "user2:Admin:Active",
                "user3:Driver:Disabled",
                "user4:BackOffice:Disabled",
                "user5:Admin:Disabled",
            ]);
        }
        finally
        {
            await using var adminConn = new NpgsqlConnection(_fixture.AdminConnectionString);
            await adminConn.OpenAsync(ct);
            await using var drop = adminConn.CreateCommand();
            drop.CommandText = $"DROP DATABASE \"{databaseName}\" WITH (FORCE);";
            await drop.ExecuteNonQueryAsync(ct);
        }
    }
}
