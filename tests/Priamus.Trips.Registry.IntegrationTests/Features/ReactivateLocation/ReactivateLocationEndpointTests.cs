using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.LocationTestSupport;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.VehicleTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Features.ReactivateLocation;

/// <summary>
/// Endpoint integration tests for <c>POST /api/registry/locations/{id}/reactivate</c>, against the
/// per-test PostgreSQL database and the test authentication scheme.
/// </summary>
public sealed class ReactivateLocationEndpointTests : RegistryIntegrationTestBase
{
    public ReactivateLocationEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> CreateFactory() =>
        ApiFactory.Create(ConnectionString, useTestAuthentication: true);

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, Guid id, object body) =>
        client.PostAsJsonAsync($"/api/registry/locations/{id}/reactivate", body, TestJson.Options, Ct);

    /// <summary>A location in the status the action starts from.</summary>
    private Task<Location> SeedAsync() => SeedWithStatusAsync(deactivated: true);

    private async Task<Location> SeedWithStatusAsync(bool deactivated)
    {
        await using var db = CreateDbContext();
        return await SeedLocationAsync(db, deactivated: deactivated);
    }

    private async Task<Location> LoadAsync(Guid id)
    {
        await using var db = CreateDbContext();
        return await db.Locations.AsNoTracking().SingleAsync(c => c.Id == new LocationId(id), Ct);
    }

    private async Task<int> AuditCountAsync()
    {
        await using var db = CreateDbContext();
        return await db.AuditLog.CountAsync(Ct);
    }

    [Fact]
    public async Task MatchingVersion_Returns204AndMovesTheStatus()
    {
        // Arrange
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(BackOfficeClient(factory), seeded.Id.Value, new { version = seeded.Version });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var stored = await LoadAsync(seeded.Id.Value);
        stored.Status.ShouldBe(LocationStatus.Active);
        stored.Version.ShouldBe(seeded.Version + 1);
        stored.LastModifiedBy.ShouldBe(FakeCurrentUser.ActorId);
        stored.Name.ShouldBe("Magazzino centrale");
        stored.Municipality.ShouldBe("Spoltore");
        stored.Address.ShouldBe("Via Roma 1");
        stored.NameKey.ShouldBe(seeded.NameKey);
        stored.MunicipalityKey.ShouldBe(seeded.MunicipalityKey);
    }

    [Fact]
    public async Task SameVersionSentTwice_SecondCallIsRefusedAsStale()
    {
        // Arrange
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);

        // Act
        var first = await PostAsync(client, seeded.Id.Value, new { version = seeded.Version });
        var second = await PostAsync(client, seeded.Id.Value, new { version = seeded.Version });

        // Assert
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await second.ShouldBeProblemAsync(HttpStatusCode.Conflict, LocationErrors.ConcurrentUpdate, Ct);
        (await LoadAsync(seeded.Id.Value)).Version.ShouldBe(seeded.Version + 1);
    }

    [Fact]
    public async Task StaleVersionOnALocationAlreadyInTheTargetStatus_Returns409AndChangesNothing()
    {
        // Arrange: the client read the location, then someone else already moved it to the target status.
        var seeded = await SeedAsync();
        var staleVersion = seeded.Version;
        await using (var db = CreateDbContext())
        {
            var other = await db.Locations.SingleAsync(Ct);
            other.Reactivate(new SystemClock(), FakeCurrentUser.ActorId);
            await db.SaveChangesAsync(Ct);
        }

        var auditBefore = await AuditCountAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(BackOfficeClient(factory), seeded.Id.Value, new { version = staleVersion });

        // Assert
        (await DetailAsync(response)).ShouldBe("Il luogo è stato modificato da un altro utente nel frattempo. Controlla i dati aggiornati e riprova.");
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, LocationErrors.ConcurrentUpdate, Ct);
        var stored = await LoadAsync(seeded.Id.Value);
        stored.Status.ShouldBe(LocationStatus.Active);
        stored.Version.ShouldBe(staleVersion + 1);
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Fact]
    public async Task VersionAheadOfTheStoredOne_IsRefused()
    {
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();

        var response = await PostAsync(BackOfficeClient(factory), seeded.Id.Value, new { version = seeded.Version + 7 });

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, LocationErrors.ConcurrentUpdate, Ct);
        (await LoadAsync(seeded.Id.Value)).Status.ShouldBe(LocationStatus.Deactivated);
    }

    [Fact]
    public async Task MissingVersion_Returns400OnVersion_AndChangesNothing()
    {
        // Arrange
        var seeded = await SeedAsync();
        var auditBefore = await AuditCountAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(BackOfficeClient(factory), seeded.Id.Value, new Dictionary<string, object?>());

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(["Version"]);
        (await LoadAsync(seeded.Id.Value)).Status.ShouldBe(LocationStatus.Deactivated);
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Fact]
    public async Task UnknownLocation_Returns404()
    {
        await using var factory = CreateFactory();

        var response = await PostAsync(BackOfficeClient(factory), Guid.NewGuid(), new { version = 0 });

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, LocationErrors.NotFound, Ct);
    }

    [Fact]
    public async Task LocationAlreadyInTheTargetStatusWithMatchingVersion_Returns204WithoutAuditOrVersionBump()
    {
        // Arrange
        var seeded = await SeedWithStatusAsync(deactivated: false);
        var auditBefore = await AuditCountAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(BackOfficeClient(factory), seeded.Id.Value, new { version = seeded.Version });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await LoadAsync(seeded.Id.Value)).Version.ShouldBe(seeded.Version);
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Fact]
    public async Task Success_WritesOneAuditRowWithTheEventTypeAndTheLocationId()
    {
        // Arrange
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(BackOfficeClient(factory), seeded.Id.Value, new { version = seeded.Version });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using var db = CreateDbContext();
        var rows = await db.AuditLog
            .Where(a => a.SubjectId == seeded.Id.Value && a.EventType == AuditEventTypes.LocationReactivated)
            .ToListAsync(Ct);
        var row = rows.ShouldHaveSingleItem();
        row.EventType.ShouldBe("registry.location.reactivated");
        row.ActorId.ShouldBe(FakeCurrentUser.ActorId.Value);
        using var payload = JsonDocument.Parse(row.Payload);
        payload.RootElement.GetProperty("locationId").GetGuid().ShouldBe(seeded.Id.Value);
    }

    [Theory]
    [InlineData("BackOffice", HttpStatusCode.NoContent)]
    [InlineData("Admin", HttpStatusCode.NoContent)]
    [InlineData("Driver", HttpStatusCode.Forbidden)]
    public async Task Reactivate_IsAllowedToBackOfficeAndAdminAndForbiddenToDriver(string role, HttpStatusCode expected)
    {
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();
        var client = TestCaller.WithRole(role).ApplyTo(factory.CreateClient());

        var response = await PostAsync(client, seeded.Id.Value, new { version = seeded.Version });

        response.StatusCode.ShouldBe(expected);
        (await LoadAsync(seeded.Id.Value)).Status.ShouldBe(
            expected == HttpStatusCode.NoContent ? LocationStatus.Active : LocationStatus.Deactivated);
    }

    [Fact]
    public async Task Reactivate_AnonymousCaller_Returns401()
    {
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();

        var response = await PostAsync(factory.CreateClient(), seeded.Id.Value, new { version = seeded.Version });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
