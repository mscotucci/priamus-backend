using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.VehicleTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Features.ListLocations;

/// <summary>
/// Endpoint integration tests for <c>GET /api/registry/locations</c>, against the per-test PostgreSQL
/// database and the test authentication scheme.
/// </summary>
public sealed class ListLocationsEndpointTests : RegistryIntegrationTestBase
{
    private const string Url = "/api/registry/locations";

    public ListLocationsEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> CreateFactory() =>
        ApiFactory.Create(ConnectionString, useTestAuthentication: true);

    private sealed class FixedClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;

        public DateTimeOffset UtcNowOffset => new(now, TimeSpan.Zero);
    }

    private async Task<Guid> SeedAsync(
        string name,
        string municipality = "Pescara",
        string? address = null,
        bool deactivated = false,
        int minute = 0)
    {
        await using var db = CreateDbContext();
        var clock = new FixedClock(new DateTime(2026, 1, 1, 10, minute, 0, DateTimeKind.Utc));
        var location = Location.Create(LocationId.New(), name, municipality, address, clock, FakeCurrentUser.ActorId);
        if (deactivated)
        {
            location.Deactivate(clock, FakeCurrentUser.ActorId);
        }

        db.Locations.Add(location);
        await db.SaveChangesAsync(Ct);
        return location.Id.Value;
    }

    // Name, municipality, last change: the three columns order the rows differently.
    private async Task SeedSortSetAsync()
    {
        await SeedAsync("Zeta", "Atri", minute: 1);
        await SeedAsync("Alfa", "Teramo", minute: 3);
        await SeedAsync("Mike", "Chieti", minute: 2);
    }

    private async Task SeedSearchSetAsync()
    {
        await SeedAsync("Magazzino centrale", "Spoltore", "Via Roma 1");
        await SeedAsync("Cantiere Nord", "Spoltore", deactivated: true);
        await SeedAsync("Discarica", "Pescara", "Strada dei Colli 7");
        await SeedAsync("Impianto", "Chieti", "Contrada Spoltore Alta");
    }

    private async Task<JsonElement> GetPageAsync(string query = "")
    {
        await using var factory = CreateFactory();
        var response = await BackOfficeClient(factory).GetAsync(Url + query, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static string[] Labels(JsonElement page) =>
        [.. page.GetProperty("data").EnumerateArray()
            .Select(i => $"{i.GetProperty("name").GetString()}, {i.GetProperty("municipality").GetString()}")];

    private static string[] Split(string pipeSeparated) =>
        pipeSeparated.Length == 0 ? [] : pipeSeparated.Split('|');

    [Fact]
    public async Task NoParameters_ReturnsEveryFieldWithTheVersion()
    {
        // Arrange
        await SeedAsync("Zeta", "Atri", "Via Roma 1", deactivated: true, minute: 5);
        await SeedAsync("Alfa", "Teramo");

        // Act
        var page = await GetPageAsync();

        // Assert
        Labels(page).ShouldBe(["Alfa, Teramo", "Zeta, Atri"]);
        page.GetProperty("totalCount").GetInt32().ShouldBe(2);
        var alfa = page.GetProperty("data")[0];
        alfa.GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);
        alfa.GetProperty("address").ValueKind.ShouldBe(JsonValueKind.Null);
        alfa.GetProperty("status").GetString().ShouldBe("Active");
        alfa.GetProperty("version").GetInt32().ShouldBe(0);
        var zeta = page.GetProperty("data")[1];
        zeta.GetProperty("address").GetString().ShouldBe("Via Roma 1");
        zeta.GetProperty("status").GetString().ShouldBe("Deactivated");
        zeta.GetProperty("lastModifiedAt").GetDateTime().ShouldBe(new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc));
        zeta.GetProperty("version").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task TwentyOneLocations_FirstPageHoldsTwentyAndTheSecondTheLast()
    {
        // Arrange
        for (var i = 1; i <= 21; i++)
        {
            await SeedAsync($"Luogo {i:000}");
        }

        // Act
        var first = await GetPageAsync();
        var second = await GetPageAsync("?page=2");

        // Assert
        first.GetProperty("data").GetArrayLength().ShouldBe(20);
        first.GetProperty("totalCount").GetInt32().ShouldBe(21);
        first.GetProperty("totalPages").GetInt32().ShouldBe(2);
        first.GetProperty("pageSize").GetInt32().ShouldBe(20);
        first.GetProperty("page").GetInt32().ShouldBe(1);
        Labels(second).ShouldBe(["Luogo 021, Pescara"]);
    }

    [Fact]
    public async Task NoSortParameters_OrdersByNameThenMunicipalityAmongEqualNames()
    {
        // Arrange
        await SeedAsync("Magazzino", "Teramo");
        await SeedAsync("Magazzino", "Atri");
        await SeedAsync("Discarica", "Zuccarello");

        // Act
        var page = await GetPageAsync();

        // Assert
        Labels(page).ShouldBe(["Discarica, Zuccarello", "Magazzino, Atri", "Magazzino, Teramo"]);
    }

    [Fact]
    public async Task SortByMunicipalityDescending_InvertsTheWholeKey()
    {
        // Arrange
        await SeedAsync("Magazzino", "Teramo");
        await SeedAsync("Magazzino", "Atri");
        await SeedAsync("Cantiere", "Atri");

        // Act
        var ascending = await GetPageAsync("?sortColumn=municipality");
        var descending = await GetPageAsync("?sortColumn=municipality&sortDirection=Descending");

        // Assert
        Labels(ascending).ShouldBe(["Cantiere, Atri", "Magazzino, Atri", "Magazzino, Teramo"]);
        Labels(descending).ShouldBe(["Magazzino, Teramo", "Magazzino, Atri", "Cantiere, Atri"]);
    }

    [Theory]
    [InlineData("name", "Ascending", "Alfa, Teramo|Mike, Chieti|Zeta, Atri")]
    [InlineData("name", "Descending", "Zeta, Atri|Mike, Chieti|Alfa, Teramo")]
    [InlineData("municipality", "Ascending", "Zeta, Atri|Mike, Chieti|Alfa, Teramo")]
    [InlineData("municipality", "Descending", "Alfa, Teramo|Mike, Chieti|Zeta, Atri")]
    [InlineData("lastModifiedAt", "Ascending", "Zeta, Atri|Mike, Chieti|Alfa, Teramo")]
    [InlineData("lastModifiedAt", "Descending", "Alfa, Teramo|Mike, Chieti|Zeta, Atri")]
    [InlineData("LASTMODIFIEDAT", "Descending", "Alfa, Teramo|Mike, Chieti|Zeta, Atri")]
    public async Task EverySortColumn_SortsInBothDirections(string column, string direction, string expected)
    {
        // Arrange
        await SeedSortSetAsync();

        // Act
        var page = await GetPageAsync($"?sortColumn={column}&sortDirection={direction}");

        // Assert
        Labels(page).ShouldBe(Split(expected));
    }

    [Theory]
    [InlineData("address")]
    [InlineData("status")]
    [InlineData("unknown")]
    public async Task ColumnOutsideTheWhitelist_FallsBackToTheDefaultOrderEvenWhenDescendingIsAsked(string column)
    {
        // Arrange
        await SeedSortSetAsync();

        // Act
        var page = await GetPageAsync($"?sortColumn={column}&sortDirection=Descending");

        // Assert
        Labels(page).ShouldBe(["Alfa, Teramo", "Mike, Chieti", "Zeta, Atri"]);
    }

    [Fact]
    public async Task EqualNamesAcrossAPageBoundary_AppearOnceEachInAStableOrder()
    {
        // Arrange
        await SeedAsync("Alfa", "Atri");
        await SeedAsync("Gemello", "Chieti");
        await SeedAsync("Gemello", "Atri");
        await SeedAsync("Zeta", "Atri");

        // Act
        var first = await GetPageAsync("?pageSize=2");
        var second = await GetPageAsync("?pageSize=2&page=2");

        // Assert
        var items = first.GetProperty("data").EnumerateArray()
            .Concat(second.GetProperty("data").EnumerateArray())
            .ToList();
        items.Select(i => i.GetProperty("id").GetGuid()).Distinct().Count().ShouldBe(4);
        items.Select(i => $"{i.GetProperty("name").GetString()}, {i.GetProperty("municipality").GetString()}")
            .ShouldBe(["Alfa, Atri", "Gemello, Atri", "Gemello, Chieti", "Zeta, Atri"]);
    }

    [Fact]
    public async Task LocationsEqualOnTheSortedColumnAcrossAPageBoundary_KeepOneOrderAndAppearOnce()
    {
        // Arrange
        var seeded = new List<Guid>();
        for (var i = 1; i <= 13; i++)
        {
            seeded.Add(await SeedAsync($"Luogo {i}", minute: 7));
        }

        // Act
        var ids = new List<Guid>();
        for (var page = 1; page <= 5; page++)
        {
            var result = await GetPageAsync($"?sortColumn=lastModifiedAt&pageSize=3&page={page}");
            ids.AddRange(result.GetProperty("data").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));
        }

        // Assert: the database orders uuid byte by byte, which is the ordinal order of the lowercase text form.
        ids.Count.ShouldBe(13);
        ids.Distinct().Count().ShouldBe(13);
        ids.ShouldBe([.. seeded.OrderBy(id => id.ToString("D"), StringComparer.Ordinal)]);
    }

    [Theory]
    [InlineData("spolt", "Cantiere Nord, Spoltore|Impianto, Chieti|Magazzino centrale, Spoltore")]
    [InlineData("SPOLT", "Cantiere Nord, Spoltore|Impianto, Chieti|Magazzino centrale, Spoltore")]
    [InlineData("  spolt  ", "Cantiere Nord, Spoltore|Impianto, Chieti|Magazzino centrale, Spoltore")]
    [InlineData("magazzino", "Magazzino centrale, Spoltore")]
    [InlineData("colli", "Discarica, Pescara")]
    [InlineData("zzzz", "")]
    [InlineData("   ", "Cantiere Nord, Spoltore|Discarica, Pescara|Impianto, Chieti|Magazzino centrale, Spoltore")]
    public async Task Search_MatchesNameMunicipalityOrAddressIgnoringCase(string search, string expected)
    {
        // Arrange
        await SeedSearchSetAsync();

        // Act
        var page = await GetPageAsync($"?search={Uri.EscapeDataString(search)}");

        // Assert
        Labels(page).ShouldBe(Split(expected));
        page.GetProperty("totalCount").GetInt32().ShouldBe(Split(expected).Length);
    }

    [Fact]
    public async Task SearchMatchingTheMunicipalityOnly_FindsBothLocationsOfTheTown()
    {
        // Arrange
        await SeedSearchSetAsync();

        // Act
        var page = await GetPageAsync("?search=spoltore&status=Active");

        // Assert
        Labels(page).ShouldBe(["Impianto, Chieti", "Magazzino centrale, Spoltore"]);
    }

    [Fact]
    public async Task SearchMatchingTheAddressOnly_FindsTheLocation()
    {
        // Arrange
        await SeedAsync("Discarica", "Pescara", "Strada dei Colli 7");
        await SeedAsync("Impianto", "Chieti");

        // Act
        var page = await GetPageAsync("?search=strada");

        // Assert
        Labels(page).ShouldBe(["Discarica, Pescara"]);
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

    [Theory]
    [InlineData("", "Cantiere Nord, Spoltore|Discarica, Pescara|Impianto, Chieti|Magazzino centrale, Spoltore")]
    [InlineData("?status=Active", "Discarica, Pescara|Impianto, Chieti|Magazzino centrale, Spoltore")]
    [InlineData("?status=Deactivated", "Cantiere Nord, Spoltore")]
    [InlineData("?search=spoltore&status=Deactivated", "Cantiere Nord, Spoltore")]
    [InlineData("?search=discarica&status=Deactivated", "")]
    public async Task StatusFilter_AppliesAloneAndTogetherWithTheSearch(string query, string expected)
    {
        // Arrange
        await SeedSearchSetAsync();

        // Act
        var page = await GetPageAsync(query);

        // Assert
        Labels(page).ShouldBe(Split(expected));
        page.GetProperty("totalCount").GetInt32().ShouldBe(Split(expected).Length);
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
}
