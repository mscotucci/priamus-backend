using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Priamus.Trips.Registry.Persistence;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.LocationTestSupport;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.VehicleTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Features.CreateLocation;

/// <summary>
/// Endpoint integration tests for <c>POST /api/registry/locations</c>, against the per-test PostgreSQL
/// database and the test authentication scheme.
/// </summary>
public sealed class CreateLocationEndpointTests : RegistryIntegrationTestBase
{
    private const string Url = "/api/registry/locations";

    public CreateLocationEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> CreateFactory(Action<IServiceCollection>? configureServices = null) =>
        ApiFactory.Create(ConnectionString, configureServices, useTestAuthentication: true);

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, object body) =>
        client.PostAsJsonAsync(Url, body, TestJson.Options, Ct);

    private async Task<int> CountLocationsAsync()
    {
        await using var db = CreateDbContext();
        return await db.Locations.CountAsync(Ct);
    }

    private async Task<int> AuditCountAsync()
    {
        await using var db = CreateDbContext();
        return await db.AuditLog.CountAsync(Ct);
    }

    [Fact]
    public async Task ValidRequest_Returns201WithAnActiveLocationAtVersionZero()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(BackOfficeClient(factory), LocationBody());

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("name").GetString().ShouldBe("Magazzino centrale");
        body.GetProperty("municipality").GetString().ShouldBe("Spoltore");
        body.GetProperty("address").GetString().ShouldBe("Via Roma 1");
        body.GetProperty("status").GetString().ShouldBe("Active");
        body.TryGetProperty("nameKey", out _).ShouldBeFalse();
        body.TryGetProperty("municipalityKey", out _).ShouldBeFalse();
        response.Headers.Location!.ToString().ShouldBe($"{Url}/{body.GetProperty("id").GetGuid()}");

        await using var db = CreateDbContext();
        var stored = await db.Locations.SingleAsync(Ct);
        stored.Id.Value.ShouldBe(body.GetProperty("id").GetGuid());
        stored.Status.ShouldBe(LocationStatus.Active);
    }

    [Fact]
    public async Task ValidRequest_ReturnsTheVersionToSendBackOnTheNextWrite()
    {
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);

        var created = await PostAsync(client, LocationBody());
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var update = LocationBody(name: "Magazzino nord");
        update["version"] = createdBody.GetProperty("version").GetInt32();
        var edited = await client.PutAsJsonAsync($"{Url}/{createdBody.GetProperty("id").GetGuid()}", update, TestJson.Options, Ct);

        createdBody.GetProperty("version").GetInt32().ShouldBe(0);
        edited.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData("omitted", true)]
    [InlineData("blank", false)]
    public async Task OnlyNameAndMunicipalityAreRequired_EmptyAddressIsStoredAsNone(string scenario, bool omit)
    {
        // Arrange
        await using var factory = CreateFactory();
        var body = LocationBody(address: "   ");
        if (omit)
        {
            body.Remove("address");
        }

        // Act
        var response = await PostAsync(BackOfficeClient(factory), body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created, scenario);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        json.GetProperty("address").ValueKind.ShouldBe(JsonValueKind.Null);
        await using var db = CreateDbContext();
        (await db.Locations.SingleAsync(Ct)).Address.ShouldBeNull();
    }

    [Fact]
    public async Task Fields_AreStoredTrimmed_WithInnerSpacesAsTyped()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(
            BackOfficeClient(factory),
            LocationBody(name: "  Magazzino   centrale  ", municipality: "  Spoltore ", address: "  Via Roma 1  "));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        await using var db = CreateDbContext();
        var stored = await db.Locations.SingleAsync(Ct);
        stored.Name.ShouldBe("Magazzino   centrale");
        stored.Municipality.ShouldBe("Spoltore");
        stored.Address.ShouldBe("Via Roma 1");
    }

    [Fact]
    public async Task Fields_AreMeasuredAfterTrimming()
    {
        await using var factory = CreateFactory();
        var name = new string('n', Location.NameMaxLength);
        var municipality = new string('m', Location.MunicipalityMaxLength);
        var address = new string('a', Location.AddressMaxLength);

        var response = await PostAsync(
            BackOfficeClient(factory),
            LocationBody(name: "  " + name + "  ", municipality: "  " + municipality + "  ", address: "  " + address + "  "));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        await using var db = CreateDbContext();
        var stored = await db.Locations.SingleAsync(Ct);
        stored.Name.ShouldBe(name);
        stored.Municipality.ShouldBe(municipality);
        stored.Address.ShouldBe(address);
    }

    [Fact]
    public async Task SameNameInAnotherMunicipality_IsSaved()
    {
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);

        var first = await PostAsync(client, LocationBody(municipality: "Spoltore"));
        var second = await PostAsync(client, LocationBody(municipality: "Pescara"));

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await CountLocationsAsync()).ShouldBe(2);
    }

    [Theory]
    [InlineData("omitted name", "name", null)]
    [InlineData("blank name", "name", "   ")]
    [InlineData("name over 100", "name", "{101}")]
    [InlineData("omitted municipality", "municipality", null)]
    [InlineData("blank municipality", "municipality", "   ")]
    [InlineData("municipality over 100", "municipality", "{101}")]
    [InlineData("address over 200", "address", "{201}")]
    public async Task InvalidField_Returns400OnThatField_AndSavesNothing(string scenario, string field, string? value)
    {
        // Arrange
        await using var factory = CreateFactory();
        var body = LocationBody();
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
        var response = await PostAsync(BackOfficeClient(factory), body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, scenario);
        (await ErrorKeysAsync(response)).ShouldBe([char.ToUpperInvariant(field[0]) + field[1..]], scenario);
        (await CountLocationsAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData("magazzino centrale", "spoltore", false)]
    [InlineData("  MAGAZZINO   Centrale ", " SPOLTORE", false)]
    [InlineData("Magazzino centrale", "Spoltore", true)]
    [InlineData("magazzino  CENTRALE", "spoltore ", true)]
    public async Task SameNameAndMunicipalityOfAnotherLocation_Returns409NamingTheExistingOneAsStored(
        string name, string municipality, bool deactivated)
    {
        // Arrange
        await using (var db = CreateDbContext())
        {
            await SeedLocationAsync(db, "Magazzino  Centrale", "Spoltore", deactivated: deactivated);
        }

        var auditBefore = await AuditCountAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(BackOfficeClient(factory), LocationBody(name, municipality));

        // Assert
        (await DetailAsync(response)).ShouldBe(deactivated
            ? "Il luogo Magazzino  Centrale, Spoltore è già in anagrafica (disattivato). Per riprenderlo, riattivalo dalla lista."
            : "Il luogo Magazzino  Centrale, Spoltore è già in anagrafica.");
        await response.ShouldBeProblemAsync(
            HttpStatusCode.Conflict,
            deactivated ? LocationErrors.TakenByDeactivated : LocationErrors.Taken,
            Ct);
        (await CountLocationsAsync()).ShouldBe(1);
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Theory]
    [InlineData("Active", true)]
    [InlineData("Deactivated", false)]
    public async Task SameLocationTakenBetweenTheCheckAndTheSave_Returns409(string competitorStatus, bool activeHolder)
    {
        // Arrange
        await using var factory = CreateFactory(services =>
            services.AddDbContext<RegistryDbContext>(options =>
                options.AddInterceptors(
                    new CompetingLocationInsertInterceptor(ConnectionString, "Magazzino Centrale", "Spoltore", competitorStatus))));

        // Act
        var response = await PostAsync(BackOfficeClient(factory), LocationBody());

        // Assert
        var detail = (await DetailAsync(response)).ShouldNotBeNull();
        detail.ShouldContain("Magazzino Centrale, Spoltore");
        await response.ShouldBeProblemAsync(
            HttpStatusCode.Conflict,
            activeHolder ? LocationErrors.Taken : LocationErrors.TakenByDeactivated,
            Ct);
        (await CountLocationsAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task RaceWinnerThatIsGoneBeforeTheLookup_Returns409NamingTheRequestedLocation()
    {
        // Arrange
        await using var factory = CreateFactory(services =>
            services.AddDbContext<RegistryDbContext>(options =>
                options.AddInterceptors(
                    new CompetingLocationInsertInterceptor(
                        ConnectionString, "Magazzino centrale", "Spoltore", "Active", vanishAfterFailure: true))));

        // Act
        var response = await PostAsync(BackOfficeClient(factory), LocationBody(name: "  Magazzino centrale ", municipality: " Spoltore "));

        // Assert
        (await DetailAsync(response)).ShouldBe("Il luogo Magazzino centrale, Spoltore è già in anagrafica.");
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, LocationErrors.Taken, Ct);
        (await CountLocationsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task TwoCreationsOfTheSameLocationAtTheSameMoment_OneSucceedsTheOthersGet409Taken()
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);

        // Act
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PostAsync(client, LocationBody())));

        // Assert
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        var conflicts = responses.Where(r => r.StatusCode == HttpStatusCode.Conflict).ToList();
        conflicts.Count.ShouldBe(3);
        foreach (var conflict in conflicts)
        {
            await conflict.ShouldBeProblemAsync(HttpStatusCode.Conflict, LocationErrors.Taken, Ct);
        }

        (await CountLocationsAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task SuccessfulCreation_WritesOneAuditRowWithTheFullStateAndTheActor()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(BackOfficeClient(factory), LocationBody());

        // Assert
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        await using var db = CreateDbContext();
        var row = await db.AuditLog.SingleAsync(a => a.SubjectId == id, Ct);
        row.EventType.ShouldBe(AuditEventTypes.LocationCreated);
        row.ActorId.ShouldBe(FakeCurrentUser.ActorId.Value);
        using var payload = JsonDocument.Parse(row.Payload);
        payload.RootElement.GetProperty("locationId").GetGuid().ShouldBe(id);
        payload.RootElement.GetProperty("name").GetString().ShouldBe("Magazzino centrale");
        payload.RootElement.GetProperty("municipality").GetString().ShouldBe("Spoltore");
        payload.RootElement.GetProperty("address").GetString().ShouldBe("Via Roma 1");
        payload.RootElement.GetProperty("status").GetString().ShouldBe("Active");
        payload.RootElement.TryGetProperty("nameKey", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task SuccessfulCreationWithoutAddress_LeavesItOutOfTheAuditPayload()
    {
        await using var factory = CreateFactory();

        var response = await PostAsync(BackOfficeClient(factory), LocationBody(address: null));

        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        await using var db = CreateDbContext();
        var row = await db.AuditLog.SingleAsync(a => a.SubjectId == id, Ct);
        using var payload = JsonDocument.Parse(row.Payload);
        payload.RootElement.TryGetProperty("address", out _).ShouldBeFalse();
        payload.RootElement.GetProperty("name").GetString().ShouldBe("Magazzino centrale");
    }

    [Fact]
    public async Task RefusedCreation_WritesNoAuditRow()
    {
        // Arrange
        await using (var db = CreateDbContext())
        {
            await SeedLocationAsync(db);
        }

        var before = await AuditCountAsync();
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);

        // Act
        var taken = await PostAsync(client, LocationBody());
        var invalid = await PostAsync(client, LocationBody(name: "  "));

        // Assert
        taken.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AuditCountAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task RaceLost_WritesNoAuditRowForTheLoser()
    {
        // Arrange
        await using var factory = CreateFactory(services =>
            services.AddDbContext<RegistryDbContext>(options =>
                options.AddInterceptors(
                    new CompetingLocationInsertInterceptor(ConnectionString, "Magazzino centrale", "Spoltore", "Active"))));

        // Act
        var response = await PostAsync(BackOfficeClient(factory), LocationBody());

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await AuditCountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData("BackOffice", HttpStatusCode.Created)]
    [InlineData("Admin", HttpStatusCode.Created)]
    [InlineData("Driver", HttpStatusCode.Forbidden)]
    public async Task Create_IsAllowedToBackOfficeAndAdminAndForbiddenToDriver(string role, HttpStatusCode expected)
    {
        await using var factory = CreateFactory();
        var client = TestCaller.WithRole(role).ApplyTo(factory.CreateClient());

        var response = await PostAsync(client, LocationBody());

        response.StatusCode.ShouldBe(expected);
        (await CountLocationsAsync()).ShouldBe(expected == HttpStatusCode.Created ? 1 : 0);
    }

    [Fact]
    public async Task Create_AnonymousCaller_Returns401()
    {
        await using var factory = CreateFactory();

        var response = await PostAsync(factory.CreateClient(), LocationBody());

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
