using System.Net;
using Microsoft.AspNetCore.Hosting;
using Npgsql;
using Priamus.Trips.IntegrationTesting;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Operations.IntegrationTests.Host;

/// <summary>
/// Starts the real host against an empty database (not the migrated template) to prove that the
/// startup migration creates the Operations schema.
/// </summary>
[Collection("OperationsIntegrationTests")]
public sealed class StartupMigrationTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly string _databaseName = "startup_" + Guid.NewGuid().ToString("N")[..16];
    private string _connectionString = null!;

    public StartupMigrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        await ExecuteAdminAsync($"CREATE DATABASE \"{_databaseName}\";");

        _connectionString = new NpgsqlConnectionStringBuilder(_fixture.AdminConnectionString)
        {
            Database = _databaseName,
            Pooling = false,
        }.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        await ExecuteAdminAsync($"DROP DATABASE \"{_databaseName}\" WITH (FORCE);");
    }

    [Fact]
    public async Task Startup_InDevelopmentOnAnEmptyDatabase_CreatesTheOperationsSchemaWithItsTrips()
    {
        // Arrange
        await using var factory = ApiFactory.Create(_connectionString)
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ScalarAsync("SELECT count(*) FROM information_schema.tables WHERE table_schema = 'operations' AND table_name = 'trips'"))
            .ShouldBe(1L);
        (await ScalarAsync("SELECT count(*) FROM operations.__ef_migrations_history"))
            .ShouldBeGreaterThan(0L);
    }

    private async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_fixture.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
