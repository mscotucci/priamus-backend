using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Domain.Users.ValueObjects;
using Priamus.Trips.Identity.Features.Login;
using Priamus.Trips.Identity.IntegrationTests.TestData;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.Identity.Persistence.DependencyInjection;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
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

    /// <summary>
    /// Calls <c>POST /api/identity/auth/login</c> and returns the <see cref="LoginResponse"/>.
    /// Asserts 200 OK so that test failures are surfaced clearly.
    /// </summary>
    protected static async Task<LoginResponse> LoginAsync(
        HttpClient client,
        string username,
        string password,
        CancellationToken ct)
    {
        var resp = await client.PostAsJsonAsync(
            "/api/identity/auth/login", new LoginRequest(username, password), ct);
        resp.StatusCode.ShouldBe(HttpStatusCode.OK,
            $"Login must succeed for user '{username}' to obtain a JWT for this test.");
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>(ct);
        body.ShouldNotBeNull();
        return body;
    }

    protected static async Task<HttpClient> AdminClientAsync(
        WebApplicationFactory<Program> factory, User admin, string password, CancellationToken ct)
    {
        var client = factory.CreateClient();
        var loginBody = await LoginAsync(client, admin.Username.Value, password, ct);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody.AccessToken);
        return client;
    }

    /// <summary>
    /// Changes the stored user twice (enable then disable, or the reverse) so its status is the
    /// same but its version has moved on by two.
    /// </summary>
    protected async Task BumpVersionKeepingStatusAsync(UserId id, CancellationToken ct)
    {
        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await db.Users.FirstAsync(u => u.Id == id, ct);
        if (user.Status == UserStatus.Active)
        {
            user.Disable(new SystemClock(), TestDataBuilders.ActorId);
            user.Enable(new SystemClock(), TestDataBuilders.ActorId);
        }
        else
        {
            user.Enable(new SystemClock(), TestDataBuilders.ActorId);
            user.Disable(new SystemClock(), TestDataBuilders.ActorId);
        }

        user.ClearDomainEvents();
        await db.SaveChangesAsync(ct);
    }

    protected async Task<(User Stored, int AuditRows)> LoadAsync(
        UserId id, string eventType, CancellationToken ct)
    {
        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var stored = await db.Users.AsNoTracking().FirstAsync(u => u.Id == id, ct);
        var audit = await db.AuditLog.AsNoTracking()
            .CountAsync(a => a.SubjectId == id.Value && a.EventType == eventType, ct);
        return (stored, audit);
    }

    protected static async Task<string> ErrorCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("errorCode").GetString()!;
    }
}
