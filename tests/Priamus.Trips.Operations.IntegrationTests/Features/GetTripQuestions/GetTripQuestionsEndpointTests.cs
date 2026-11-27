using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Priamus.Trips.IntegrationTesting;
using Priamus.Trips.Operations.IntegrationTests.Infrastructure;
using Priamus.Trips.Operations.IntegrationTests.TestData;
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Operations.IntegrationTests.Features.GetTripQuestions;

/// <summary>
/// Endpoint integration tests for <c>GET /api/operations/driver/trip-questions</c>, with the registry
/// replaced by <see cref="FakeTripRegistry"/>, a clock fixed at 12:00 in Italy on 2026-06-15 and the test
/// authentication scheme.
/// </summary>
public sealed class GetTripQuestionsEndpointTests : OperationsIntegrationTestBase
{
    private const string Url = "/api/operations/driver/trip-questions";

    private static readonly DateTime NowUtc = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 6, 15);
    private static readonly DateTime FirstAddedAt = new(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);

    private readonly FakeTripRegistry _registry = new();
    private readonly Guid _driverId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly TripLocation _north = new(Guid.NewGuid(), "Deposito Nord", "Torino");
    private readonly TripLocation _south = new(Guid.NewGuid(), "Piattaforma Sud", "Bari");
    private readonly TripLocation _east = new(Guid.NewGuid(), "Magazzino Est", "Venezia");

    public GetTripQuestionsEndpointTests(PostgresFixture fixture) : base(fixture) { }

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

    private HttpClient DriverClient(WebApplicationFactory<Program> factory) =>
        TestCaller.DriverWith(_driverId).ApplyTo(factory.CreateClient());

    private string UrlFor(Guid? customerId = null) => $"{Url}?customerId={customerId ?? _customerId}";

    private void Ask(TripLocationsAsked asked, TripMeasureKind measure, params TripPlace[] places) =>
        _registry.Questions[(_customerId, Today)] = new TripQuestions(asked, measure, places);

    private Task UsedAsync(Guid driverId, TripLocation? pickup, TripLocation? delivery, int daysAfterStart = 0, Guid? customerId = null) =>
        new TripSeeder(ConnectionString).AddAsync(
            driverId, customerId ?? _customerId, FirstAddedAt.AddDays(daysAfterStart), pickup?.Id, delivery?.Id);

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static (string? Pickup, string? Delivery) NamesOf(JsonElement place) =>
        (NameOf(place.GetProperty("pickup")), NameOf(place.GetProperty("delivery")));

    private static string? NameOf(JsonElement location) =>
        location.ValueKind == JsonValueKind.Null ? null : location.GetProperty("name").GetString();

    [Theory]
    [InlineData(TripLocationsAsked.None, TripMeasureKind.StartAndEnd)]
    [InlineData(TripLocationsAsked.Pickup, TripMeasureKind.Weight)]
    [InlineData(TripLocationsAsked.Delivery, TripMeasureKind.Distance)]
    [InlineData(TripLocationsAsked.Both, TripMeasureKind.StartOnly)]
    public async Task Get_PassesTheLocationsAskedOfTheContractThroughAndTellsNoMeasure(
        TripLocationsAsked asked, TripMeasureKind measure)
    {
        // Arrange
        Ask(asked, measure);
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).GetAsync(UrlFor(), Ct);

        // Assert
        var body = await ReadAsync(response);
        body.GetProperty("locationsAsked").GetString().ShouldBe(asked.ToString());
        body.TryGetProperty("measure", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Get_ReturnsThePlacesOfTheContractWithNameAndMunicipality()
    {
        // Arrange
        Ask(TripLocationsAsked.Both, TripMeasureKind.StartOnly, new TripPlace(_north, _south), new TripPlace(_north, _east));
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).GetAsync(UrlFor(), Ct);

        // Assert
        var body = await ReadAsync(response);
        var places = body.GetProperty("places").EnumerateArray().ToList();
        places.Select(NamesOf).ShouldBe([("Deposito Nord", "Piattaforma Sud"), ("Deposito Nord", "Magazzino Est")]);
        var pickup = places[0].GetProperty("pickup");
        pickup.GetProperty("id").GetGuid().ShouldBe(_north.Id);
        pickup.GetProperty("municipality").GetString().ShouldBe("Torino");
    }

    [Fact]
    public async Task Get_SingleSidePlaces_LeaveTheOtherSideNull()
    {
        // Arrange
        Ask(TripLocationsAsked.Delivery, TripMeasureKind.Weight, new TripPlace(null, _south));
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).GetAsync(UrlFor(), Ct);

        // Assert
        var body = await ReadAsync(response);
        body.GetProperty("places").EnumerateArray().Select(NamesOf).ShouldBe([(null, "Piattaforma Sud")]);
    }

    [Fact]
    public async Task Get_LastPlaceOfAPairContract_IsThePairOfTheLatestTripForTheCustomer()
    {
        // Arrange
        Ask(TripLocationsAsked.Both, TripMeasureKind.StartOnly, new TripPlace(_north, _south), new TripPlace(_north, _east));
        await UsedAsync(_driverId, _north, _south, daysAfterStart: 1);
        await UsedAsync(_driverId, _north, _east, daysAfterStart: 2);
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).GetAsync(UrlFor(), Ct);

        // Assert
        var body = await ReadAsync(response);
        NamesOf(body.GetProperty("lastPlace")).ShouldBe(("Deposito Nord", "Magazzino Est"));
    }

    [Theory]
    [InlineData(TripLocationsAsked.Pickup, "Deposito Nord", null)]
    [InlineData(TripLocationsAsked.Delivery, null, "Piattaforma Sud")]
    public async Task Get_LastPlaceOfASingleSideContract_IsTheSideTheLevelNames(
        TripLocationsAsked asked, string? expectedPickup, string? expectedDelivery)
    {
        // Arrange: the latest trip also named the other side, which the contract does not ask for.
        Ask(
            asked,
            TripMeasureKind.Weight,
            asked == TripLocationsAsked.Pickup ? new TripPlace(_north, null) : new TripPlace(null, _south));
        await UsedAsync(_driverId, _north, _south);
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).GetAsync(UrlFor(), Ct);

        // Assert
        var body = await ReadAsync(response);
        NamesOf(body.GetProperty("lastPlace")).ShouldBe((expectedPickup, expectedDelivery));
    }

    [Fact]
    public async Task Get_LastPlaceTheContractNoLongerOffers_IsNull()
    {
        // Arrange: the latest trip went north to south, the contract now rates only north to east.
        Ask(TripLocationsAsked.Both, TripMeasureKind.StartOnly, new TripPlace(_north, _east));
        await UsedAsync(_driverId, _north, _south);
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).GetAsync(UrlFor(), Ct);

        // Assert
        var body = await ReadAsync(response);
        body.GetProperty("lastPlace").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_LatestTripWithoutTheLocationsTheContractAsks_HasNoLastPlace()
    {
        // Arrange: only the very latest trip counts, even when an older one named the place.
        Ask(TripLocationsAsked.Both, TripMeasureKind.StartOnly, new TripPlace(_north, _south));
        await UsedAsync(_driverId, _north, _south, daysAfterStart: 1);
        await UsedAsync(_driverId, null, null, daysAfterStart: 2);
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).GetAsync(UrlFor(), Ct);

        // Assert
        var body = await ReadAsync(response);
        body.GetProperty("lastPlace").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_LatestTripIsPlanned_ItsPlaceIsTheLastPlace()
    {
        // Arrange: a trip added and never started still counts, whatever its state.
        Ask(TripLocationsAsked.Both, TripMeasureKind.StartOnly, new TripPlace(_north, _south), new TripPlace(_north, _east));
        await UsedAsync(_driverId, _north, _south, daysAfterStart: 1);
        await new TripSeeder(ConnectionString).AddPlannedAsync(
            _driverId, _customerId, FirstAddedAt.AddDays(2), _north.Id, _east.Id);
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).GetAsync(UrlFor(), Ct);

        // Assert
        var body = await ReadAsync(response);
        NamesOf(body.GetProperty("lastPlace")).ShouldBe(("Deposito Nord", "Magazzino Est"));
    }

    [Fact]
    public async Task Get_FlatRateContract_NeverProposesALastPlace()
    {
        // Arrange: the driver used locations for this customer before, but the contract offers none.
        Ask(TripLocationsAsked.None, TripMeasureKind.StartOnly, new TripPlace(_north, _south));
        await UsedAsync(_driverId, _north, _south);
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).GetAsync(UrlFor(), Ct);

        // Assert
        var body = await ReadAsync(response);
        body.GetProperty("lastPlace").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_LastPlace_IsReadFromTheTripsOfTheDriverOfTheSessionAlone()
    {
        // Arrange: another driver added a later trip for the same customer.
        Ask(TripLocationsAsked.Both, TripMeasureKind.StartOnly, new TripPlace(_north, _south), new TripPlace(_north, _east));
        await UsedAsync(_driverId, _north, _south, daysAfterStart: 1);
        await UsedAsync(Guid.NewGuid(), _north, _east, daysAfterStart: 2);
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).GetAsync(UrlFor(), Ct);

        // Assert
        var body = await ReadAsync(response);
        NamesOf(body.GetProperty("lastPlace")).ShouldBe(("Deposito Nord", "Piattaforma Sud"));
    }

    [Fact]
    public async Task Get_LastPlace_IsReadFromTheTripsForThatCustomerAlone()
    {
        // Arrange: the driver's latest trip of all was for another customer.
        Ask(TripLocationsAsked.Both, TripMeasureKind.StartOnly, new TripPlace(_north, _south), new TripPlace(_north, _east));
        await UsedAsync(_driverId, _north, _south, daysAfterStart: 1);
        await UsedAsync(_driverId, _north, _east, daysAfterStart: 2, customerId: Guid.NewGuid());
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).GetAsync(UrlFor(), Ct);

        // Assert
        var body = await ReadAsync(response);
        NamesOf(body.GetProperty("lastPlace")).ShouldBe(("Deposito Nord", "Piattaforma Sud"));
    }

    [Fact]
    public async Task Get_NoContractToday_Returns404WithTheItalianText()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).GetAsync(UrlFor(), Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("errorCode").GetString().ShouldBe("Operations.Trip.NoContractThatDay");
        body.GetProperty("detail").GetString().ShouldBe("Il cliente non ha un contratto in quel giorno.");
    }

    [Fact]
    public async Task Get_CustomerMissing_Returns400()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).GetAsync(Url, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
