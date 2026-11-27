using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Priamus.Trips.IntegrationTesting;
using Priamus.Trips.Operations.Domain.Trips;
using Priamus.Trips.Operations.IntegrationTests.Infrastructure;
using Priamus.Trips.Operations.Persistence;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;
using RegistryTripErrors = Priamus.Trips.Registry.Domain.Trips.TripErrors;

namespace Priamus.Trips.Operations.IntegrationTests.Features.EndTrip;

/// <summary>
/// End-to-end tests of a trip added, started and ended through the endpoints, with the real Registry seeded
/// through its own HTTP API, a clock fixed at 12:00 in Italy on 2026-06-15 and the test authentication
/// scheme (a back-office caller for the Registry, the driver for the trip).
/// </summary>
public sealed class EndTripEndToEndTests : OperationsIntegrationTestBase
{
    private static readonly DateTime NowUtc = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

    public EndTripEndToEndTests(PostgresFixture fixture) : base(fixture)
    {
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> CreateFactory() =>
        ApiFactory.Create(
            ConnectionString,
            services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(new FixedClockStub(NowUtc));
            },
            useTestAuthentication: true);

    private OperationsDbContext NewContext() =>
        new(new DbContextOptionsBuilder<OperationsDbContext>().UseNpgsql(ConnectionString).Options);

    private static async Task<JsonElement> CreatedAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>What the Registry holds for one trip: a driver, a vehicle, a customer with a contract, locations.</summary>
    private sealed record World(
        Guid DriverId, Guid VehicleId, int VehicleVersion, Guid CustomerId, Guid ContractId, int ContractVersion,
        Guid PickupId, Guid OtherLocationId);

    private static async Task<World> SeedRegistryAsync(
        HttpClient backOffice, string pricingMode, string billingLevel = "FlatRate")
    {
        var driver = await CreatedAsync(await backOffice.PostAsJsonAsync(
            "/api/registry/drivers", new { firstName = "Mario", lastName = "Bianchi" }, Ct));
        var vehicle = await CreatedAsync(await backOffice.PostAsJsonAsync(
            "/api/registry/vehicles",
            new { licensePlate = "AB123CD", description = "Iveco", type = "StandaloneVehicle", requiresTrailer = false },
            Ct));
        var customer = await CreatedAsync(await backOffice.PostAsJsonAsync(
            "/api/registry/customers", new { companyName = "Rossi Trasporti Srl" }, Ct));
        var pickup = await CreatedAsync(await backOffice.PostAsJsonAsync(
            "/api/registry/locations", new { name = "Magazzino Nord", municipality = "Verona" }, Ct));
        var other = await CreatedAsync(await backOffice.PostAsJsonAsync(
            "/api/registry/locations", new { name = "Cava Est", municipality = "Padova" }, Ct));
        var customerId = customer.GetProperty("id").GetGuid();
        var pickupId = pickup.GetProperty("id").GetGuid();

        var perLocation = billingLevel == "PerPickupLocation";
        var contract = await CreatedAsync(await backOffice.PostAsJsonAsync(
            $"/api/registry/customers/{customerId}/contracts",
            new
            {
                validFrom = "2026-01-01",
                billingLevel,
                pricingMode,
                requiredDocuments = new[] { "DeliveryNote" },
                nightSurcharge = false,
                dayPrice = perLocation ? (decimal?)null : 40m,
                minimumRule = perLocation ? null : "None",
                locationRates = perLocation
                    ? new[] { new { pickupLocationId = (Guid?)pickupId, deliveryLocationId = (Guid?)null, dayPrice = (decimal?)40m, minimumRule = "None" } }
                    : null,
            },
            Ct));

        return new World(
            driver.GetProperty("id").GetGuid(),
            vehicle.GetProperty("id").GetGuid(),
            vehicle.GetProperty("version").GetInt32(),
            customerId,
            contract.GetProperty("id").GetGuid(),
            contract.GetProperty("version").GetInt32(),
            pickupId,
            other.GetProperty("id").GetGuid());
    }

