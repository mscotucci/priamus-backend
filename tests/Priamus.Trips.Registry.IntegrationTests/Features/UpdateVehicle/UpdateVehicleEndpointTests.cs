using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.VehicleTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Features.UpdateVehicle;

/// <summary>
/// Endpoint integration tests for <c>PUT /api/registry/vehicles/{id}</c>, against the per-test PostgreSQL
/// database and the test authentication scheme.
/// </summary>
public sealed class UpdateVehicleEndpointTests : RegistryIntegrationTestBase
{
    public UpdateVehicleEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> CreateFactory(Action<IServiceCollection>? configureServices = null) =>
        ApiFactory.Create(ConnectionString, configureServices, useTestAuthentication: true);

    private static Dictionary<string, object?> UpdateBody(
        int? version,
        string licensePlate = "XY999ZZ",
        string description = "Scania R500",
        string type = "Tractor",
        bool requiresTrailer = false)
    {
        var body = Body(licensePlate, description, type, requiresTrailer);
        if (version is not null)
        {
            body["version"] = version;
        }

        return body;
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, Guid id, object body) =>
        client.PutAsJsonAsync($"/api/registry/vehicles/{id}", body, TestJson.Options, Ct);

    private async Task<Vehicle> SeedAsync(string plate = "AB456CD", bool decommissioned = false)
    {
        await using var db = CreateDbContext();
        return await SeedVehicleAsync(db, plate, decommissioned: decommissioned);
    }

    private async Task<Vehicle> LoadAsync(Guid id)
    {
        await using var db = CreateDbContext();
        return await db.Vehicles.AsNoTracking().SingleAsync(v => v.Id == new VehicleId(id), Ct);
    }

