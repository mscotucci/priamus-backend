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
using Priamus.Trips.Operations.IntegrationTests.TestData;
using Priamus.Trips.Operations.Persistence;
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;
using RegistryTripErrors = Priamus.Trips.Registry.Domain.Trips.TripErrors;
using TripMeasureKind = Priamus.Trips.Registry.Contracts.TripMeasureKind;
using TripMinimumKind = Priamus.Trips.Registry.Contracts.TripMinimumKind;
using TripPricing = Priamus.Trips.Registry.Contracts.TripPricing;
using TripRateSource = Priamus.Trips.Registry.Contracts.TripRateSource;

namespace Priamus.Trips.Operations.IntegrationTests.Features.EndTrip;

/// <summary>
/// Endpoint integration tests for <c>POST /api/operations/driver/trips/{tripId}/end</c>, with the registry
/// replaced by <see cref="FakeTripRegistry"/>, a clock fixed at 12:00 in Italy on 2026-06-15 and the test
/// authentication scheme.
/// </summary>
public sealed class EndTripEndpointTests : OperationsIntegrationTestBase
{
    private static readonly DateTime NowUtc = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 6, 15);
    private static readonly Guid ContractId = Guid.NewGuid();

    private readonly FakeTripRegistry _registry = new();
    private readonly Guid _driverId = Guid.NewGuid();
    private readonly TripCustomer _customer = new(Guid.NewGuid(), "Rossi Trasporti Srl", "01234567890");
    private readonly TripVehicle _tractor = new(Guid.NewGuid(), "AA111AA", "Iveco Stralis", TripVehicleKind.Tractor, true);

    public EndTripEndpointTests(PostgresFixture fixture) : base(fixture)
    {
        _registry.Customers.Add(_customer);
        _registry.Vehicles.Add(_tractor);
        _registry.PricedTripResult = Priced(TripMeasureKind.StartAndEnd);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string UrlOf(Guid tripId) => $"/api/operations/driver/trips/{tripId}/end";

    private static PricedTrip Priced(TripMeasureKind measure, params string[] documents) =>
        new(
            ContractId,
            measure,
            documents,
            new TripPricing(
                TripRateSource.Contract, null, null, null,
                DayPrice: 60m, NightPrice: 70m, NightCallFee: 5m,
                TripMinimumKind.None, MinimumAmount: null, MinimumThreshold: null, MinimumOveragePrice: null,
                NightBandStart: new TimeOnly(22, 0), NightBandEnd: new TimeOnly(6, 0), NightSurcharge: true,
                IsNight: false, Quantity: 1, UnitPrice: 60m, BaseAmount: 60m, AmountAfterMinimum: 60m,
                MinimumApplied: false, AppliedNightCallFee: null, Total: 60m));

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

    private HttpClient DriverClient(WebApplicationFactory<Program> factory) =>
        TestCaller.DriverWith(_driverId).ApplyTo(factory.CreateClient());

    private OperationsDbContext NewContext() =>
        new(new DbContextOptionsBuilder<OperationsDbContext>().UseNpgsql(ConnectionString).Options);

    private async Task<List<Trip>> TripsAsync()
    {
        await using var db = NewContext();
        return await db.Trips.AsNoTracking().ToListAsync(Ct);
    }

    private async Task<TripId> SaveAsync(Trip trip)
    {
        await using var db = NewContext();
        db.Trips.Add(trip);
        await db.SaveChangesAsync(Ct);
        return trip.Id;
    }

    private Task<TripId> SeedInProgressAsync(DateOnly day, TimeOnly start, Guid? driverId = null) =>
        SaveAsync(TripSeeder.BuildInProgress(
            TripId.New(), driverId ?? _driverId, _customer.Id, _tractor.Id, null, null, null, day, start, NowUtc,
            FakeCurrentUser.ActorId));

    private Task SeedEndedAsync(DateTime startedAt, TimeOnly end, Guid? driverId = null) =>
        new TripSeeder(ConnectionString).AddAsync(driverId ?? _driverId, _customer.Id, startedAt, end: end);

    private async Task AssertStillInProgressAsync(TripId id)
    {
        var trip = (await TripsAsync()).Single(t => t.Id == id);
        trip.State.ShouldBe(TripState.InProgress);
        trip.End.ShouldBeNull();
        trip.Number.ShouldBeNull();
        trip.Pricing.ShouldBeNull();
    }

    /// <summary>The <c>detail</c> of a problem response; the body stays readable by the other assertions.</summary>
    private static async Task<string> DetailOfAsync(HttpResponseMessage response)
    {
        await response.Content.LoadIntoBufferAsync(Ct);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return body.RootElement.GetProperty("detail").GetString()!;
    }

    [Fact]
    public async Task Post_NoTime_EndsTheTripNowAndKeepsWhatItWasPricedWithAndItsDocuments()
    {
        // Arrange
        var id = await SeedInProgressAsync(Today, new TimeOnly(9, 0));
        _registry.PricedTripResult = Priced(TripMeasureKind.StartAndEnd, "CMR", "DDT");
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(UrlOf(id.Value), new { }, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("state").GetString().ShouldBe("Ended");
        body.GetProperty("start").GetString().ShouldBe("09:00:00");
        body.GetProperty("end").GetString().ShouldBe("12:00:00");
        body.GetProperty("number").GetInt64().ShouldBeGreaterThan(0);
        var trip = (await TripsAsync()).ShouldHaveSingleItem();
        trip.State.ShouldBe(TripState.Ended);
        trip.End.ShouldBe(new TimeOnly(12, 0));
        trip.EndedAt.ShouldBe(NowUtc);
        trip.ContractId.ShouldBe(ContractId);
        trip.Pricing!.Total.ShouldBe(60m);
        trip.RequiredDocuments.ShouldBe(["CMR", "DDT"]);
    }

    [Fact]
    public async Task Post_ATimeInThePast_EndsTheTripAtThatTime()
    {
        // Arrange
        var id = await SeedInProgressAsync(Today, new TimeOnly(9, 0));
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(UrlOf(id.Value), new { time = "10:45" }, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("end").GetString().ShouldBe("10:45:00");
        (await TripsAsync()).ShouldHaveSingleItem().End.ShouldBe(new TimeOnly(10, 45));
    }

    [Fact]
    public async Task Post_ATypedTimeWithSecondsInTheCurrentMinute_EndsTheTripStoredAndReturnedWithoutSeconds()
    {
        // Arrange
        var id = await SeedInProgressAsync(Today, new TimeOnly(9, 0));
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(UrlOf(id.Value), new { time = "12:00:30" }, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("end").GetString().ShouldBe("12:00:00");
        (await TripsAsync()).ShouldHaveSingleItem().End.ShouldBe(new TimeOnly(12, 0));
    }

    [Theory]
    [InlineData("12:01")]
    [InlineData("23:59")]
    public async Task Post_ATypedTimeLaterThanNow_Returns422AndEndsNothing(string time)
    {
        // Arrange
        var id = await SeedInProgressAsync(Today, new TimeOnly(9, 0));
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(UrlOf(id.Value), new { time }, Ct);

        // Assert
        (await DetailOfAsync(response)).ShouldBe("L'ora non può essere nel futuro.");
        await response.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, TripErrors.InTheFuture, Ct);
        await AssertStillInProgressAsync(id);
    }

    [Fact]
    public async Task Post_ATypedTimeNotAfterTheStart_IsTheDayAfterTheStart()
    {
        // Arrange: started yesterday at 23:00, ended at 08:00 today.
        var id = await SeedInProgressAsync(new DateOnly(2026, 6, 14), new TimeOnly(23, 0));
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(UrlOf(id.Value), new { time = "08:00" }, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var trip = (await TripsAsync()).ShouldHaveSingleItem();
        trip.State.ShouldBe(TripState.Ended);
        trip.Day.ShouldBe(new DateOnly(2026, 6, 14));
        trip.End.ShouldBe(new TimeOnly(8, 0));
    }

    [Fact]
    public async Task Post_ATypedTimeEqualToTheStart_Returns409BecauseTheTripWouldLast24Hours()
    {
        // Arrange
        var id = await SeedInProgressAsync(new DateOnly(2026, 6, 14), new TimeOnly(9, 0));
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(UrlOf(id.Value), new { time = "09:00" }, Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, TripErrors.DurationOutOfRange, Ct);
        await AssertStillInProgressAsync(id);
    }

    [Theory]
    [InlineData("12:00", HttpStatusCode.Conflict)] // started exactly 24 hours ago
    [InlineData("11:59", HttpStatusCode.Conflict)] // started more than 24 hours ago
    [InlineData("12:01", HttpStatusCode.OK)] // started 23 hours 59 minutes ago
    public async Task Post_NoTime_IsRefusedFrom24HoursAfterTheStart(string startYesterday, HttpStatusCode expected)
    {
        // Arrange
        var id = await SeedInProgressAsync(new DateOnly(2026, 6, 14), TimeOnly.Parse(startYesterday));
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(UrlOf(id.Value), new { }, Ct);

        // Assert
        response.StatusCode.ShouldBe(expected);
        if (expected == HttpStatusCode.Conflict)
        {
            await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, TripErrors.DurationOutOfRange, Ct);
            await AssertStillInProgressAsync(id);
        }
        else
        {
            (await TripsAsync()).ShouldHaveSingleItem().State.ShouldBe(TripState.Ended);
        }
    }

    [Theory]
    [InlineData("10:30")] // inside the other trip
    [InlineData("11:30")] // the span it closes contains the other trip
    public async Task Post_AnEndThatClosesASpanContainingAnotherTripsStart_Returns422WithTheOtherTripsTimes(string time)
    {
        // Arrange: this trip started at 09:00; another one ran from 10:00 to 11:00.
        await SeedEndedAsync(new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc), new TimeOnly(11, 0));
        var id = await SeedInProgressAsync(Today, new TimeOnly(9, 0));
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(UrlOf(id.Value), new { time }, Ct);

        // Assert
        (await DetailOfAsync(response)).ShouldBe("Hai già un viaggio dalle 10:00 alle 11:00 di quel giorno. Controlla l'ora.");
        await response.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, TripErrors.Overlaps, Ct);
        await AssertStillInProgressAsync(id);
    }

    [Fact]
    public async Task Post_AnEndExactlyWhenAnotherTripStarts_EndsTheTrip()
    {
        // Arrange
        await SeedEndedAsync(new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc), new TimeOnly(11, 0));
        var id = await SeedInProgressAsync(Today, new TimeOnly(9, 0));
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(UrlOf(id.Value), new { time = "10:00" }, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Post_AnEndWhereAnotherDriverHasATrip_EndsTheTrip()
    {
        // Arrange
        await SeedEndedAsync(
            new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc), new TimeOnly(11, 0), driverId: Guid.NewGuid());
        var id = await SeedInProgressAsync(Today, new TimeOnly(9, 0));
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(UrlOf(id.Value), new { time = "10:30" }, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Post_TwoTripsEndedOneAfterTheOther_GetNumbersThatGrow()
    {
        // Arrange
        var first = await SeedInProgressAsync(Today, new TimeOnly(8, 0));
        await using var factory = CreateFactory();
        var client = DriverClient(factory);
        (await client.PostAsJsonAsync(UrlOf(first.Value), new { time = "09:00" }, Ct)).EnsureSuccessStatusCode();
        var second = await SeedInProgressAsync(Today, new TimeOnly(10, 0));

        // Act
        (await client.PostAsJsonAsync(UrlOf(second.Value), new { time = "11:00" }, Ct)).EnsureSuccessStatusCode();

        // Assert
        var trips = await TripsAsync();
        var firstNumber = trips.Single(t => t.Id == first).Number!.Value;
        var secondNumber = trips.Single(t => t.Id == second).Number!.Value;
        secondNumber.ShouldBeGreaterThan(firstNumber);
    }

    [Theory]
    [InlineData(TripMeasureKind.Weight, 18_500, null)]
    [InlineData(TripMeasureKind.Distance, null, 320)]
    [InlineData(TripMeasureKind.StartAndEnd, null, null)]
    [InlineData(TripMeasureKind.StartOnly, null, null)]
    public async Task Post_TheMeasureTheContractAsks_IsKeptAndNothingElse(
        TripMeasureKind measure, int? kilograms, int? kilometres)
    {
        // Arrange
        var id = await SeedInProgressAsync(Today, new TimeOnly(9, 0));
        _registry.PricedTripResult = Priced(measure);
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(UrlOf(id.Value), new { kilograms, kilometres }, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("kilograms").ValueKind.ShouldBe(kilograms is null ? JsonValueKind.Null : JsonValueKind.Number);
        body.GetProperty("kilometres").ValueKind.ShouldBe(kilometres is null ? JsonValueKind.Null : JsonValueKind.Number);
        var trip = (await TripsAsync()).ShouldHaveSingleItem();
        trip.Kilograms.ShouldBe(kilograms);
        trip.Kilometres.ShouldBe(kilometres);
    }

    [Theory]
    [InlineData(TripMeasureKind.Weight, null, null)] // weight asked, none given
    [InlineData(TripMeasureKind.Distance, 100, null)] // kilograms given where distance is asked
    [InlineData(TripMeasureKind.StartOnly, null, 50)] // kilometres given where nothing is asked
    public async Task Post_AMeasureThatDoesNotMatchWhatWasPriced_Returns400AndEndsNothing(
        TripMeasureKind measure, int? kilograms, int? kilometres)
    {
        // Arrange
        var id = await SeedInProgressAsync(Today, new TimeOnly(9, 0));
        _registry.PricedTripResult = Priced(measure);
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(UrlOf(id.Value), new { kilograms, kilometres }, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await AssertStillInProgressAsync(id);
    }

    [Theory]
    [InlineData("""{ "kilograms": 0 }""")]
    [InlineData("""{ "kilograms": 100000 }""")]
    [InlineData("""{ "kilometres": 0 }""")]
    [InlineData("""{ "kilometres": 10000 }""")]
    public async Task Post_AMeasureOutOfRange_Returns400AndEndsNothing(string json)
    {
        // Arrange
        var id = await SeedInProgressAsync(Today, new TimeOnly(9, 0));
        await using var factory = CreateFactory();
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await DriverClient(factory).PostAsync(UrlOf(id.Value), content, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await AssertStillInProgressAsync(id);
    }

    [Fact]
    public async Task Post_ATripTheContractNoLongerPrices_Returns400AndTheTripStaysInProgress()
    {
        // Arrange
        var id = await SeedInProgressAsync(Today, new TimeOnly(9, 0));
        _registry.PricedTripResult = RegistryTripErrors.NotPriceable;
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(UrlOf(id.Value), new { }, Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, RegistryTripErrors.NotPriceable, Ct);
        await AssertStillInProgressAsync(id);
    }

    [Fact]
    public async Task Post_AContractThatChangedItsMeasure_Returns409AndTheTripStaysInProgress()
    {
        // Arrange
        var id = await SeedInProgressAsync(Today, new TimeOnly(9, 0));
        _registry.PricedTripResult = RegistryTripErrors.MeasureChanged;
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(UrlOf(id.Value), new { kilograms = 100 }, Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, RegistryTripErrors.MeasureChanged, Ct);
        await AssertStillInProgressAsync(id);
    }

    [Fact]
    public async Task Post_AContractThatChangedItsMeasure_AnswersWithTheRegistrysItalianText()
    {
        // Arrange
        var id = await SeedInProgressAsync(Today, new TimeOnly(9, 0));
        _registry.PricedTripResult = RegistryTripErrors.MeasureChanged;
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(UrlOf(id.Value), new { kilograms = 100 }, Ct);

        // Assert
        (await DetailOfAsync(response)).ShouldBe(
            "Il contratto è cambiato mentre terminavi il viaggio. Controlla i dati e riprova.");
    }

    [Fact]
    public async Task Post_ATripTheContractNoLongerPrices_AnswersWithTheRegistrysItalianText()
    {
        // Arrange
        var id = await SeedInProgressAsync(Today, new TimeOnly(9, 0));
        _registry.PricedTripResult = RegistryTripErrors.NotPriceable;
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(UrlOf(id.Value), new { }, Ct);

        // Assert
        (await DetailOfAsync(response)).ShouldBe("Non è stato possibile terminare il viaggio. Chiama la segreteria.");
    }

    [Fact]
    public async Task Post_AfterTheRegistryFactsOfTheTripChanged_StillEndsTheTripAndAsksNoValidation()
    {
        // Arrange: a vehicle decommissioned or a location deactivated since the start would refuse a validation.
        var id = await SeedInProgressAsync(Today, new TimeOnly(9, 0));
        _registry.ValidateTripResult = RegistryTripErrors.AnswersChanged;
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(UrlOf(id.Value), new { }, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        _registry.ValidatedAnswers.ShouldBeEmpty();
        (await TripsAsync()).ShouldHaveSingleItem().State.ShouldBe(TripState.Ended);
    }

    [Fact]
    public async Task Post_OnATripAlreadyEnded_Answers200WithTheTripAsItIsAndChangesNothing()
    {
        // Arrange
        var id = await SeedInProgressAsync(Today, new TimeOnly(9, 0));
        await using var factory = CreateFactory();
        var client = DriverClient(factory);
        (await client.PostAsJsonAsync(UrlOf(id.Value), new { time = "10:00" }, Ct)).EnsureSuccessStatusCode();
        var ended = (await TripsAsync()).ShouldHaveSingleItem();
        // A second end must not reach the registry: it would be priced again.
        _registry.PricedTripResult = null;

        // Act
        var response = await client.PostAsJsonAsync(UrlOf(id.Value), new { time = "11:00" }, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("state").GetString().ShouldBe("Ended");
        body.GetProperty("end").GetString().ShouldBe("10:00:00");
        body.GetProperty("number").GetInt64().ShouldBe(ended.Number!.Value);
        var trip = (await TripsAsync()).ShouldHaveSingleItem();
        trip.End.ShouldBe(new TimeOnly(10, 0));
        trip.Number.ShouldBe(ended.Number);
    }

    [Fact]
    public async Task Post_OnAPlannedTrip_Returns409AndChangesNothing()
    {
        // Arrange
        var id = await SaveAsync(TripSeeder.BuildPlanned(
            TripId.New(), _driverId, _customer.Id, _tractor.Id, null, null, null, NowUtc, FakeCurrentUser.ActorId));
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(UrlOf(id.Value), new { }, Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, TripErrors.NotInProgress, Ct);
        (await TripsAsync()).ShouldHaveSingleItem().State.ShouldBe(TripState.Planned);
    }

    [Fact]
    public async Task Post_OnAnUnknownTrip_Returns404()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(UrlOf(Guid.NewGuid()), new { }, Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, TripErrors.NotFound, Ct);
    }

    [Fact]
    public async Task Post_OnAnotherDriversTrip_Returns403AndEndsNothing()
    {
        // Arrange
        var id = await SeedInProgressAsync(Today, new TimeOnly(9, 0), driverId: Guid.NewGuid());
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).PostAsJsonAsync(UrlOf(id.Value), new { }, Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, TripErrors.OwnedByAnotherDriver, Ct);
        await AssertStillInProgressAsync(id);
    }

    [Fact]
    public async Task OpenApiDocument_ListsEndTripWithItsRequestAndItsResponses()
    {
        // Arrange
        await using var factory = ApiFactory.Create(ConnectionString);

        // Act
        var document = await factory.CreateClient().GetStringAsync("/openapi/v1.json", Ct);

        // Assert
        using var openApi = JsonDocument.Parse(document);
        var operation = openApi.RootElement.GetProperty("paths")
            .GetProperty("/api/operations/driver/trips/{tripId}/end")
            .GetProperty("post");
        operation.GetProperty("operationId").GetString().ShouldBe("EndTrip");
        var responses = operation.GetProperty("responses").EnumerateObject().Select(r => r.Name).ToList();
        responses.ShouldContain("200");
        responses.ShouldContain("409");
        responses.ShouldContain("422");
        var request = operation.GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString()!;
        var properties = openApi.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty(request.Split('/')[^1]).GetProperty("properties").EnumerateObject()
            .Select(p => p.Name)
            .Order()
            .ToList();
        properties.ShouldBe(["kilograms", "kilometres", "time"]);
    }
}
