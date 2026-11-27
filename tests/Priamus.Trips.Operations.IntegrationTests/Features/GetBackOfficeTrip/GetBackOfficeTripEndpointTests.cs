using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Priamus.Trips.IntegrationTesting;
using Priamus.Trips.Operations.Domain.Trips;
using Priamus.Trips.Operations.IntegrationTests.Infrastructure;
using Priamus.Trips.Operations.IntegrationTests.TestData;
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;
using RegistryContracts = Priamus.Trips.Registry.Contracts;

namespace Priamus.Trips.Operations.IntegrationTests.Features.GetBackOfficeTrip;

/// <summary>
/// Endpoint integration tests for <c>GET /api/operations/backoffice/trips/{tripId}</c>, with the registry
/// replaced by <see cref="FakeTripRegistry"/>, a clock fixed at 12:00 in Italy on 2026-06-15 and the test
/// authentication scheme.
/// </summary>
public sealed class GetBackOfficeTripEndpointTests : OperationsIntegrationTestBase
{
    private static readonly DateTime NowUtc = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 6, 15);

    private readonly FakeTripRegistry _registry = new();
    private readonly Guid _mario = Guid.NewGuid();
    private readonly TripCustomer _alfa = new(Guid.NewGuid(), "Alfa SRL", "00000000001");
    private readonly TripLocation _north = new(Guid.NewGuid(), "Deposito Nord", "Torino");
    private readonly TripLocation _south = new(Guid.NewGuid(), "Piattaforma Sud", "Bari");
    private readonly TripVehicle _tractor = new(Guid.NewGuid(), "AA111AA", "Iveco Stralis", TripVehicleKind.Tractor, true);
    private readonly TripVehicle _trailer = new(Guid.NewGuid(), "RR333RR", "Cisterna", TripVehicleKind.Trailer, false);
    private readonly TripVehicle _rateVehicle = new(Guid.NewGuid(), "CC444CC", "Scania R", TripVehicleKind.Tractor, false);

    public GetBackOfficeTripEndpointTests(PostgresFixture fixture) : base(fixture)
    {
        _registry.Drivers[_mario] = new TripDriver("Mario", "Rossi");
        _registry.Customers.Add(_alfa);
        _registry.Locations.AddRange([_north, _south]);
        _registry.Vehicles.AddRange([_tractor, _trailer, _rateVehicle]);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> CreateFactory() =>
        ApiFactory.Create(
            ConnectionString,
            services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(new FixedClockStub(NowUtc));
                services.AddSingleton<ITripRegistry>(_registry);
            },
            useTestAuthentication: true);

    private async Task<HttpResponseMessage> SendAsync(Guid tripId, TestCaller caller)
    {
        await using var factory = CreateFactory();
        return await caller.ApplyTo(factory.CreateClient())
            .GetAsync($"/api/operations/backoffice/trips/{tripId}", Ct);
    }

    private async Task<JsonElement> GetAsync(Guid tripId)
    {
        var response = await SendAsync(tripId, TestCaller.WithRole("BackOffice"));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private async Task<string?> NotFoundCodeAsync(Guid tripId)
    {
        var response = await SendAsync(tripId, TestCaller.WithRole("BackOffice"));
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return body.GetProperty("errorCode").GetString();
    }

    private async Task<Guid> SeedEndedAsync(
        TimeOnly start,
        TimeOnly end,
        RegistryContracts.PricedTrip priced,
        DateOnly? day = null,
        Guid? trailerId = null,
        Guid? pickupId = null,
        Guid? deliveryId = null)
    {
        var id = TripId.New();
        var trip = TripSeeder.Build(
            id, _mario, _alfa.Id, _tractor.Id, trailerId, pickupId, deliveryId,
            day ?? Today.AddDays(-1), start, end, null, null, priced, 42, NowUtc, FakeCurrentUser.ActorId);
        await new TripSeeder(ConnectionString).SaveAsync(trip);
        return id.Value;
    }

    private async Task<Guid> SeedPlannedAsync(DateTime addedAt, bool deleted = false)
    {
        var id = TripId.New();
        var trip = TripSeeder.BuildPlanned(
            id, _mario, _alfa.Id, _tractor.Id, null, _north.Id, null, addedAt, FakeCurrentUser.ActorId);
        if (deleted)
        {
            trip.Delete(NowUtc, FakeCurrentUser.ActorId).IsSuccess.ShouldBeTrue();
        }

        await new TripSeeder(ConnectionString).SaveAsync(trip);
        return id.Value;
    }

    [Fact]
    public async Task Get_AsADriver_Returns403()
    {
        // Arrange
        var id = await SeedPlannedAsync(NowUtc);

        // Act
        var response = await SendAsync(id, TestCaller.DriverWith(_mario));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_AnEndedTrip_CarriesItsDataNamesAndEveryStepOfTheSavedCalculation()
    {
        // Arrange
        var priced = TripSeeder.Priced(RegistryContracts.TripMeasureKind.StartAndEnd, total: 135m, unitPrice: 65m, quantity: 120);
        priced = priced with
        {
            Pricing = priced.Pricing with
            {
                RateSource = RegistryContracts.TripRateSource.Vehicle,
                RateVehicleId = _rateVehicle.Id,
                RatePickupLocationId = _north.Id,
                RateDeliveryLocationId = _south.Id,
                MinimumKind = RegistryContracts.TripMinimumKind.FixedMinimum,
                MinimumAmount = 100m,
                MinimumApplied = true,
                BaseAmount = 90m,
                AmountAfterMinimum = 100m,
                AppliedNightCallFee = 5m,
            },
        };
        var id = await SeedEndedAsync(
            new TimeOnly(8, 0), new TimeOnly(10, 0), priced,
            trailerId: _trailer.Id, pickupId: _north.Id, deliveryId: _south.Id);

        // Act
        var body = await GetAsync(id);

        // Assert
        body.GetProperty("state").GetString().ShouldBe("Ended");
        body.GetProperty("number").GetInt64().ShouldBe(42);
        body.GetProperty("day").GetString().ShouldBe("2026-06-14");
        body.GetProperty("start").GetString().ShouldBe("08:00:00");
        body.GetProperty("end").GetString().ShouldBe("10:00:00");
        body.GetProperty("endedOn").GetString().ShouldBe("2026-06-14");
        body.GetProperty("addedAt").GetDateTime().ShouldBe(NowUtc);
        body.GetProperty("customer").GetProperty("companyName").GetString().ShouldBe("Alfa SRL");
        body.GetProperty("pickup").GetProperty("name").GetString().ShouldBe("Deposito Nord");
        body.GetProperty("delivery").GetProperty("municipality").GetString().ShouldBe("Bari");
        body.GetProperty("driver").GetProperty("lastName").GetString().ShouldBe("Rossi");
        body.GetProperty("vehicle").GetProperty("licensePlate").GetString().ShouldBe("AA111AA");
        body.GetProperty("trailer").GetProperty("licensePlate").GetString().ShouldBe("RR333RR");
        body.GetProperty("measureKind").GetString().ShouldBe("StartAndEnd");
        body.GetProperty("daysToExpiry").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("expiresOn").ValueKind.ShouldBe(JsonValueKind.Null);

        var calc = body.GetProperty("calculation");
        calc.GetProperty("rateSource").GetString().ShouldBe("Vehicle");
        calc.GetProperty("rateVehicle").GetProperty("licensePlate").GetString().ShouldBe("CC444CC");
        calc.GetProperty("ratePickup").GetProperty("name").GetString().ShouldBe("Deposito Nord");
        calc.GetProperty("rateDelivery").GetProperty("name").GetString().ShouldBe("Piattaforma Sud");
        calc.GetProperty("dayPrice").GetDecimal().ShouldBe(60m);
        calc.GetProperty("nightPrice").GetDecimal().ShouldBe(70m);
        calc.GetProperty("nightCallFee").GetDecimal().ShouldBe(5m);
        calc.GetProperty("minimumKind").GetString().ShouldBe("FixedMinimum");
        calc.GetProperty("minimumAmount").GetDecimal().ShouldBe(100m);
        calc.GetProperty("minimumApplied").GetBoolean().ShouldBeTrue();
        calc.GetProperty("quantity").GetInt32().ShouldBe(120);
        calc.GetProperty("unitPrice").GetDecimal().ShouldBe(65m);
        calc.GetProperty("baseAmount").GetDecimal().ShouldBe(90m);
        calc.GetProperty("amountAfterMinimum").GetDecimal().ShouldBe(100m);
        calc.GetProperty("appliedNightCallFee").GetDecimal().ShouldBe(5m);
        calc.GetProperty("total").GetDecimal().ShouldBe(135m);
        calc.EnumerateObject().Select(p => p.Name).ShouldNotContain("contractId");
    }

    [Fact]
    public async Task Get_AnEndedTrip_ReadsTheSavedCalculationWhateverTheRegistryAnswersNow()
    {
        // Arrange: the registry's contract questions would answer something else today
        var id = await SeedEndedAsync(
            new TimeOnly(8, 0), new TimeOnly(10, 0),
            TripSeeder.Priced(RegistryContracts.TripMeasureKind.StartAndEnd, total: 77m, unitPrice: 33m));
        _registry.PricedTripResult = TripSeeder.Priced(RegistryContracts.TripMeasureKind.Weight, total: 999m, unitPrice: 9m);

        // Act
        var calc = (await GetAsync(id)).GetProperty("calculation");

        // Assert
        calc.GetProperty("total").GetDecimal().ShouldBe(77m);
        calc.GetProperty("unitPrice").GetDecimal().ShouldBe(33m);
    }

    [Fact]
    public async Task Get_AnEndedTripAcrossMidnight_EndsTheDayAfter()
    {
        // Arrange
        var id = await SeedEndedAsync(
            new TimeOnly(23, 0), new TimeOnly(1, 0),
            TripSeeder.Priced(RegistryContracts.TripMeasureKind.StartAndEnd, total: 60m));

        // Act
        var body = await GetAsync(id);

        // Assert
        body.GetProperty("day").GetString().ShouldBe("2026-06-14");
        body.GetProperty("endedOn").GetString().ShouldBe("2026-06-15");
    }

    [Fact]
    public async Task Get_ANightTrip_CarriesTheNightBandAndTheNightFlags()
    {
        // Arrange
        var id = await SeedEndedAsync(
            new TimeOnly(23, 0), new TimeOnly(1, 0),
            TripSeeder.Priced(RegistryContracts.TripMeasureKind.StartAndEnd, total: 80m, unitPrice: 70m, isNight: true));

        // Act
        var calc = (await GetAsync(id)).GetProperty("calculation");

        // Assert
        calc.GetProperty("nightBandStart").GetString().ShouldBe("22:00:00");
        calc.GetProperty("nightBandEnd").GetString().ShouldBe("06:00:00");
        calc.GetProperty("nightSurcharge").GetBoolean().ShouldBeTrue();
        calc.GetProperty("isNight").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Get_ATripInProgress_CarriesDayAndStartButNoEndNorCalculation()
    {
        // Arrange
        var id = TripId.New();
        var trip = TripSeeder.BuildInProgress(
            id, _mario, _alfa.Id, _tractor.Id, null, _north.Id, null, Today, new TimeOnly(9, 30), NowUtc,
            FakeCurrentUser.ActorId);
        await new TripSeeder(ConnectionString).SaveAsync(trip);

        // Act
        var body = await GetAsync(id.Value);

        // Assert
        body.GetProperty("state").GetString().ShouldBe("InProgress");
        body.GetProperty("day").GetString().ShouldBe("2026-06-15");
        body.GetProperty("start").GetString().ShouldBe("09:30:00");
        body.GetProperty("end").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("endedOn").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("number").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("calculation").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_APlannedTrip_CarriesNoDayNoTimesAndItsExpiryDay()
    {
        // Arrange: added three days ago, so it expires in four
        var id = await SeedPlannedAsync(NowUtc.AddDays(-3));

        // Act
        var body = await GetAsync(id);

        // Assert
        body.GetProperty("state").GetString().ShouldBe("Planned");
        body.GetProperty("day").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("start").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("end").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("daysToExpiry").GetInt32().ShouldBe(4);
        body.GetProperty("expiresOn").GetString().ShouldBe("2026-06-19");
        body.GetProperty("calculation").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_RequiredDocuments_AreTheOnesSavedAndEmptyWhenNone()
    {
        // Arrange
        var withDocuments = await SeedEndedAsync(
            new TimeOnly(8, 0), new TimeOnly(10, 0),
            TripSeeder.Priced(RegistryContracts.TripMeasureKind.StartAndEnd, total: 60m) with
            {
                RequiredDocuments = ["DDT", "CMR"],
            });
        var withNone = await SeedPlannedAsync(NowUtc);

        // Act
        var documents = await GetAsync(withDocuments);
        var none = await GetAsync(withNone);

        // Assert
        documents.GetProperty("requiredDocuments").EnumerateArray().Select(d => d.GetString()).ShouldBe(["DDT", "CMR"]);
        none.GetProperty("requiredDocuments").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Get_AnUnknownADeletedOrAnExpiredTrip_Returns404()
    {
        // Arrange: a planned trip is expired on its eighth day
        var deleted = await SeedPlannedAsync(NowUtc, deleted: true);
        var expired = await SeedPlannedAsync(NowUtc.AddDays(-8));
        var lastDay = await SeedPlannedAsync(NowUtc.AddDays(-7));

        // Act
        var unknownCode = await NotFoundCodeAsync(Guid.NewGuid());
        var deletedCode = await NotFoundCodeAsync(deleted);
        var expiredCode = await NotFoundCodeAsync(expired);
        var lastDayBody = await GetAsync(lastDay);

        // Assert
        unknownCode.ShouldBe("Operations.Trip.NotFound");
        deletedCode.ShouldBe("Operations.Trip.NotFound");
        expiredCode.ShouldBe("Operations.Trip.NotFound");
        lastDayBody.GetProperty("daysToExpiry").GetInt32().ShouldBe(0);
    }
}
