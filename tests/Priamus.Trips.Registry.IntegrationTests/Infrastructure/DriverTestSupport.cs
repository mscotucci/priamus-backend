using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Priamus.Trips.Registry.Domain.Drivers;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Registry.IntegrationTests.Infrastructure;

/// <summary>
/// Helpers shared by the driver endpoint tests. The caller, error-detail and error-key helpers are
/// those of <see cref="VehicleTestSupport"/>.
/// </summary>
public static class DriverTestSupport
{
    private const string AccountPassword = "Str0ng-account-pass!";

    /// <summary>A client of <paramref name="factory"/> authenticated as an <c>Admin</c> caller.</summary>
    public static HttpClient AdminClient(WebApplicationFactory<Program> factory) =>
        TestCaller.WithRole("Admin").ApplyTo(factory.CreateClient());

    /// <summary>A valid request body; tests override or remove single fields.</summary>
    public static Dictionary<string, object?> DriverBody(
        string firstName = "Luca",
        string lastName = "Di Febo",
        string? phone = "333 418 2291",
        Guid? userId = null) =>
        new()
        {
            ["firstName"] = firstName,
            ["lastName"] = lastName,
            ["phone"] = phone,
            ["userId"] = userId,
        };

    /// <summary>Saves a driver straight through the context.</summary>
    public static async Task<Driver> SeedDriverAsync(
        RegistryDbContext db,
        string firstName = "Luca",
        string lastName = "Di Febo",
        string? phone = "333 418 2291",
        Guid? userId = null,
        bool deactivated = false)
    {
        var clock = new SystemClock();
        var driver = Driver.Create(
            DriverId.New(),
            firstName,
            lastName,
            phone,
            userId is { } id ? new UserId(id) : null,
            clock,
            FakeCurrentUser.ActorId);
        if (deactivated)
        {
            driver.Deactivate(clock, FakeCurrentUser.ActorId);
        }

        db.Drivers.Add(driver);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return driver;
    }

    /// <summary>
    /// Creates an Identity user through <c>POST /api/identity/users</c> as an <c>Admin</c> caller,
    /// and disables it through <c>/disable</c> when asked. Returns the user's id.
    /// </summary>
    /// <param name="factory">A host built with the test authentication scheme.</param>
    /// <param name="username">The login name.</param>
    /// <param name="role">The Identity role: <c>Driver</c>, <c>BackOffice</c> or <c>Admin</c>.</param>
    /// <param name="disabled">Whether to disable the user after creating it.</param>
    public static async Task<Guid> CreateAccountAsync(
        WebApplicationFactory<Program> factory,
        string username = "luca.difebo",
        string role = "Driver",
        bool disabled = false)
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = AdminClient(factory);

        var created = await admin.PostAsJsonAsync(
            "/api/identity/users",
            new { username, password = AccountPassword, role },
            TestJson.Options,
            ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();

        if (disabled)
        {
            var response = await admin.PostAsJsonAsync($"/api/identity/users/{id}/disable", new { version = 0 }, TestJson.Options, ct);
            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        return id;
    }

    /// <summary>Counts the rows of <c>identity.users</c> through a separate connection.</summary>
    public static async Task<long> CountUsersAsync(string connectionString)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM identity.users";
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    /// <summary>
    /// A save interceptor that, once, inserts a driver linked to the given account through a
    /// separate connection just before the first save reaches the database: the handler's upfront
    /// check has already passed, so only the unique index can stop the write.
    /// </summary>
    public sealed class CompetingAccountLinkInterceptor : SaveChangesInterceptor
    {
        private readonly string _connectionString;
        private readonly Guid _userId;
        private readonly string _status;
        private readonly bool _vanishAfterFailure;
        private readonly Guid _competitorId = Guid.NewGuid();
        private bool _fired;

        public CompetingAccountLinkInterceptor(string connectionString, Guid userId, string status, bool vanishAfterFailure = false)
        {
            _connectionString = connectionString;
            _userId = userId;
            _status = status;
            _vanishAfterFailure = vanishAfterFailure;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (_fired)
            {
                return result;
            }

            _fired = true;
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO registry.drivers
                    (id, first_name, last_name, phone, user_id, status,
                     created_at, created_by, last_modified_at, last_modified_by, version)
                VALUES
                    (@id, 'Mario', 'Concorrente', NULL, @user, @status,
                     now(), @actor, now(), @actor, 0)
                """;
            command.Parameters.AddWithValue("id", _competitorId);
            command.Parameters.AddWithValue("user", _userId);
            command.Parameters.AddWithValue("status", _status);
            command.Parameters.AddWithValue("actor", FakeCurrentUser.ActorId.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return result;
        }

        public override async Task SaveChangesFailedAsync(
            DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!_vanishAfterFailure)
            {
                return;
            }

            // The competitor that won the race on the unique index lets go of the account before
            // the handler looks it up again to name it.
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE registry.drivers SET user_id = NULL WHERE id = @id";
            command.Parameters.AddWithValue("id", _competitorId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