    /// <summary>Adds the trip and starts it at 08:00 that day; returns its identifier.</summary>
    private static async Task<Guid> AddAndStartAsync(HttpClient driver, World world, bool withPickup = false)
    {
        var tripId = Guid.NewGuid();
        var added = await driver.PutAsJsonAsync(
            $"/api/operations/driver/trips/{tripId}",
            new
            {
                customerId = world.CustomerId,
                vehicleId = world.VehicleId,
                pickupLocationId = withPickup ? (Guid?)world.PickupId : null,
            },
            Ct);
        added.IsSuccessStatusCode.ShouldBeTrue(await added.Content.ReadAsStringAsync(Ct));

        var started = await driver.PostAsJsonAsync(
            $"/api/operations/driver/trips/{tripId}/start", new { day = "2026-06-15", time = "08:00" }, Ct);
        started.StatusCode.ShouldBe(HttpStatusCode.OK, await started.Content.ReadAsStringAsync(Ct));
        return tripId;
    }

    private async Task<Trip> TripAsync(Guid tripId)
    {
        await using var db = NewContext();
        return await db.Trips.AsNoTracking().SingleAsync(t => t.Id == new TripId(tripId), Ct);
    }

    [Theory]
    [InlineData("ByHour", null, null)]
    [InlineData("ByTrip", null, null)]
    [InlineData("ByWeight", 18_500, null)]
    [InlineData("ByDistance", null, 320)]
    public async Task Post_ATripOfEachPricingMode_IsAddedStartedAndEndedWithItsMeasureAndPriced(
        string pricingMode, int? kilograms, int? kilometres)
    {
        // Arrange
        await using var factory = CreateFactory();
        var world = await SeedRegistryAsync(TestCaller.WithRole("BackOffice").ApplyTo(factory.CreateClient()), pricingMode);
        var driver = TestCaller.DriverWith(world.DriverId).ApplyTo(factory.CreateClient());
        var tripId = await AddAndStartAsync(driver, world);

        // Act
        var response = await driver.PostAsJsonAsync(
            $"/api/operations/driver/trips/{tripId}/end", new { time = "11:00", kilograms, kilometres }, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("state").GetString().ShouldBe("Ended");
        body.GetProperty("end").GetString().ShouldBe("11:00:00");
        var trip = await TripAsync(tripId);
        trip.State.ShouldBe(TripState.Ended);
        trip.Number.ShouldNotBeNull();
        trip.ContractId.ShouldBe(world.ContractId);
        trip.Pricing!.Total.ShouldBeGreaterThan(0m);
        trip.Kilograms.ShouldBe(kilograms);
        trip.Kilometres.ShouldBe(kilometres);
        trip.RequiredDocuments.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Post_AVehicleDecommissionedAfterTheStart_StillEndsTheTrip()
    {
        // Arrange
        await using var factory = CreateFactory();
        var backOffice = TestCaller.WithRole("BackOffice").ApplyTo(factory.CreateClient());
        var world = await SeedRegistryAsync(backOffice, "ByTrip");
        var driver = TestCaller.DriverWith(world.DriverId).ApplyTo(factory.CreateClient());
        var tripId = await AddAndStartAsync(driver, world);
        var decommissioned = await backOffice.PostAsJsonAsync(
            $"/api/registry/vehicles/{world.VehicleId}/decommission", new { version = world.VehicleVersion }, Ct);
        decommissioned.IsSuccessStatusCode.ShouldBeTrue(await decommissioned.Content.ReadAsStringAsync(Ct));

        // Act
        var response = await driver.PostAsJsonAsync($"/api/operations/driver/trips/{tripId}/end", new { }, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await TripAsync(tripId)).State.ShouldBe(TripState.Ended);
    }

    [Fact]
    public async Task Post_AContractThatLostTheRateOfTheTrip_Returns400AndTheTripStaysInProgress()
    {
        // Arrange: priced by the pickup location, the contract is rewritten with a rate for another location.
        await using var factory = CreateFactory();
        var backOffice = TestCaller.WithRole("BackOffice").ApplyTo(factory.CreateClient());
        var world = await SeedRegistryAsync(backOffice, "ByTrip", "PerPickupLocation");
        var driver = TestCaller.DriverWith(world.DriverId).ApplyTo(factory.CreateClient());
        var tripId = await AddAndStartAsync(driver, world, withPickup: true);
        var updated = await backOffice.PutAsJsonAsync(
            $"/api/registry/customers/{world.CustomerId}/contracts/{world.ContractId}",
            new
            {
                validFrom = "2026-01-01",
                billingLevel = "PerPickupLocation",
                pricingMode = "ByTrip",
                requiredDocuments = new[] { "DeliveryNote" },
                nightSurcharge = false,
                locationRates = new[] { new { pickupLocationId = (Guid?)world.OtherLocationId, deliveryLocationId = (Guid?)null, dayPrice = (decimal?)40m, minimumRule = "None" } },
                version = world.ContractVersion,
            },
            Ct);
        updated.IsSuccessStatusCode.ShouldBeTrue(await updated.Content.ReadAsStringAsync(Ct));

        // Act
        var response = await driver.PostAsJsonAsync($"/api/operations/driver/trips/{tripId}/end", new { }, Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, RegistryTripErrors.NotPriceable, Ct);
        var trip = await TripAsync(tripId);
        trip.State.ShouldBe(TripState.InProgress);
        trip.Number.ShouldBeNull();
    }

    [Fact]
    public async Task Post_AfterTheTripIsEnded_AChangeOfTheContractOrTheVehicleChangesNothingOfTheTrip()
    {
        // Arrange
        await using var factory = CreateFactory();
        var backOffice = TestCaller.WithRole("BackOffice").ApplyTo(factory.CreateClient());
        var world = await SeedRegistryAsync(backOffice, "ByTrip");
        var driver = TestCaller.DriverWith(world.DriverId).ApplyTo(factory.CreateClient());
        var tripId = await AddAndStartAsync(driver, world);
        (await driver.PostAsJsonAsync($"/api/operations/driver/trips/{tripId}/end", new { }, Ct)).EnsureSuccessStatusCode();
        var before = await TripAsync(tripId);

        // Act
        var contract = await backOffice.PutAsJsonAsync(
            $"/api/registry/customers/{world.CustomerId}/contracts/{world.ContractId}",
            new
            {
                validFrom = "2026-01-01",
                billingLevel = "FlatRate",
                pricingMode = "ByTrip",
                requiredDocuments = Array.Empty<string>(),
                nightSurcharge = false,
                dayPrice = 99m,
                minimumRule = "None",
                version = world.ContractVersion,
            },
            Ct);
        var vehicle = await backOffice.PutAsJsonAsync(
            $"/api/registry/vehicles/{world.VehicleId}",
            new
            {
                licensePlate = "AB123CD",
                description = "Iveco",
                type = "Tractor",
                requiresTrailer = true,
                version = world.VehicleVersion,
            },
            Ct);

        // Assert
        contract.IsSuccessStatusCode.ShouldBeTrue(await contract.Content.ReadAsStringAsync(Ct));
        vehicle.IsSuccessStatusCode.ShouldBeTrue(await vehicle.Content.ReadAsStringAsync(Ct));
        var after = await TripAsync(tripId);
        after.Pricing.ShouldBe(before.Pricing);
        after.ContractId.ShouldBe(before.ContractId);
        after.Number.ShouldBe(before.Number);
        after.RequiredDocuments.ShouldBe(before.RequiredDocuments);
        after.RequiredDocuments.ShouldNotBeEmpty();
    }
}
