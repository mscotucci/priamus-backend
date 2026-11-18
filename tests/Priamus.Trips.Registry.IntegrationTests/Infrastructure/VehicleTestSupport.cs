using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.Domain.Vehicles.ValueObjects;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Time;
using Xunit;

namespace Priamus.Trips.Registry.IntegrationTests.Infrastructure;

/// <summary>Helpers shared by the vehicle endpoint tests.</summary>
public static class VehicleTestSupport
{
    /// <summary>A client of <paramref name="factory"/> authenticated as a <c>BackOffice</c> caller.</summary>
    public static HttpClient BackOfficeClient(WebApplicationFactory<Program> factory) =>
        TestCaller.WithRole("BackOffice").ApplyTo(factory.CreateClient());

    /// <summary>A valid request body; tests override or remove single fields.</summary>
    public static Dictionary<string, object?> Body(
        string licensePlate = "AB456CD",
        string description = "Iveco Stralis 480",
        string type = "Tractor",
        bool requiresTrailer = true) =>
        new()
        {
            ["licensePlate"] = licensePlate,
            ["description"] = description,
            ["type"] = type,
            ["requiresTrailer"] = requiresTrailer,
        };

    /// <summary>
    /// The <c>detail</c> of a problem response. Reads the body as text so the content stays
    /// buffered for <c>ShouldBeProblemAsync</c> afterwards.
    /// </summary>
    public static async Task<string?> DetailAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.GetProperty("detail").GetString();
    }

    /// <summary>The field names a 400 response carries errors for, sorted.</summary>
    public static async Task<string[]> ErrorKeysAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return [.. document.RootElement.GetProperty("errors").EnumerateObject().Select(e => e.Name).Order(StringComparer.Ordinal)];
    }

    /// <summary>Saves a vehicle straight through the context.</summary>
    public static async Task<Vehicle> SeedVehicleAsync(
        RegistryDbContext db,
        string licensePlate = "AB456CD",
        VehicleType type = VehicleType.Tractor,
        bool requiresTrailer = false,
        bool decommissioned = false)
    {
        var clock = new SystemClock();
        var vehicle = Vehicle.Create(
            VehicleId.New(),
            LicensePlate.Create(licensePlate),
            "Iveco Stralis 480",
            type,
            requiresTrailer,
            clock,
            FakeCurrentUser.ActorId);
        if (decommissioned)
        {
            vehicle.Decommission(clock, FakeCurrentUser.ActorId);
        }

        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return vehicle;
    }

    /// <summary>
    /// A save interceptor that, once, inserts a vehicle with the given plate through a separate
    /// connection just before the first save reaches the database: the handler's upfront check has
    /// already passed, so only the unique index can stop the write.
    /// </summary>
    public sealed class CompetingInsertInterceptor : SaveChangesInterceptor
    {
        private readonly string _connectionString;
        private readonly string _licensePlate;
        private readonly string _status;
        private readonly bool _vanishAfterFailure;
        private readonly Guid _competitorId = Guid.NewGuid();
        private bool _fired;

        public CompetingInsertInterceptor(string connectionString, string licensePlate, string status, bool vanishAfterFailure = false)
        {
            _connectionString = connectionString;
            _licensePlate = licensePlate;
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
                INSERT INTO registry.vehicles
                    (id, license_plate, description, type, requires_trailer, status,
                     created_at, created_by, last_modified_at, last_modified_by, version)
                VALUES
                    (@id, @plate, 'Concurrent', 'Trailer', false, @status,
                     now(), @actor, now(), @actor, 0)
                """;
            command.Parameters.AddWithValue("id", _competitorId);
            command.Parameters.AddWithValue("plate", _licensePlate);
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

            // The competitor that won the race on the unique index is gone before the handler
            // looks it up again to name it.
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM registry.vehicles WHERE id = @id";
            command.Parameters.AddWithValue("id", _competitorId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
