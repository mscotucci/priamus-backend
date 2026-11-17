using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.Domain.Vehicles.ValueObjects;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.VehicleTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Features.ListVehicles;

/// <summary>
/// Endpoint integration tests for <c>GET /api/registry/vehicles</c>, against the per-test PostgreSQL
/// database and the test authentication scheme.
/// </summary>
public sealed class ListVehiclesEndpointTests : RegistryIntegrationTestBase
{
    private const string Url = "/api/registry/vehicles";

    public ListVehiclesEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> CreateFactory() =>
        ApiFactory.Create(ConnectionString, useTestAuthentication: true);

    private sealed class FixedClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;

        public DateTimeOffset UtcNowOffset => new(now, TimeSpan.Zero);
    }

    private async Task SeedAsync(
        string plate,
        string description = "Iveco Stralis 480",
        VehicleType type = VehicleType.Tractor,
        bool decommissioned = false,
        int minute = 0)
    {
        await using var db = CreateDbContext();
        var clock = new FixedClock(new DateTime(2026, 1, 1, 10, minute, 0, DateTimeKind.Utc));
        var vehicle = Vehicle.Create(
            VehicleId.New(), LicensePlate.Create(plate), description, type, false, clock, FakeCurrentUser.ActorId);
        if (decommissioned)
        {
            vehicle.Decommission(clock, FakeCurrentUser.ActorId);
        }

        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(Ct);
    }

    private async Task SeedSortSetAsync()
    {
        await SeedAsync("AA111AA", "Zeta", VehicleType.Tractor, false, minute: 3);
        await SeedAsync("BB222BB", "Alfa", VehicleType.Trailer, true, minute: 1);
        await SeedAsync("CC333CC", "Mike", VehicleType.StandaloneVehicle, false, minute: 2);
    }

    private async Task SeedSearchSetAsync()
    {
        await SeedAsync("AB456CD", "Iveco Stralis 480", VehicleType.Tractor);
        await SeedAsync("XY789ZZ", "Volvo FH", VehicleType.Tractor, decommissioned: true);
        await SeedAsync("TR100", "Cassone ribaltabile", VehicleType.Trailer);
        await SeedAsync("ST200", "Furgone Stralis", VehicleType.StandaloneVehicle);
    }

    private async Task<JsonElement> GetPageAsync(string query = "")
    {
        await using var factory = CreateFactory();
        var response = await BackOfficeClient(factory).GetAsync(Url + query, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static string[] Plates(JsonElement page) =>
        [.. page.GetProperty("data").EnumerateArray().Select(i => i.GetProperty("licensePlate").GetString()!)];

    private static string[] Split(string commaSeparated) =>
        commaSeparated.Length == 0 ? [] : commaSeparated.Split(',');

    [Fact]
    public async Task NoParameters_ReturnsEveryFieldOrderedByPlateAscendingWithTheVersion()
    {
        // Arrange
        await SeedAsync("CC333CC", "Mike", VehicleType.StandaloneVehicle);
        await SeedAsync("AA111AA", "Zeta", VehicleType.Tractor, decommissioned: true, minute: 5);

        // Act
        var page = await GetPageAsync();

        // Assert
        Plates(page).ShouldBe(["AA111AA", "CC333CC"]);
        page.GetProperty("totalCount").GetInt32().ShouldBe(2);
        page.GetProperty("pageSize").GetInt32().ShouldBe(20);
        var first = page.GetProperty("data")[0];
        first.GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);
        first.GetProperty("description").GetString().ShouldBe("Zeta");
        first.GetProperty("type").GetString().ShouldBe("Tractor");
        first.GetProperty("requiresTrailer").GetBoolean().ShouldBeFalse();
        first.GetProperty("status").GetString().ShouldBe("Decommissioned");
        first.GetProperty("lastModifiedAt").GetDateTime().ShouldBe(new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc));
        first.GetProperty("version").GetInt32().ShouldBe(1);
        page.GetProperty("data")[1].GetProperty("version").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task TwentyOneVehicles_FirstPageHoldsTwentyAndTheSecondTheLast()
    {
        // Arrange
        for (var i = 1; i <= 21; i++)
        {
            await SeedAsync($"PL{i:000}");
        }

        // Act
        var first = await GetPageAsync();
        var second = await GetPageAsync("?page=2");

        // Assert
        first.GetProperty("data").GetArrayLength().ShouldBe(20);
        first.GetProperty("totalCount").GetInt32().ShouldBe(21);
        first.GetProperty("totalPages").GetInt32().ShouldBe(2);
        Plates(second).ShouldBe(["PL021"]);
    }

    [Theory]
    [InlineData(1, "PL001,PL002")]
    [InlineData(2, "PL003,PL004")]
    [InlineData(3, "PL005")]
    public async Task MultiplePages_FirstMiddleAndLastHoldTheirSlice(int page, string expected)
    {
        // Arrange
        for (var i = 1; i <= 5; i++)
        {
            await SeedAsync($"PL{i:000}");
        }

        // Act
        var result = await GetPageAsync($"?page={page}&pageSize=2");

        // Assert
        Plates(result).ShouldBe(Split(expected));
        result.GetProperty("totalCount").GetInt32().ShouldBe(5);
        result.GetProperty("page").GetInt32().ShouldBe(page);
    }

    [Fact]
    public async Task EmptyRegistry_ReturnsAnEmptyPage()
    {
        // Act
        var page = await GetPageAsync();

        // Assert
        page.GetProperty("data").GetArrayLength().ShouldBe(0);
        page.GetProperty("totalCount").GetInt32().ShouldBe(0);
    }

    [Theory]
    [InlineData("?page=0", 1, 20)]
    [InlineData("?page=-3", 1, 20)]
    [InlineData("?pageSize=0", 1, 20)]
    [InlineData("?pageSize=-5", 1, 20)]
    [InlineData("?pageSize=1000", 1, 100)]
    public async Task InvalidPageValues_AreCoercedToTheDefaultsOrTheMaximum(string query, int expectedPage, int expectedSize)
    {
        // Arrange
        await SeedAsync("AA111AA");

        // Act
        var page = await GetPageAsync(query);

        // Assert
        page.GetProperty("page").GetInt32().ShouldBe(expectedPage);
        page.GetProperty("pageSize").GetInt32().ShouldBe(expectedSize);
        Plates(page).ShouldBe(["AA111AA"]);
    }

    [Theory]
    [InlineData("licensePlate", "Ascending", "AA111AA,BB222BB,CC333CC")]
    [InlineData("licensePlate", "Descending", "CC333CC,BB222BB,AA111AA")]
    [InlineData("description", "Ascending", "BB222BB,CC333CC,AA111AA")]
    [InlineData("description", "Descending", "AA111AA,CC333CC,BB222BB")]
    [InlineData("type", "Ascending", "CC333CC,AA111AA,BB222BB")]
    [InlineData("type", "Descending", "BB222BB,AA111AA,CC333CC")]
    [InlineData("status", "Ascending", "AA111AA,CC333CC,BB222BB")]
    [InlineData("status", "Descending", "BB222BB,AA111AA,CC333CC")]
    [InlineData("lastModifiedAt", "Ascending", "BB222BB,CC333CC,AA111AA")]
    [InlineData("lastModifiedAt", "Descending", "AA111AA,CC333CC,BB222BB")]
    public async Task EverySortColumn_SortsInBothDirectionsWithThePlateBreakingTies(string column, string direction, string expected)
    {
        // Arrange
        await SeedSortSetAsync();

        // Act
        var page = await GetPageAsync($"?sortColumn={column}&sortDirection={direction}");

        // Assert
        Plates(page).ShouldBe(Split(expected));
    }

    [Fact]
    public async Task SortColumnName_IsCaseInsensitive()
    {
        // Arrange
        await SeedSortSetAsync();

        // Act
        var page = await GetPageAsync("?sortColumn=DESCRIPTION&sortDirection=Descending");

        // Assert
        Plates(page).ShouldBe(["AA111AA", "CC333CC", "BB222BB"]);
    }

    [Theory]
    [InlineData("requiresTrailer")]
    [InlineData("unknown")]
    public async Task ColumnOutsideTheWhitelist_FallsBackToPlateAscendingEvenWhenDescendingIsAsked(string column)
    {
        // Arrange
        await SeedSortSetAsync();

        // Act
        var page = await GetPageAsync($"?sortColumn={column}&sortDirection=Descending");

        // Assert
        Plates(page).ShouldBe(["AA111AA", "BB222BB", "CC333CC"]);
    }

    [Theory]
    [InlineData("ab 456", "AB456CD")]
    [InlineData("ab-456", "AB456CD")]
    [InlineData("ab456cd", "AB456CD")]
    [InlineData("  AB 4  ", "AB456CD")]
    [InlineData("zz", "XY789ZZ")]
    [InlineData("stralis", "AB456CD,ST200")]
    [InlineData("STRALIS", "AB456CD,ST200")]
    [InlineData("volvo fh", "XY789ZZ")]
    [InlineData("cassone", "TR100")]
    [InlineData("iveco 480", "")]
    [InlineData("-", "")]
    [InlineData("   ", "AB456CD,ST200,TR100,XY789ZZ")]
    public async Task Search_MatchesThePlateNormalisedOrTheDescriptionIgnoringCase(string search, string expected)
    {
        // Arrange
        await SeedSearchSetAsync();

        // Act
        var page = await GetPageAsync($"?search={Uri.EscapeDataString(search)}");

        // Assert
        Plates(page).ShouldBe(Split(expected));
        page.GetProperty("totalCount").GetInt32().ShouldBe(Split(expected).Length);
    }

    [Theory]
    [InlineData("?type=Tractor", "AB456CD,XY789ZZ")]
    [InlineData("?type=Trailer", "TR100")]
    [InlineData("?status=Decommissioned", "XY789ZZ")]
    [InlineData("?status=Active", "AB456CD,ST200,TR100")]
    [InlineData("?type=Tractor&status=Active", "AB456CD")]
    [InlineData("?search=stralis&type=Tractor", "AB456CD")]
    [InlineData("?search=stralis&status=Active", "AB456CD,ST200")]
    [InlineData("?search=stralis&status=Decommissioned", "")]
    public async Task TypeAndStatusFilters_ApplyAloneAndTogetherWithTheSearch(string query, string expected)
    {
        // Arrange
        await SeedSearchSetAsync();

        // Act
        var page = await GetPageAsync(query);

        // Assert
        Plates(page).ShouldBe(Split(expected));
        page.GetProperty("totalCount").GetInt32().ShouldBe(Split(expected).Length);
    }

    [Fact]
    public async Task SearchOverOneHundredCharacters_Returns400()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await BackOfficeClient(factory).GetAsync($"{Url}?search={new string('a', 101)}", Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(["Search"], ignoreOrder: true);
    }

    [Fact]
    public async Task SearchOfExactlyOneHundredCharacters_Returns200()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await BackOfficeClient(factory).GetAsync($"{Url}?search={new string('a', 100)}", Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminCaller_Returns200()
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = TestCaller.WithRole("Admin").ApplyTo(factory.CreateClient());

        // Act
        var response = await client.GetAsync(Url, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DriverCaller_Returns403()
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = TestCaller.WithRole("Driver").ApplyTo(factory.CreateClient());

        // Act
        var response = await client.GetAsync(Url, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnonymousCaller_Returns401()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await factory.CreateClient().GetAsync(Url, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
