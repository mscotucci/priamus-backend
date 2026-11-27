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

namespace Priamus.Trips.Operations.IntegrationTests.Features.ListBackOfficeTrips;

/// <summary>
/// Endpoint integration tests for <c>GET /api/operations/backoffice/trips</c>, with the registry replaced by
/// <see cref="FakeTripRegistry"/>, a clock fixed at 12:00 in Italy on 2026-06-15 and the test authentication
/// scheme.
/// </summary>
public sealed class ListBackOfficeTripsEndpointTests : OperationsIntegrationTestBase
{
    private const string Url = "/api/operations/backoffice/trips";

    private static readonly DateTime NowUtc = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 6, 15);

    private readonly FakeTripRegistry _registry = new();
    private readonly Guid _mario = Guid.NewGuid();
    private readonly Guid _luisa = Guid.NewGuid();
    private readonly TripCustomer _alfa = new(Guid.NewGuid(), "Alfa SRL", "00000000001");
    private readonly TripCustomer _beta = new(Guid.NewGuid(), "Beta SPA", null);
    private readonly TripLocation _north = new(Guid.NewGuid(), "Deposito Nord", "Torino");
    private readonly TripLocation _south = new(Guid.NewGuid(), "Piattaforma Sud", "Bari");
    private readonly TripVehicle _tractor = new(Guid.NewGuid(), "AA111AA", "Iveco Stralis", TripVehicleKind.Tractor, true);
    private readonly TripVehicle _otherTractor = new(Guid.NewGuid(), "BB222BB", "Volvo FH", TripVehicleKind.Tractor, false);
    private readonly TripVehicle _trailer = new(Guid.NewGuid(), "RR333RR", "Cisterna", TripVehicleKind.Trailer, false);

    public ListBackOfficeTripsEndpointTests(PostgresFixture fixture) : base(fixture)
    {
        _registry.Drivers[_mario] = new TripDriver("Mario", "Rossi");
        _registry.Drivers[_luisa] = new TripDriver("Luisa", "Bianchi");
        _registry.Customers.AddRange([_alfa, _beta]);
        _registry.Locations.AddRange([_north, _south]);
        _registry.Vehicles.AddRange([_tractor, _otherTractor, _trailer]);
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

    private static HttpClient BackOffice(WebApplicationFactory<Program> factory) =>
        TestCaller.WithRole("BackOffice").ApplyTo(factory.CreateClient());

    private async Task<JsonElement> GetAsync(string query = "")
    {
        await using var factory = CreateFactory();
        var response = await BackOffice(factory).GetAsync(Url + query, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static List<JsonElement> Items(JsonElement body) =>
        [.. body.GetProperty("trips").GetProperty("data").EnumerateArray()];

    private static List<long> Numbers(JsonElement body) =>
        [.. Items(body).Select(i => i.GetProperty("number").GetInt64())];

    private static int TotalCount(JsonElement body) => body.GetProperty("trips").GetProperty("totalCount").GetInt32();

    private Task SeedEndedAsync(
        long number,
        DateOnly day,
        TripCustomer? customer = null,
        Guid? driver = null,
        TripVehicle? vehicle = null,
        TripVehicle? trailer = null,
        TripLocation? pickup = null,
        TripLocation? delivery = null,
        RegistryContracts.TripMeasureKind measure = RegistryContracts.TripMeasureKind.StartAndEnd,
        decimal total = 60m,
        TimeOnly? start = null,
        bool isNight = false,
        bool nightSurcharge = true)
    {
        var trip = TripSeeder.Build(
            TripId.New(),
            driver ?? _mario,
            (customer ?? _alfa).Id,
            (vehicle ?? _tractor).Id,
            trailer?.Id,
            pickup?.Id,
            delivery?.Id,
            day,
            start ?? new TimeOnly(8, 0),
            new TimeOnly(10, 0),
            measure == RegistryContracts.TripMeasureKind.Weight ? 1000 : null,
            measure == RegistryContracts.TripMeasureKind.Distance ? 50 : null,
            TripSeeder.Priced(measure, total, isNight: isNight, nightSurcharge: nightSurcharge),
            number,
            NowUtc,
            FakeCurrentUser.ActorId);

        return new TripSeeder(ConnectionString).SaveAsync(trip);
    }

    private Task SeedInProgressAsync(DateOnly day, TimeOnly start, bool deleted = false)
    {
        // A driver has one trip in progress at a time: each one gets a driver of its own.
        var driver = Guid.NewGuid();
        _registry.Drivers[driver] = new TripDriver("Paolo", "Verdi");
        var trip = TripSeeder.BuildInProgress(
            TripId.New(), driver, _alfa.Id, _tractor.Id, null, _north.Id, null, day, start, NowUtc,
            FakeCurrentUser.ActorId);
        if (deleted)
        {
            trip.Delete(NowUtc, FakeCurrentUser.ActorId).IsSuccess.ShouldBeTrue();
        }

        return new TripSeeder(ConnectionString).SaveAsync(trip);
    }

    private Task SeedPlannedAsync(DateTime addedAt, TripCustomer? customer = null, bool deleted = false)
    {
        var trip = TripSeeder.BuildPlanned(
            TripId.New(), _mario, (customer ?? _alfa).Id, _tractor.Id, null, null, null, addedAt,
            FakeCurrentUser.ActorId);
        if (deleted)
        {
            trip.Delete(NowUtc, FakeCurrentUser.ActorId).IsSuccess.ShouldBeTrue();
        }

        return new TripSeeder(ConnectionString).SaveAsync(trip);
    }

    [Fact]
    public async Task Get_WithoutAView_ListsTheEndedTripsWithTheirNamesAndAmount()
    {
        // Arrange
        await SeedEndedAsync(
            7, Today.AddDays(-1), _beta, _luisa, _tractor, _trailer, _north, _south,
            RegistryContracts.TripMeasureKind.Weight, total: 123.45m);
        await SeedInProgressAsync(Today, new TimeOnly(9, 0));
        await SeedPlannedAsync(NowUtc);

        // Act
        var body = await GetAsync();

        // Assert
        var item = Items(body).ShouldHaveSingleItem();
        item.GetProperty("state").GetString().ShouldBe("Ended");
        item.GetProperty("number").GetInt64().ShouldBe(7);
        item.GetProperty("day").GetString().ShouldBe("2026-06-14");
        item.GetProperty("start").GetString().ShouldBe("08:00:00");
        item.GetProperty("end").GetString().ShouldBe("10:00:00");
        item.GetProperty("customer").GetProperty("companyName").GetString().ShouldBe("Beta SPA");
        item.GetProperty("pickup").GetProperty("name").GetString().ShouldBe("Deposito Nord");
        item.GetProperty("delivery").GetProperty("municipality").GetString().ShouldBe("Bari");
        item.GetProperty("driver").GetProperty("firstName").GetString().ShouldBe("Luisa");
        item.GetProperty("driver").GetProperty("lastName").GetString().ShouldBe("Bianchi");
        item.GetProperty("vehicle").GetProperty("licensePlate").GetString().ShouldBe("AA111AA");
        item.GetProperty("trailer").GetProperty("licensePlate").GetString().ShouldBe("RR333RR");
        item.GetProperty("measureKind").GetString().ShouldBe("Weight");
        item.GetProperty("quantity").GetInt32().ShouldBe(1);
        item.GetProperty("unitPrice").GetDecimal().ShouldBe(60m);
        item.GetProperty("total").GetDecimal().ShouldBe(123.45m);
        item.GetProperty("daysToExpiry").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_WithAPeriod_ListsTheEndedTripsWhoseDayIsInsideItEndsIncluded()
    {
        // Arrange
        await SeedEndedAsync(1, new DateOnly(2026, 6, 1));
        await SeedEndedAsync(2, new DateOnly(2026, 6, 2));
        await SeedEndedAsync(3, new DateOnly(2026, 6, 10));
        await SeedEndedAsync(4, new DateOnly(2026, 6, 11));

        // Act
        var body = await GetAsync("?from=2026-06-02&to=2026-06-10&sortColumn=day&sortDirection=Ascending");

        // Assert
        Numbers(body).ShouldBe([2, 3]);
        TotalCount(body).ShouldBe(2);
    }

    [Fact]
    public async Task Get_Ended_FiguresCoverTheWholeSelectionAndNotThePage()
    {
        // Arrange
        await SeedEndedAsync(1, Today, _alfa, total: 10.50m);
        await SeedEndedAsync(2, Today, _alfa, total: 20m);
        await SeedEndedAsync(3, Today, _beta, total: 30m);

        // Act
        var body = await GetAsync("?pageSize=1");

        // Assert
        Items(body).Count.ShouldBe(1);
        var figures = body.GetProperty("figures");
        figures.GetProperty("trips").GetInt32().ShouldBe(3);
        figures.GetProperty("customers").GetInt32().ShouldBe(2);
        figures.GetProperty("amount").GetDecimal().ShouldBe(60.50m);
    }

    [Fact]
    public async Task Get_Ended_FiguresFollowTheFilters()
    {
        // Arrange
        await SeedEndedAsync(1, Today, _alfa, total: 10m);
        await SeedEndedAsync(2, Today, _beta, total: 20m);

        // Act
        var body = await GetAsync($"?customerId={_beta.Id}");

        // Assert
        var figures = body.GetProperty("figures");
        figures.GetProperty("trips").GetInt32().ShouldBe(1);
        figures.GetProperty("customers").GetInt32().ShouldBe(1);
        figures.GetProperty("amount").GetDecimal().ShouldBe(20m);
    }

    [Theory]
    [InlineData("InProgress")]
    [InlineData("Planned")]
    public async Task Get_AViewOtherThanEnded_HasNoFigures(string view)
    {
        // Arrange
        await SeedInProgressAsync(Today, new TimeOnly(9, 0));
        await SeedPlannedAsync(NowUtc);

        // Act
        var body = await GetAsync($"?view={view}");

        // Assert
        body.GetProperty("figures").ValueKind.ShouldBe(JsonValueKind.Null);
        TotalCount(body).ShouldBe(1);
    }

    [Fact]
    public async Task Get_Ended_OrdersByDayNewestFirstThenByNumber()
    {
        // Arrange
        await SeedEndedAsync(1, new DateOnly(2026, 6, 10));
        await SeedEndedAsync(2, new DateOnly(2026, 6, 12));
        await SeedEndedAsync(3, new DateOnly(2026, 6, 12));
        await SeedEndedAsync(4, new DateOnly(2026, 6, 11));

        // Act
        var body = await GetAsync();

        // Assert
        Numbers(body).ShouldBe([3, 2, 4, 1]);
    }

    [Theory]
    [InlineData("?sortColumn=day&sortDirection=Ascending", new long[] { 1, 4, 2, 3 })]
    [InlineData("?sortColumn=DAY&sortDirection=Descending", new long[] { 3, 2, 4, 1 })]
    [InlineData("?sortColumn=total", new long[] { 3, 2, 1, 4 })]
    [InlineData("?sortColumn=Total&sortDirection=Descending", new long[] { 3, 2, 1, 4 })]
    [InlineData("?sortColumn=total&sortDirection=Ascending", new long[] { 4, 1, 3, 2 })]
    [InlineData("?sortColumn=unknown&sortDirection=Ascending", new long[] { 3, 2, 4, 1 })]
    public async Task Get_Ended_SortsByDayOrTotalAndAnUnknownColumnFallsBackToTheDefault(string query, long[] expected)
    {
        // Arrange: totals 30, 50, 50, 10 for numbers 1, 2, 3, 4; equal totals break on the number, descending
        await SeedEndedAsync(1, new DateOnly(2026, 6, 10), total: 30m);
        await SeedEndedAsync(2, new DateOnly(2026, 6, 12), total: 50m);
        await SeedEndedAsync(3, new DateOnly(2026, 6, 12), total: 50m);
        await SeedEndedAsync(4, new DateOnly(2026, 6, 11), total: 10m);

        // Act
        var body = await GetAsync(query);

        // Assert
        Numbers(body).ShouldBe(expected);
    }

    [Fact]
    public async Task Get_InProgress_ListsTripsInProgressNewestStartFirstWithNoMeasureOrAmount()
    {
        // Arrange
        await SeedInProgressAsync(Today, new TimeOnly(7, 0));
        await SeedInProgressAsync(Today, new TimeOnly(9, 30));
        await SeedInProgressAsync(Today.AddDays(-1), new TimeOnly(23, 0));
        await SeedInProgressAsync(Today, new TimeOnly(11, 0), deleted: true);
        await SeedEndedAsync(1, Today);
        await SeedPlannedAsync(NowUtc);

        // Act
        var body = await GetAsync("?view=InProgress");

        // Assert
        var items = Items(body);
        items.Select(i => i.GetProperty("day").GetString() + " " + i.GetProperty("start").GetString())
            .ShouldBe(["2026-06-15 09:30:00", "2026-06-15 07:00:00", "2026-06-14 23:00:00"]);
        TotalCount(body).ShouldBe(3);
        items.ShouldAllBe(i => i.GetProperty("state").GetString() == "InProgress"
            && i.GetProperty("number").ValueKind == JsonValueKind.Null
            && i.GetProperty("measureKind").ValueKind == JsonValueKind.Null
            && i.GetProperty("quantity").ValueKind == JsonValueKind.Null
            && i.GetProperty("unitPrice").ValueKind == JsonValueKind.Null
            && i.GetProperty("total").ValueKind == JsonValueKind.Null
            && i.GetProperty("nightPriceApplied").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_Planned_ListsTheTripsOldestAddedFirstWithTheirDaysToExpiry()
    {
        // Arrange
        await SeedPlannedAsync(NowUtc.AddDays(-2));
        await SeedPlannedAsync(NowUtc.AddDays(-6));
        await SeedPlannedAsync(NowUtc);

        // Act
        var body = await GetAsync("?view=Planned");

        // Assert
        var items = Items(body);
        items.Select(i => i.GetProperty("daysToExpiry").GetInt32()).ShouldBe([1, 5, 7]);
        items.ShouldAllBe(i => i.GetProperty("state").GetString() == "Planned"
            && i.GetProperty("day").ValueKind == JsonValueKind.Null
            && i.GetProperty("total").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_Planned_NeverListsNorCountsAnExpiredOrADeletedTrip()
    {
        // Arrange: seven days old is its last day, eight days old is expired
        await SeedPlannedAsync(NowUtc.AddDays(-7));
        await SeedPlannedAsync(NowUtc.AddDays(-8));
        await SeedPlannedAsync(NowUtc, deleted: true);

        // Act
        var body = await GetAsync("?view=Planned");

        // Assert
        Items(body).ShouldHaveSingleItem().GetProperty("daysToExpiry").GetInt32().ShouldBe(0);
        TotalCount(body).ShouldBe(1);
    }

    [Fact]
    public async Task Get_FilteredByAVehicle_MatchesTheLeadingVehicleAndTheTrailer()
    {
        // Arrange
        await SeedEndedAsync(1, Today, vehicle: _tractor, trailer: _trailer);
        await SeedEndedAsync(2, Today, vehicle: _otherTractor);
        await SeedEndedAsync(3, Today, vehicle: _otherTractor, trailer: _trailer);

        // Act
        var byTrailer = await GetAsync($"?vehicleId={_trailer.Id}&sortColumn=day&sortDirection=Ascending");
        var byTractor = await GetAsync($"?vehicleId={_otherTractor.Id}&sortColumn=day&sortDirection=Ascending");

        // Assert
        Numbers(byTrailer).ShouldBe([1, 3]);
        Numbers(byTractor).ShouldBe([2, 3]);
    }

    [Fact]
    public async Task Get_FilteredByALocation_MatchesThePickupAndTheDelivery()
    {
        // Arrange
        await SeedEndedAsync(1, Today, pickup: _north);
        await SeedEndedAsync(2, Today, delivery: _north);
        await SeedEndedAsync(3, Today, pickup: _south, delivery: _south);

        // Act
        var body = await GetAsync($"?locationId={_north.Id}&sortColumn=day&sortDirection=Ascending");

        // Assert
        Numbers(body).ShouldBe([1, 2]);
    }

    [Fact]
    public async Task Get_FilteredByAMode_ListsOnlyTheTripsOfThatMeasureKind()
    {
        // Arrange
        await SeedEndedAsync(1, Today, measure: RegistryContracts.TripMeasureKind.StartAndEnd);
        await SeedEndedAsync(2, Today, measure: RegistryContracts.TripMeasureKind.Weight);
        await SeedEndedAsync(3, Today, measure: RegistryContracts.TripMeasureKind.Distance);

        // Act
        var body = await GetAsync("?mode=Distance");

        // Assert
        Numbers(body).ShouldBe([3]);
    }

    [Fact]
    public async Task Get_FilteredByCustomerAndDriver_AppliesToEveryView()
    {
        // Arrange
        await SeedPlannedAsync(NowUtc, _alfa);
        await SeedPlannedAsync(NowUtc, _beta);

        // Act
        var byCustomer = await GetAsync($"?view=Planned&customerId={_beta.Id}");
        var byDriver = await GetAsync($"?view=Planned&driverId={_luisa}");

        // Assert
        TotalCount(byCustomer).ShouldBe(1);
        Items(byCustomer).ShouldHaveSingleItem().GetProperty("customer").GetProperty("companyName").GetString()
            .ShouldBe("Beta SPA");
        TotalCount(byDriver).ShouldBe(0);
    }

    [Theory]
    [InlineData("InProgress", "from=2026-06-01&to=2026-06-30")]
    [InlineData("Planned", "from=2026-06-01&to=2026-06-30")]
    [InlineData("InProgress", "mode=Weight")]
    [InlineData("Planned", "mode=Distance")]
    public async Task Get_APeriodOrAModeOutsideTheEndedView_Returns400WithErrors(string view, string filter)
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await BackOffice(factory).GetAsync($"{Url}?view={view}&{filter}", Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("errors").EnumerateObject().ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("from=2026-06-01")]
    [InlineData("to=2026-06-30")]
    [InlineData("from=2026-06-30&to=2026-06-01")]
    public async Task Get_AFromWithoutAToOrAToBeforeTheFrom_Returns400WithErrors(string period)
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await BackOffice(factory).GetAsync($"{Url}?{period}", Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("errors").EnumerateObject().ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("view=7")]
    [InlineData("mode=99")]
    public async Task Get_AViewOrAModeThatIsNotDefined_Returns400WithErrors(string filter)
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await BackOffice(factory).GetAsync($"{Url}?{filter}", Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("errors").EnumerateObject().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Get_SearchedByANumber_FindsTheTripWithThatNumberAndAsksTheRegistryOnce()
    {
        // Arrange
        await SeedEndedAsync(12, Today);
        await SeedEndedAsync(34, Today);

        // Act
        var body = await GetAsync("?search=%2034%20");

        // Assert
        Numbers(body).ShouldBe([34]);
        _registry.MatchSearches.ShouldBe(["34"]);
    }

    [Fact]
    public async Task Get_SearchedByTextTheRegistryMatches_FindsTheTripsOfTheMatchedCustomerDriverVehicleOrLocation()
    {
        // Arrange
        await SeedEndedAsync(1, Today, _alfa);
        await SeedEndedAsync(2, Today, _beta, _luisa);
        await SeedEndedAsync(3, Today, vehicle: _otherTractor, trailer: _trailer);
        await SeedEndedAsync(4, Today, pickup: _north);
        await SeedEndedAsync(5, Today, delivery: _south);
        await SeedEndedAsync(6, Today, _beta);

        // Act
        _registry.Matches = new TripSearchMatches([_alfa.Id], [_luisa], [_trailer.Id], [_north.Id, _south.Id]);
        var body = await GetAsync("?search=text&sortColumn=day&sortDirection=Ascending");

        // Assert
        Numbers(body).ShouldBe([1, 2, 3, 4, 5]);
        _registry.MatchSearches.ShouldBe(["text"]);
    }

    [Fact]
    public async Task Get_SearchedAndFiltered_FindsOnlyTheTripsThatMatchBoth()
    {
        // Arrange
        await SeedEndedAsync(1, Today, _alfa, _mario);
        await SeedEndedAsync(2, Today, _alfa, _luisa);
        _registry.Matches = new TripSearchMatches([_alfa.Id], [], [], []);

        // Act
        var body = await GetAsync($"?search=alfa&driverId={_luisa}");

        // Assert
        Numbers(body).ShouldBe([2]);
    }

    [Fact]
    public async Task Get_AWhitespaceSearch_AsksNoRegistryAndFiltersNothing()
    {
        // Arrange
        await SeedEndedAsync(1, Today);

        // Act
        var body = await GetAsync("?search=%20%20");

        // Assert
        Numbers(body).ShouldBe([1]);
        _registry.MatchSearches.ShouldBeEmpty();
    }

    [Fact]
    public async Task Get_Ended_NightPriceAppliedIsTrueOnlyWhenTheTripStartedInTheNightOfAContractWithTheSurcharge()
    {
        // Arrange
        await SeedEndedAsync(1, Today, isNight: true, nightSurcharge: true);
        await SeedEndedAsync(2, Today, isNight: true, nightSurcharge: false);
        await SeedEndedAsync(3, Today, isNight: false, nightSurcharge: true);

        // Act
        var body = await GetAsync("?sortColumn=day&sortDirection=Ascending");

        // Assert
        Items(body).Select(i => i.GetProperty("nightPriceApplied").GetBoolean()).ShouldBe([true, false, false]);
    }

    [Fact]
    public async Task Get_Paged_SplitsTheSelectionAndKeepsTheTotalCount()
    {
        // Arrange
        for (var number = 1; number <= 5; number++)
        {
            await SeedEndedAsync(number, Today);
        }

        // Act
        var first = await GetAsync("?pageSize=2&page=1");
        var last = await GetAsync("?pageSize=2&page=3");
        var beyond = await GetAsync("?pageSize=2&page=4");

        // Assert
        Numbers(first).ShouldBe([5, 4]);
        Numbers(last).ShouldBe([1]);
        Items(beyond).ShouldBeEmpty();
        TotalCount(first).ShouldBe(5);
        first.GetProperty("trips").GetProperty("totalPages").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task Get_WithNoTrips_ReturnsAnEmptyPageAndZeroFigures()
    {
        // Act
        var body = await GetAsync();

        // Assert
        Items(body).ShouldBeEmpty();
        TotalCount(body).ShouldBe(0);
        body.GetProperty("figures").GetProperty("amount").GetDecimal().ShouldBe(0m);
    }

    [Fact]
    public async Task Get_InvalidPageValues_AreCoercedToTheDefaults()
    {
        // Arrange
        await SeedEndedAsync(1, Today);

        // Act
        var body = await GetAsync("?page=0&pageSize=-5");

        // Assert
        body.GetProperty("trips").GetProperty("page").GetInt32().ShouldBe(1);
        body.GetProperty("trips").GetProperty("pageSize").GetInt32().ShouldBe(20);
        Numbers(body).ShouldBe([1]);
    }

    [Fact]
    public async Task Get_ASearchOfMoreThanAHundredCharacters_Returns400()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await BackOffice(factory).GetAsync($"{Url}?search={new string('a', 101)}", Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
