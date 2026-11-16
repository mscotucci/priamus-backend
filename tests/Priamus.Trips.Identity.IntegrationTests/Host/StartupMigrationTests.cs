using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Priamus.Trips.IntegrationTesting;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Identity.IntegrationTests.Host;

/// <summary>
/// Starts the real host against an empty database (not the migrated template) to prove where
/// the startup migration runs.
/// </summary>
[Collection("IdentityIntegrationTests")]
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
    public async Task Startup_InDevelopmentOnAnEmptyDatabase_CreatesTheIdentitySchemaAndServesOpenApi()
    {
        // Arrange
        await using var factory = ApiFactory.Create(_connectionString)
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ScalarAsync("SELECT count(*) FROM information_schema.schemata WHERE schema_name = 'identity'"))
            .ShouldBe(1L);
        (await ScalarAsync("SELECT count(*) FROM identity.__ef_migrations_history"))
            .ShouldBeGreaterThan(0L);
    }

    [Fact]
    public async Task Startup_InProduction_AppliesNoMigration()
    {
        // Arrange
        await using var factory = ApiFactory.Create(_connectionString)
            .WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

        // Act
        var act = () => factory.CreateClient();

        // Assert: no migration ran, so the bootstrap step finds no users table and the host does not start.
        var exception = Should.Throw<PostgresException>(act);
        exception.SqlState.ShouldBe(PostgresErrorCodes.UndefinedTable);
        (await ScalarAsync("SELECT count(*) FROM information_schema.schemata WHERE schema_name = 'identity'"))
            .ShouldBe(0L);
        (await ScalarAsync("SELECT count(*) FROM information_schema.tables WHERE table_name = '__ef_migrations_history'"))
            .ShouldBe(0L);
    }

    [Fact]
    public async Task Startup_WithoutTheDatabaseConnectionString_FailsNamingIt()
    {
        // Arrange: Production, so appsettings.Development.json does not supply the connection string.
        // The value is set to empty here, so the outcome does not depend on the process environment
        // defining ConnectionStrings__Database: an empty value counts as missing.
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("ConnectionStrings:Database", "");
                builder.UseSetting("Jwt:Issuer", "test-issuer");
                builder.UseSetting("Jwt:Audience", "test-audience");
                builder.UseSetting("Jwt:SigningKey", "test-signing-key-endpoint-32chars!!");
            });

        // Act
        var act = () => factory.CreateClient();

        // Assert
        var exception = Should.Throw<InvalidOperationException>(act);
        exception.Message.ShouldContain("Connection string 'Database' is required");
    }

    private async Task<long> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_fixture.AdminConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
