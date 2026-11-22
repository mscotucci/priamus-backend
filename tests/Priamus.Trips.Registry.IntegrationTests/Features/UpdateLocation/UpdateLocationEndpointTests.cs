using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.LocationTestSupport;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.VehicleTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Features.UpdateLocation;

/// <summary>
/// Endpoint integration tests for <c>PUT /api/registry/locations/{id}</c>, against the per-test PostgreSQL
/// database and the test authentication scheme.
/// </summary>
public sealed class UpdateLocationEndpointTests : RegistryIntegrationTestBase
{
    public UpdateLocationEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> CreateFactory(Action<IServiceCollection>? configureServices = null) =>
        ApiFactory.Create(ConnectionString, configureServices, useTestAuthentication: true);

    private static Dictionary<string, object?> UpdateBody(
        int? version,
        string name = "Cantiere nord",
        string municipality = "Pescara",
        string? address = "Corso Umberto 5")
    {
        var body = LocationBody(name, municipality, address);
        if (version is not null)
        {
            body["version"] = version;
        }

        return body;
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, Guid id, object body) =>
        client.PutAsJsonAsync($"/api/registry/locations/{id}", body, TestJson.Options, Ct);

    private async Task<Location> SeedAsync(
        string name = "Magazzino centrale", string municipality = "Spoltore", bool deactivated = false)
    {
        await using var db = CreateDbContext();
        return await SeedLocationAsync(db, name, municipality, deactivated: deactivated);
    }

    private async Task<Location> LoadAsync(Guid id)
    {
        await using var db = CreateDbContext();
        return await db.Locations.AsNoTracking().SingleAsync(l => l.Id == new LocationId(id), Ct);
    }

    private async Task<int> AuditCountAsync()
    {
        await using var db = CreateDbContext();
        return await db.AuditLog.CountAsync(Ct);
    }

    private async Task ChangeBehindTheClientsBackAsync(Guid id)
    {
        await using var db = CreateDbContext();
        var other = await db.Locations.SingleAsync(l => l.Id == new LocationId(id), Ct);
        other.Update("Modificato da altri", other.Municipality, other.Address, new SystemClock(), FakeCurrentUser.ActorId);
        await db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task ValidRequest_Returns204AndReplacesTheDataAndBumpsTheVersion()
    {
        // Arrange
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(
            BackOfficeClient(factory),
            seeded.Id.Value,
            UpdateBody(version: 0, name: "  Cantiere nord  ", municipality: " Pescara "));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var stored = await LoadAsync(seeded.Id.Value);
        stored.Name.ShouldBe("Cantiere nord");
        stored.Municipality.ShouldBe("Pescara");
        stored.Address.ShouldBe("Corso Umberto 5");
        stored.Status.ShouldBe(LocationStatus.Active);
        stored.Version.ShouldBe(1);
        stored.LastModifiedBy.ShouldBe(FakeCurrentUser.ActorId);
        stored.LastModifiedAt.ShouldBeGreaterThanOrEqualTo(seeded.LastModifiedAt);
    }

    [Fact]
    public async Task DeactivatedLocation_CanBeEdited_AndStaysDeactivated()
    {
        // Arrange
        var seeded = await SeedAsync(deactivated: true);
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(
            BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version, name: "Rinominato"));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var stored = await LoadAsync(seeded.Id.Value);
        stored.Name.ShouldBe("Rinominato");
        stored.Status.ShouldBe(LocationStatus.Deactivated);
    }