    private async Task<int> AuditCountAsync()
    {
        await using var db = CreateDbContext();
        return await db.AuditLog.CountAsync(Ct);
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
            UpdateBody(version: 0, licensePlate: "xy 999-zz", description: "  Scania R500  ", type: "StandaloneVehicle"));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var stored = await LoadAsync(seeded.Id.Value);
        stored.LicensePlate.Value.ShouldBe("XY999ZZ");
        stored.Description.ShouldBe("Scania R500");
        stored.Type.ShouldBe(VehicleType.StandaloneVehicle);
        stored.RequiresTrailer.ShouldBeFalse();
        stored.Status.ShouldBe(VehicleStatus.Active);
        stored.Version.ShouldBe(1);
        stored.LastModifiedBy.ShouldBe(FakeCurrentUser.ActorId);
        stored.LastModifiedAt.ShouldBeGreaterThanOrEqualTo(seeded.LastModifiedAt);
    }

    [Fact]
    public async Task DecommissionedVehicle_CanBeEdited_AndStaysDecommissioned()
    {
        // Arrange
        var seeded = await SeedAsync(decommissioned: true);
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(
            BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version, description: "Rottamato"));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var stored = await LoadAsync(seeded.Id.Value);
        stored.Description.ShouldBe("Rottamato");
        stored.Status.ShouldBe(VehicleStatus.Decommissioned);
    }

    [Fact]
    public async Task KeepingItsOwnPlate_IsNotAConflict()
    {
        var seeded = await SeedAsync("AB456CD");
        await using var factory = CreateFactory();

        var response = await PutAsync(
            BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version, licensePlate: "ab-456 cd", description: "Nuova descrizione"));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData("omitted plate", "licensePlate", null)]
    [InlineData("omitted description", "description", null)]
    [InlineData("omitted type", "type", null)]
    [InlineData("omitted requires trailer", "requiresTrailer", null)]
    [InlineData("omitted version", "version", null)]
    [InlineData("plate too short", "licensePlate", "A")]
    [InlineData("plate too long", "licensePlate", "ABCDEFGHIJK")]
    [InlineData("plate with symbols", "licensePlate", "AB!456")]
    [InlineData("plate with final newline", "licensePlate", "AB456CD\n")]
    [InlineData("blank description", "description", "   ")]
    [InlineData("description over 100", "description", "0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901")]
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
            body[field] = value;
        }

        // Act
        var response = await PutAsync(BackOfficeClient(factory), seeded.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, scenario);
        // Validation errors are keyed by the member name, as for Identity; the frontend matches case-insensitively.
        (await ErrorKeysAsync(response)).ShouldBe([char.ToUpperInvariant(field[0]) + field[1..]], scenario);
        (await LoadAsync(seeded.Id.Value)).Version.ShouldBe(0);
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Theory]
    [InlineData("Trailer")]
    [InlineData("StandaloneVehicle")]
    public async Task RequiresTrailerOnANonTractor_Returns400OnThatField(string type)
    {
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();

        var response = await PutAsync(
            BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version, type: type, requiresTrailer: true));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(["RequiresTrailer"]);
        (await LoadAsync(seeded.Id.Value)).Type.ShouldBe(VehicleType.Tractor);
    }

    [Fact]
    public async Task UnknownVehicle_Returns404()
    {
        await using var factory = CreateFactory();

        var response = await PutAsync(BackOfficeClient(factory), Guid.NewGuid(), UpdateBody(version: 0));

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, VehicleErrors.NotFound, Ct);
    }

    [Theory]
    [InlineData(false, "La targa XY999ZZ è già registrata su un altro mezzo.")]
    [InlineData(true, "La targa XY999ZZ è già registrata su un mezzo dismesso. Per rimetterlo in servizio, riattivalo dalla lista.")]
    public async Task PlateOfAnotherVehicle_Returns409AndNamesThePlate(bool otherIsDecommissioned, string expectedDetail)
    {
        // Arrange
        var other = await SeedAsync("XY999ZZ", otherIsDecommissioned);
        var seeded = await SeedAsync("AB456CD");
        var auditBefore = await AuditCountAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(
            BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version, licensePlate: "xy 999-zz"));

        // Assert
        (await DetailAsync(response)).ShouldBe(expectedDetail);
        await response.ShouldBeProblemAsync(
            HttpStatusCode.Conflict,
            otherIsDecommissioned ? VehicleErrors.LicensePlateTakenByDecommissioned : VehicleErrors.LicensePlateTaken,
            Ct);
        (await LoadAsync(seeded.Id.Value)).LicensePlate.Value.ShouldBe("AB456CD");
        (await LoadAsync(other.Id.Value)).Version.ShouldBe(other.Version);
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Theory]
    [InlineData("Active", true)]
    [InlineData("Decommissioned", false)]
    public async Task PlateTakenBetweenTheCheckAndTheSave_Returns409AndChangesNothing(string competitorStatus, bool activeHolder)
    {
        // Arrange
        var seeded = await SeedAsync("AB456CD");
        var auditBefore = await AuditCountAsync();
        await using var factory = CreateFactory(services =>
            services.AddDbContext<RegistryDbContext>(options =>
                options.AddInterceptors(new CompetingInsertInterceptor(ConnectionString, "XY999ZZ", competitorStatus))));

        // Act
        var response = await PutAsync(BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version));

        // Assert
        var expected = activeHolder ? VehicleErrors.LicensePlateTaken : VehicleErrors.LicensePlateTakenByDecommissioned;
        (await DetailAsync(response)).ShouldNotBeNull().ShouldContain("XY999ZZ");
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, expected, Ct);
        (await LoadAsync(seeded.Id.Value)).LicensePlate.Value.ShouldBe("AB456CD");
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Fact]
    public async Task StaleVersion_Returns409WithTheItalianMessage_AndSavesNothing()
    {
        // Arrange: the vehicle is read at version 0, then someone else changes it.
        var seeded = await SeedAsync();
        await using (var db = CreateDbContext())
        {
            var other = await db.Vehicles.SingleAsync(Ct);
            other.Update(other.LicensePlate, "Modificato da altri", other.Type, other.RequiresTrailer, new SystemClock(), FakeCurrentUser.ActorId);
            await db.SaveChangesAsync(Ct);
        }

        var auditBefore = await AuditCountAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(BackOfficeClient(factory), seeded.Id.Value, UpdateBody(version: 0));

        // Assert
        (await DetailAsync(response)).ShouldBe("Il mezzo è stato modificato da un altro utente nel frattempo. Controlla i dati aggiornati e riprova.");
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, VehicleErrors.ConcurrentUpdate, Ct);
        var stored = await LoadAsync(seeded.Id.Value);
        stored.Description.ShouldBe("Modificato da altri");
        stored.LicensePlate.Value.ShouldBe("AB456CD");
        stored.Version.ShouldBe(1);
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Fact]
    public async Task VersionFromTheCreateResponse_IsAcceptedOnce_ThenGoesStale()
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);
        var created = await client.PostAsJsonAsync("/api/registry/vehicles", Body(), TestJson.Options, Ct);
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = createdBody.GetProperty("id").GetGuid();
        var version = createdBody.GetProperty("version").GetInt32();

        // Act
        var first = await PutAsync(client, id, UpdateBody(version, description: "Prima modifica"));
        var second = await PutAsync(client, id, UpdateBody(version, description: "Seconda modifica"));

        // Assert
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await second.ShouldBeProblemAsync(HttpStatusCode.Conflict, VehicleErrors.ConcurrentUpdate, Ct);
        (await LoadAsync(id)).Description.ShouldBe("Prima modifica");
    }

    [Fact]
    public async Task StaleVersion_IsRefusedEvenWhenTheDataIsUnchanged()
    {
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);
        (await PutAsync(client, seeded.Id.Value, UpdateBody(version: 0))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var response = await PutAsync(client, seeded.Id.Value, UpdateBody(version: 0));

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, VehicleErrors.ConcurrentUpdate, Ct);
    }

    [Fact]
    public async Task VersionAheadOfTheStoredOne_IsRefused()
    {
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();

        var response = await PutAsync(BackOfficeClient(factory), seeded.Id.Value, UpdateBody(version: 7));

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, VehicleErrors.ConcurrentUpdate, Ct);
        (await LoadAsync(seeded.Id.Value)).Version.ShouldBe(0);
    }

    [Fact]
    public async Task SuccessfulUpdate_WritesOneAuditRowWithTheFullStateAfterTheChange()
    {
        // Arrange
        var seeded = await SeedAsync(decommissioned: true);
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(
            BackOfficeClient(factory),
            seeded.Id.Value,
            UpdateBody(seeded.Version, licensePlate: "xy 999-zz", type: "Tractor", requiresTrailer: true));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using var db = CreateDbContext();
        var row = await db.AuditLog.SingleAsync(a => a.SubjectId == seeded.Id.Value && a.EventType == AuditEventTypes.VehicleUpdated, Ct);
        row.ActorId.ShouldBe(FakeCurrentUser.ActorId.Value);
        using var payload = JsonDocument.Parse(row.Payload);
        payload.RootElement.GetProperty("vehicleId").GetGuid().ShouldBe(seeded.Id.Value);
        payload.RootElement.GetProperty("licensePlate").GetString().ShouldBe("XY999ZZ");
        payload.RootElement.GetProperty("description").GetString().ShouldBe("Scania R500");
        payload.RootElement.GetProperty("type").GetString().ShouldBe("Tractor");
        payload.RootElement.GetProperty("requiresTrailer").GetBoolean().ShouldBeTrue();
        payload.RootElement.GetProperty("status").GetString().ShouldBe("Decommissioned");
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
