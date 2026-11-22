using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Time;
using Xunit;

namespace Priamus.Trips.Registry.IntegrationTests.Infrastructure;

/// <summary>
/// Helpers shared by the location endpoint tests. The caller, error-detail and error-key helpers are
/// those of <see cref="VehicleTestSupport"/>.
/// </summary>
public static class LocationTestSupport
{
    /// <summary>A valid request body; tests override or remove single fields.</summary>
    public static Dictionary<string, object?> LocationBody(
        string name = "Magazzino centrale",
        string municipality = "Spoltore",
        string? address = "Via Roma 1") =>
        new()
        {
            ["name"] = name,
            ["municipality"] = municipality,
            ["address"] = address,
        };

    /// <summary>Saves a location straight through the context.</summary>
    public static async Task<Location> SeedLocationAsync(
        RegistryDbContext db,
        string name = "Magazzino centrale",
        string municipality = "Spoltore",
        string? address = "Via Roma 1",
        bool deactivated = false)
    {
        var clock = new SystemClock();
        var location = Location.Create(LocationId.New(), name, municipality, address, clock, FakeCurrentUser.ActorId);
        if (deactivated)
        {
            location.Deactivate(clock, FakeCurrentUser.ActorId);
        }

        db.Locations.Add(location);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return location;
    }

    /// <summary>
    /// A save interceptor that, once, inserts a location through a separate connection just before
    /// the first save reaches the database: the handler's upfront check has already passed, so only
    /// the unique index can stop the write.
    /// </summary>
    public sealed class CompetingLocationInsertInterceptor : SaveChangesInterceptor
    {
        private readonly string _connectionString;
        private readonly string _name;
        private readonly string _municipality;
        private readonly string _status;
        private readonly bool _vanishAfterFailure;
        private readonly Guid _competitorId = Guid.NewGuid();
        private bool _fired;

        public CompetingLocationInsertInterceptor(
            string connectionString, string name, string municipality, string status, bool vanishAfterFailure = false)
        {
            _connectionString = connectionString;
            _name = name;
            _municipality = municipality;
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
                INSERT INTO registry.locations
                    (id, name, municipality, address, status, name_key, municipality_key,
                     created_at, created_by, last_modified_at, last_modified_by, version)
                VALUES
                    (@id, @name, @municipality, NULL, @status, @nameKey, @municipalityKey,
                     now(), @actor, now(), @actor, 0)
                """;
            command.Parameters.AddWithValue("id", _competitorId);
            command.Parameters.AddWithValue("name", _name);
            command.Parameters.AddWithValue("municipality", _municipality);
            command.Parameters.AddWithValue("status", _status);
            command.Parameters.AddWithValue("nameKey", Location.KeyOf(_name));
            command.Parameters.AddWithValue("municipalityKey", Location.KeyOf(_municipality));
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
            command.CommandText = "DELETE FROM registry.locations WHERE id = @id";
            command.Parameters.AddWithValue("id", _competitorId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
