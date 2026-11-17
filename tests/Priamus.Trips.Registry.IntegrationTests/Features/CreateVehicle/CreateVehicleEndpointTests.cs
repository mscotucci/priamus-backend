using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Priamus.Trips.Registry.Persistence;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.VehicleTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Features.CreateVehicle;

/// <summary>
/// Endpoint integration tests for <c>POST /api/registry/vehicles</c>, against the per-test PostgreSQL
/// database and the test authentication scheme.
/// </summary>
public sealed class CreateVehicleEndpointTests : RegistryIntegrationTestBase
{
    private const string Url = "/api/registry/vehicles";

    public CreateVehicleEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> CreateFactory(Action<IServiceCollection>? configureServices = null) =>
        ApiFactory.Create(ConnectionString, configureServices, useTestAuthentication: true);

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, object body) =>
        client.PostAsJsonAsync(Url, body, TestJson.Options, Ct);

    private async Task<int> CountVehiclesAsync()
    {
        await using var db = CreateDbContext();
        return await db.Vehicles.CountAsync(Ct);
    }

    [Fact]
    public async Task ValidRequest_Returns201WithAnActiveVehicleAtVersionZero()
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);

        // Act
        var response = await PostAsync(client, Body(type: "Tractor", requiresTrailer: true));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("licensePlate").GetString().ShouldBe("AB456CD");
        body.GetProperty("description").GetString().ShouldBe("Iveco Stralis 480");
        body.GetProperty("type").GetString().ShouldBe("Tractor");
        body.GetProperty("requiresTrailer").GetBoolean().ShouldBeTrue();
        body.GetProperty("status").GetString().ShouldBe("Active");
        body.GetProperty("version").GetInt32().ShouldBe(0);
        response.Headers.Location!.ToString().ShouldBe($"{Url}/{body.GetProperty("id").GetGuid()}");

        await using var db = CreateDbContext();
        var stored = await db.Vehicles.SingleAsync(Ct);
        stored.Id.Value.ShouldBe(body.GetProperty("id").GetGuid());
        stored.Status.ShouldBe(VehicleStatus.Active);
    }

    [Theory]
    [InlineData("Tractor")]
    [InlineData("Trailer")]
    [InlineData("StandaloneVehicle")]
    public async Task EveryVehicleType_IsAccepted(string type)
    {
        await using var factory = CreateFactory();

        var response = await PostAsync(BackOfficeClient(factory), Body(type: type, requiresTrailer: false));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("type").GetString().ShouldBe(type);
    }

    [Fact]
    public async Task Plate_IsStoredUppercaseWithoutSpacesAndHyphens()
    {
        await using var factory = CreateFactory();

        var response = await PostAsync(BackOfficeClient(factory), Body(licensePlate: " ab 456-cd "));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        await using var db = CreateDbContext();
        (await db.Vehicles.SingleAsync(Ct)).LicensePlate.Value.ShouldBe("AB456CD");
    }

    [Fact]
    public async Task Description_IsTrimmedAndMeasuredAfterTrimming()
    {
        await using var factory = CreateFactory();
        var padded = "  " + new string('x', Vehicle.DescriptionMaxLength) + "  ";

        var response = await PostAsync(BackOfficeClient(factory), Body(description: padded));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        await using var db = CreateDbContext();
        (await db.Vehicles.SingleAsync(Ct)).Description.ShouldBe(new string('x', Vehicle.DescriptionMaxLength));
    }

    [Theory]
    [InlineData("omitted plate", "licensePlate", null)]
    [InlineData("omitted description", "description", null)]
    [InlineData("omitted type", "type", null)]
    [InlineData("omitted requires trailer", "requiresTrailer", null)]
    [InlineData("plate too short", "licensePlate", "A")]
    [InlineData("plate too long", "licensePlate", "ABCDEFGHIJK")]
    [InlineData("plate with symbols", "licensePlate", "AB!456")]
    [InlineData("plate with accents", "licensePlate", "AB4é6")]
    [InlineData("plate with final newline", "licensePlate", "AB456CD\n")]
    [InlineData("blank plate", "licensePlate", "   ")]
    [InlineData("blank description", "description", "   ")]
    [InlineData("description over 100", "description", "0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901")]
    public async Task InvalidField_Returns400OnThatField(string scenario, string field, string? value)
    {
        // Arrange
        await using var factory = CreateFactory();
        var body = Body();
        if (value is null)
        {
            body.Remove(field);
        }
        else
        {
            body[field] = value;
        }

        // Act
        var response = await PostAsync(BackOfficeClient(factory), body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, scenario);
        // Validation errors are keyed by the member name, as for Identity; the frontend matches case-insensitively.
        (await ErrorKeysAsync(response)).ShouldBe([char.ToUpperInvariant(field[0]) + field[1..]], scenario);
        (await CountVehiclesAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task InvalidPlateAndOverlongDescription_ReportsOnlyThePlateBecauseAttributeErrorsComeFirst()
    {
        await using var factory = CreateFactory();

        var response = await PostAsync(BackOfficeClient(factory), Body(licensePlate: "A", description: new string('x', Vehicle.DescriptionMaxLength + 1)));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).OrderBy(k => k).ShouldBe(["LicensePlate"]);
        (await CountVehiclesAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData("Trailer")]
    [InlineData("StandaloneVehicle")]
    public async Task RequiresTrailerOnANonTractor_Returns400OnThatField(string type)
    {
        await using var factory = CreateFactory();

        var response = await PostAsync(BackOfficeClient(factory), Body(type: type, requiresTrailer: true));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(["RequiresTrailer"]);
        (await CountVehiclesAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData("AB456CD", VehicleType.Tractor)]
    [InlineData("ab 456-cd", VehicleType.Trailer)]
    public async Task PlateOfAnActiveVehicle_Returns409AndNamesThePlate(string plate, VehicleType existingType)
    {
        // Arrange
        await using (var db = CreateDbContext())
        {
            await SeedVehicleAsync(db, "AB456CD", existingType);
        }

        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(BackOfficeClient(factory), Body(licensePlate: plate, type: "Trailer", requiresTrailer: false));

        // Assert
        var detail = await DetailAsync(response);
        detail.ShouldBe("La targa AB456CD è già registrata su un altro mezzo.");
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, VehicleErrors.LicensePlateTaken, Ct);
        (await CountVehiclesAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task PlateOfADecommissionedVehicle_Returns409AndPointsToReactivation()
    {
        // Arrange
        await using (var db = CreateDbContext())
        {
            await SeedVehicleAsync(db, "AB456CD", decommissioned: true);
        }

        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(BackOfficeClient(factory), Body());

        // Assert
        var detail = await DetailAsync(response);
        detail.ShouldBe("La targa AB456CD è già registrata su un mezzo dismesso. Per rimetterlo in servizio, riattivalo dalla lista.");
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, VehicleErrors.LicensePlateTakenByDecommissioned, Ct);
        (await CountVehiclesAsync()).ShouldBe(1);
    }

    [Theory]
    [InlineData("Active", true)]
    [InlineData("Decommissioned", false)]
    public async Task PlateTakenBetweenTheCheckAndTheSave_Returns409(string competitorStatus, bool activeHolder)
    {
        // Arrange
        await using var factory = CreateFactory(services =>
            services.AddDbContext<RegistryDbContext>(options =>
                options.AddInterceptors(new CompetingInsertInterceptor(ConnectionString, "AB456CD", competitorStatus))));

        // Act
        var response = await PostAsync(BackOfficeClient(factory), Body());

        // Assert
        var expected = activeHolder ? VehicleErrors.LicensePlateTaken : VehicleErrors.LicensePlateTakenByDecommissioned;
        var detail = await DetailAsync(response);
        detail!.ShouldContain("AB456CD");
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, expected, Ct);
        (await CountVehiclesAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task TwoCreationsWithTheSamePlateAtTheSameMoment_OneSucceedsTheOtherGets409()
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);

        // Act
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PostAsync(client, Body())));

        // Assert
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(3);
        (await CountVehiclesAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task SuccessfulCreation_WritesOneAuditRowWithTheFullStateAndTheActor()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(BackOfficeClient(factory), Body(licensePlate: "ab 456-cd", type: "Tractor", requiresTrailer: true));

        // Assert
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        await using var db = CreateDbContext();
        var row = await db.AuditLog.SingleAsync(a => a.SubjectId == id, Ct);
        row.EventType.ShouldBe(AuditEventTypes.VehicleCreated);
        row.ActorId.ShouldBe(FakeCurrentUser.ActorId.Value);
        using var payload = JsonDocument.Parse(row.Payload);
        payload.RootElement.GetProperty("vehicleId").GetGuid().ShouldBe(id);
        payload.RootElement.GetProperty("licensePlate").GetString().ShouldBe("AB456CD");
        payload.RootElement.GetProperty("description").GetString().ShouldBe("Iveco Stralis 480");
        payload.RootElement.GetProperty("type").GetString().ShouldBe("Tractor");
        payload.RootElement.GetProperty("requiresTrailer").GetBoolean().ShouldBeTrue();
        payload.RootElement.GetProperty("status").GetString().ShouldBe("Active");
    }

    [Fact]
    public async Task RefusedCreation_WritesNoAuditRow()
    {
        // Arrange
        await using (var db = CreateDbContext())
        {
            await SeedVehicleAsync(db, "AB456CD");
        }

        int before;
        await using (var auditCountDb = CreateDbContext())
        {
            before = await auditCountDb.AuditLog.CountAsync(Ct);
        }

        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);

        // Act
        var taken = await PostAsync(client, Body());
        var invalid = await PostAsync(client, Body(licensePlate: "A"));

        // Assert
        taken.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await using var db2 = CreateDbContext();
        (await db2.AuditLog.CountAsync(Ct)).ShouldBe(before);
    }

    [Fact]
    public async Task PlateRaceLost_WritesNoAuditRowForTheLoser()
    {
        // Arrange
        await using var factory = CreateFactory(services =>
            services.AddDbContext<RegistryDbContext>(options =>
                options.AddInterceptors(new CompetingInsertInterceptor(ConnectionString, "AB456CD", "Active"))));

        // Act
        var response = await PostAsync(BackOfficeClient(factory), Body());

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await using var db = CreateDbContext();
        (await db.AuditLog.CountAsync(Ct)).ShouldBe(0);
    }

    [Theory]
    [InlineData("BackOffice", HttpStatusCode.Created)]
    [InlineData("Admin", HttpStatusCode.Created)]
    [InlineData("Driver", HttpStatusCode.Forbidden)]
    public async Task Create_IsAllowedToBackOfficeAndAdminAndForbiddenToDriver(string role, HttpStatusCode expected)
    {
        await using var factory = CreateFactory();
        var client = TestCaller.WithRole(role).ApplyTo(factory.CreateClient());

        var response = await PostAsync(client, Body());

        response.StatusCode.ShouldBe(expected);
        (await CountVehiclesAsync()).ShouldBe(expected == HttpStatusCode.Created ? 1 : 0);
    }

    [Fact]
    public async Task Create_AnonymousCaller_Returns401()
    {
        await using var factory = CreateFactory();

        var response = await PostAsync(factory.CreateClient(), Body());

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