    [Theory]
    [InlineData("blank", "   ")]
    [InlineData("empty", "")]
    [InlineData("null", null)]
    public async Task EmptyAddress_ClearsTheStoredValue(string scenario, string? address)
    {
        // Arrange
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(
            BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version, address: address));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, scenario);
        (await LoadAsync(seeded.Id.Value)).Address.ShouldBeNull(scenario);
    }

    [Theory]
    [InlineData("Magazzino centrale", "Spoltore")]
    [InlineData("MAGAZZINO CENTRALE", "spoltore")]
    [InlineData("  magazzino   centrale ", "Spoltore")]
    public async Task KeepingItsOwnNameAndMunicipality_EvenChangingOnlyTheCase_IsNotADuplicateOfItself(
        string name, string municipality)
    {
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();

        var response = await PutAsync(
            BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version, name, municipality));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task NameOfAnotherLocationInAnotherMunicipality_IsAccepted()
    {
        await SeedAsync("Magazzino centrale", "Spoltore");
        var seeded = await SeedAsync("Altro", "Pescara");
        await using var factory = CreateFactory();

        var response = await PutAsync(
            BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version, "Magazzino centrale", "Pescara"));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData("omitted name", "name", null)]
    [InlineData("omitted municipality", "municipality", null)]
    [InlineData("omitted version", "version", null)]
    [InlineData("blank name", "name", "   ")]
    [InlineData("blank municipality", "municipality", "   ")]
    [InlineData("name over 100", "name", "{101}")]
    [InlineData("municipality over 100", "municipality", "{101}")]
    [InlineData("address over 200", "address", "{201}")]
    public async Task InvalidField_Returns400OnThatField_AndChangesNothing(string scenario, string field, string? value)
    {
        // Arrange
        var seeded = await SeedAsync();
        var auditBefore = await AuditCountAsync();
        await using var factory = CreateFactory();
        var body = UpdateBody(seeded.Version);
        if (value is null)
        {
            body.Remove(field);
        }
        else
        {
            // "{n}" stands for a run of n characters.
            body[field] = value.StartsWith('{') ? new string('x', int.Parse(value.Trim('{', '}'))) : value;
        }

        // Act
        var response = await PutAsync(BackOfficeClient(factory), seeded.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, scenario);
        (await ErrorKeysAsync(response)).ShouldBe([char.ToUpperInvariant(field[0]) + field[1..]], scenario);
        (await LoadAsync(seeded.Id.Value)).Version.ShouldBe(0);
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Fact]
    public async Task UnknownLocation_Returns404()
    {
        await using var factory = CreateFactory();

        var response = await PutAsync(BackOfficeClient(factory), Guid.NewGuid(), UpdateBody(version: 0));

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, LocationErrors.NotFound, Ct);
    }

    [Theory]
    [InlineData(false, "Il luogo Cantiere  Nord, Pescara è già in anagrafica.")]
    [InlineData(true, "Il luogo Cantiere  Nord, Pescara è già in anagrafica (disattivato). Per riprenderlo, riattivalo dalla lista.")]
    public async Task NameAndMunicipalityOfAnotherLocation_Returns409NamingTheExistingOneAsStored(
        bool otherIsDeactivated, string expectedDetail)
    {
        // Arrange
        var other = await SeedAsync("Cantiere  Nord", "Pescara", otherIsDeactivated);
        var seeded = await SeedAsync("Magazzino centrale", "Spoltore");
        var auditBefore = await AuditCountAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(
            BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version, "  cantiere nord ", "PESCARA"));

        // Assert
        (await DetailAsync(response)).ShouldBe(expectedDetail);
        await response.ShouldBeProblemAsync(
            HttpStatusCode.Conflict,
            otherIsDeactivated ? LocationErrors.TakenByDeactivated : LocationErrors.Taken,
            Ct);
        (await LoadAsync(seeded.Id.Value)).Name.ShouldBe("Magazzino centrale");
        (await LoadAsync(other.Id.Value)).Version.ShouldBe(other.Version);
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Theory]
    [InlineData("Active", true)]
    [InlineData("Deactivated", false)]
    public async Task NameTakenBetweenTheCheckAndTheSave_Returns409AndChangesNothing(string competitorStatus, bool activeHolder)
    {
        // Arrange
        var seeded = await SeedAsync();
        var auditBefore = await AuditCountAsync();
        await using var factory = CreateFactory(services =>
            services.AddDbContext<RegistryDbContext>(options =>
                options.AddInterceptors(
                    new CompetingLocationInsertInterceptor(ConnectionString, "Cantiere Nord", "Pescara", competitorStatus))));

        // Act
        var response = await PutAsync(BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version));

        // Assert
        var detail = (await DetailAsync(response)).ShouldNotBeNull();
        detail.ShouldContain("Cantiere Nord, Pescara");
        await response.ShouldBeProblemAsync(
            HttpStatusCode.Conflict,
            activeHolder ? LocationErrors.Taken : LocationErrors.TakenByDeactivated,
            Ct);
        (await LoadAsync(seeded.Id.Value)).Name.ShouldBe("Magazzino centrale");
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Fact]
    public async Task TwoEditsToTheSameNameAndMunicipalityAtTheSameMoment_OneSucceedsTheOtherGets409()
    {
        // Arrange
        var first = await SeedAsync("Uno", "Spoltore");
        var second = await SeedAsync("Due", "Spoltore");
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);

        // Act
        var responses = await Task.WhenAll(
            PutAsync(client, first.Id.Value, UpdateBody(first.Version)),
            PutAsync(client, second.Id.Value, UpdateBody(second.Version)));

        // Assert
        responses.Count(r => r.StatusCode == HttpStatusCode.NoContent).ShouldBe(1);
        var lost = responses.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        await lost.ShouldBeProblemAsync(HttpStatusCode.Conflict, LocationErrors.Taken, Ct);
        await using var db = CreateDbContext();
        (await db.Locations.CountAsync(l => l.Name == "Cantiere nord", Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task StaleVersion_Returns409WithTheItalianMessage_AndSavesNothing()
    {
        // Arrange: the location is read at version 0, then someone else changes it.
        var seeded = await SeedAsync();
        await ChangeBehindTheClientsBackAsync(seeded.Id.Value);
        var auditBefore = await AuditCountAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(BackOfficeClient(factory), seeded.Id.Value, UpdateBody(version: 0));

        // Assert
        (await DetailAsync(response)).ShouldBe("Il luogo è stato modificato da un altro utente nel frattempo. Controlla i dati aggiornati e riprova.");
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, LocationErrors.ConcurrentUpdate, Ct);
        var stored = await LoadAsync(seeded.Id.Value);
        stored.Name.ShouldBe("Modificato da altri");
        stored.Version.ShouldBe(1);
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Fact]
    public async Task StaleVersion_WithANameTakenByAnother_GetsConcurrentUpdateNotTheDuplicateConflict()
    {
        // Arrange
        await SeedAsync("Cantiere nord", "Pescara");
        var seeded = await SeedAsync();
        await ChangeBehindTheClientsBackAsync(seeded.Id.Value);
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(BackOfficeClient(factory), seeded.Id.Value, UpdateBody(version: 0));

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, LocationErrors.ConcurrentUpdate, Ct);
        (await LoadAsync(seeded.Id.Value)).Name.ShouldBe("Modificato da altri");
    }

    [Fact]
    public async Task VersionFromTheCreateResponse_IsAcceptedOnce_ThenGoesStale()
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);
        var created = await client.PostAsJsonAsync("/api/registry/locations", LocationBody(), TestJson.Options, Ct);
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = createdBody.GetProperty("id").GetGuid();
        var version = createdBody.GetProperty("version").GetInt32();

        // Act
        var first = await PutAsync(client, id, UpdateBody(version, name: "Prima modifica"));
        var second = await PutAsync(client, id, UpdateBody(version, name: "Seconda modifica"));

        // Assert
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await second.ShouldBeProblemAsync(HttpStatusCode.Conflict, LocationErrors.ConcurrentUpdate, Ct);
        (await LoadAsync(id)).Name.ShouldBe("Prima modifica");
    }

    [Fact]
    public async Task StaleVersion_IsRefusedEvenWhenTheDataIsUnchanged()
    {
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);
        (await PutAsync(client, seeded.Id.Value, UpdateBody(version: 0))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var response = await PutAsync(client, seeded.Id.Value, UpdateBody(version: 0));

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, LocationErrors.ConcurrentUpdate, Ct);
    }

    [Fact]
    public async Task VersionAheadOfTheStoredOne_IsRefused()
    {
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();

        var response = await PutAsync(BackOfficeClient(factory), seeded.Id.Value, UpdateBody(version: 7));

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, LocationErrors.ConcurrentUpdate, Ct);
        (await LoadAsync(seeded.Id.Value)).Version.ShouldBe(0);
    }

    [Fact]
    public async Task UnchangedData_Returns204WithoutAuditOrVersionBump()
    {
        // Arrange: the body repeats exactly what is stored.
        var seeded = await SeedAsync();
        var auditBefore = await AuditCountAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(
            BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version, seeded.Name, seeded.Municipality, seeded.Address));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await LoadAsync(seeded.Id.Value)).Version.ShouldBe(seeded.Version);
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Fact]
    public async Task SuccessfulUpdate_WritesOneAuditRowWithTheFullStateAfterTheChange()
    {
        // Arrange
        var seeded = await SeedAsync(deactivated: true);
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using var db = CreateDbContext();
        var row = await db.AuditLog.SingleAsync(a => a.SubjectId == seeded.Id.Value && a.EventType == AuditEventTypes.LocationUpdated, Ct);
        row.ActorId.ShouldBe(FakeCurrentUser.ActorId.Value);
        using var payload = JsonDocument.Parse(row.Payload);
        payload.RootElement.GetProperty("locationId").GetGuid().ShouldBe(seeded.Id.Value);
        payload.RootElement.GetProperty("name").GetString().ShouldBe("Cantiere nord");
        payload.RootElement.GetProperty("municipality").GetString().ShouldBe("Pescara");
        payload.RootElement.GetProperty("address").GetString().ShouldBe("Corso Umberto 5");
        payload.RootElement.GetProperty("status").GetString().ShouldBe("Deactivated");
    }

    [Fact]
    public async Task SuccessfulUpdateClearingTheAddress_LeavesItOutOfTheAuditPayload()
    {
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();

        var response = await PutAsync(BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version, address: null));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using var db = CreateDbContext();
        var row = await db.AuditLog.SingleAsync(a => a.SubjectId == seeded.Id.Value && a.EventType == AuditEventTypes.LocationUpdated, Ct);
        using var payload = JsonDocument.Parse(row.Payload);
        payload.RootElement.TryGetProperty("address", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("BackOffice", HttpStatusCode.NoContent)]
    [InlineData("Admin", HttpStatusCode.NoContent)]
    [InlineData("Driver", HttpStatusCode.Forbidden)]
    public async Task Update_IsAllowedToBackOfficeAndAdminAndForbiddenToDriver(string role, HttpStatusCode expected)
    {
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();
        var client = TestCaller.WithRole(role).ApplyTo(factory.CreateClient());

        var response = await PutAsync(client, seeded.Id.Value, UpdateBody(seeded.Version));

        response.StatusCode.ShouldBe(expected);
        (await LoadAsync(seeded.Id.Value)).Version.ShouldBe(expected == HttpStatusCode.NoContent ? 1 : 0);
    }

    [Fact]
    public async Task Update_AnonymousCaller_Returns401()
    {
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();

        var response = await PutAsync(factory.CreateClient(), seeded.Id.Value, UpdateBody(seeded.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
