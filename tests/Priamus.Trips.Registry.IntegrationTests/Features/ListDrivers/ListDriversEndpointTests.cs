using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Priamus.Trips.Registry.Domain.Drivers;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.DriverTestSupport;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.VehicleTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Features.ListDrivers;

/// <summary>
/// Endpoint integration tests for <c>GET /api/registry/drivers</c>, against the per-test PostgreSQL
/// database and the test authentication scheme.
/// </summary>
public sealed class ListDriversEndpointTests : RegistryIntegrationTestBase
{
    private const string Url = "/api/registry/drivers";

    public ListDriversEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> CreateFactory() =>
        ApiFactory.Create(ConnectionString, useTestAuthentication: true);

    private sealed class FixedClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;

        public DateTimeOffset UtcNowOffset => new(now, TimeSpan.Zero);
    }

    private async Task SeedAsync(
        string firstName,
        string lastName,
        string? phone = null,
        Guid? userId = null,
        bool deactivated = false,
        int minute = 0)
    {
        await using var db = CreateDbContext();
        var clock = new FixedClock(new DateTime(2026, 1, 1, 10, minute, 0, DateTimeKind.Utc));
        var driver = Driver.Create(
            DriverId.New(),
            firstName,
            lastName,
            phone,
            userId is { } id ? new UserId(id) : null,
            clock,
            FakeCurrentUser.ActorId);
        if (deactivated)
        {
            driver.Deactivate(clock, FakeCurrentUser.ActorId);
        }

        db.Drivers.Add(driver);
        await db.SaveChangesAsync(Ct);
    }

    private async Task SeedSortSetAsync()
    {
        await SeedAsync("Anna", "Zeta", minute: 1);
        await SeedAsync("Carla", "Alfa", minute: 3);
        await SeedAsync("Bruno", "Alfa", minute: 2);
    }

    private async Task SeedSearchSetAsync()
    {
        await SeedAsync("Luca", "Di Febo", "333 418 2291");
        await SeedAsync("Mario", "Rossi", "+39 06 1234567", deactivated: true);
        await SeedAsync("Febo", "Verdi");
    }

    private async Task<JsonElement> GetPageAsync(string query = "")
    {
        await using var factory = CreateFactory();
        var response = await BackOfficeClient(factory).GetAsync(Url + query, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static string[] Names(JsonElement page) =>
        [.. page.GetProperty("data").EnumerateArray()
            .Select(i => $"{i.GetProperty("lastName").GetString()} {i.GetProperty("firstName").GetString()}")];

    private static string[] Split(string commaSeparated) =>
        commaSeparated.Length == 0 ? [] : commaSeparated.Split(',');

    [Fact]
    public async Task NoParameters_ReturnsEveryFieldOrderedByLastNameThenFirstNameWithTheVersion()
    {
        // Arrange
        await SeedAsync("Luca", "Zeta", "333 418 2291", deactivated: true, minute: 5);
        await SeedAsync("Anna", "Alfa");

        // Act
        var page = await GetPageAsync();

        // Assert
        Names(page).ShouldBe(["Alfa Anna", "Zeta Luca"]);
        page.GetProperty("totalCount").GetInt32().ShouldBe(2);
        var anna = page.GetProperty("data")[0];
        anna.GetProperty("phone").ValueKind.ShouldBe(JsonValueKind.Null);
        anna.GetProperty("status").GetString().ShouldBe("Active");
        anna.GetProperty("version").GetInt32().ShouldBe(0);
        var luca = page.GetProperty("data")[1];
        luca.GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);
        luca.GetProperty("phone").GetString().ShouldBe("333 418 2291");
        luca.GetProperty("status").GetString().ShouldBe("Deactivated");
        luca.GetProperty("lastModifiedAt").GetDateTime().ShouldBe(new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc));
        luca.GetProperty("version").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task TwentyOneDrivers_FirstPageHoldsTwentyAndTheSecondTheLast()
    {
        // Arrange
        for (var i = 1; i <= 21; i++)
        {
            await SeedAsync("Nome", $"Cognome {i:000}");
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
        Names(second).ShouldBe(["Cognome 021 Nome"]);
    }

    [Fact]
    public async Task EqualNamesAcrossAPageBoundary_AppearOnceEachInAStableOrder()
    {
        // Arrange
        await SeedAsync("Anna", "Alfa");
        await SeedAsync("Mario", "Gemello");
        await SeedAsync("Mario", "Gemello");
        await SeedAsync("Zoe", "Zeta");

        // Act
        var first = await GetPageAsync("?pageSize=2");
        var second = await GetPageAsync("?pageSize=2&page=2");

        // Assert
        var ids = first.GetProperty("data").EnumerateArray()
            .Concat(second.GetProperty("data").EnumerateArray())
            .Select(i => i.GetProperty("id").GetGuid())
            .ToList();
        ids.Distinct().Count().ShouldBe(4);
        Names(first).ShouldBe(["Alfa Anna", "Gemello Mario"]);
        Names(second).ShouldBe(["Gemello Mario", "Zeta Zoe"]);
        var twins = new[] { first.GetProperty("data")[1], second.GetProperty("data")[0] }
            .Select(i => i.GetProperty("id").GetGuid()).ToList();
        twins.ShouldBe([.. twins.Order()]);
    }

    [Theory]
    [InlineData(1, "Cognome 1 Nome,Cognome 2 Nome")]
    [InlineData(2, "Cognome 3 Nome,Cognome 4 Nome")]
    [InlineData(3, "Cognome 5 Nome")]
    public async Task MultiplePages_FirstMiddleAndLastHoldTheirSlice(int page, string expected)
    {
        // Arrange
        for (var i = 1; i <= 5; i++)
        {
            await SeedAsync("Nome", $"Cognome {i}");
        }

        // Act
        var result = await GetPageAsync($"?page={page}&pageSize=2");

        // Assert
        Names(result).ShouldBe(Split(expected));
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
    [InlineData("name", "Ascending", "Alfa Bruno,Alfa Carla,Zeta Anna")]
    [InlineData("name", "Descending", "Zeta Anna,Alfa Carla,Alfa Bruno")]
    [InlineData("lastModifiedAt", "Ascending", "Zeta Anna,Alfa Bruno,Alfa Carla")]
    [InlineData("lastModifiedAt", "Descending", "Alfa Carla,Alfa Bruno,Zeta Anna")]
    public async Task EverySortColumn_SortsInBothDirections(string column, string direction, string expected)
    {
        // Arrange
        await SeedSortSetAsync();

        // Act
        var page = await GetPageAsync($"?sortColumn={column}&sortDirection={direction}");

        // Assert
        Names(page).ShouldBe(Split(expected));
    }

    [Theory]
    [InlineData("phone")]
    [InlineData("unknown")]
    [InlineData("status")]
    public async Task ColumnOutsideTheWhitelist_FallsBackToNameAscendingEvenWhenDescendingIsAsked(string column)
    {
        // Arrange
        await SeedSortSetAsync();

        // Act
        var page = await GetPageAsync($"?sortColumn={column}&sortDirection=Descending");

        // Assert
        Names(page).ShouldBe(["Alfa Bruno", "Alfa Carla", "Zeta Anna"]);
    }

    [Theory]
    [InlineData("febo", "Di Febo Luca,Verdi Febo")]
    [InlineData("FEBO", "Di Febo Luca,Verdi Febo")]
    [InlineData("mari", "Rossi Mario")]
    [InlineData("3334182", "Di Febo Luca")]
    [InlineData("333 4182", "Di Febo Luca")]
    [InlineData("39 06", "Rossi Mario")]
    [InlineData("+3906", "Rossi Mario")]
    [InlineData("zzzz", "")]
    [InlineData("   ", "Di Febo Luca,Rossi Mario,Verdi Febo")]
    public async Task Search_MatchesNamesIgnoringCaseOrThePhoneWithoutSpaces(string search, string expected)
    {
        // Arrange
        await SeedSearchSetAsync();

        // Act
        var page = await GetPageAsync($"?search={Uri.EscapeDataString(search)}");

        // Assert
        Names(page).ShouldBe(Split(expected));
        page.GetProperty("totalCount").GetInt32().ShouldBe(Split(expected).Length);
    }

    [Theory]
    [InlineData("", "Di Febo Luca,Rossi Mario,Verdi Febo")]
    [InlineData("?status=Active", "Di Febo Luca,Verdi Febo")]
    [InlineData("?status=Deactivated", "Rossi Mario")]
    [InlineData("?search=febo&status=Active", "Di Febo Luca,Verdi Febo")]
    [InlineData("?search=febo&status=Deactivated", "")]
    public async Task StatusFilter_AppliesAloneAndTogetherWithTheSearch(string query, string expected)
    {
        // Arrange
        await SeedSearchSetAsync();

        // Act
        var page = await GetPageAsync(query);

        // Assert
        Names(page).ShouldBe(Split(expected));
        page.GetProperty("totalCount").GetInt32().ShouldBe(Split(expected).Length);
    }

    [Fact]
    public async Task ListedDriver_CarriesTheAccountReadAtRequestTime()
    {
        // Arrange
        await using var factory = CreateFactory();
        var userId = await CreateAccountAsync(factory, "luca.difebo");
        await SeedAsync("Luca", "Di Febo", userId: userId);
        await SeedAsync("Anna", "Alfa");
        var client = BackOfficeClient(factory);

        // Act
        var before = await (await client.GetAsync(Url, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        var disable = await AdminClient(factory).PostAsJsonAsync(
            $"/api/identity/users/{userId}/disable", new { version = 0 }, TestJson.Options, Ct);
        var after = await (await client.GetAsync(Url, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);

        // Assert
        disable.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        before.GetProperty("data")[0].GetProperty("account").ValueKind.ShouldBe(JsonValueKind.Null);
        var linkedBefore = before.GetProperty("data")[1].GetProperty("account");
        linkedBefore.GetProperty("userId").GetGuid().ShouldBe(userId);
        linkedBefore.GetProperty("username").GetString().ShouldBe("luca.difebo");
        linkedBefore.GetProperty("isDisabled").GetBoolean().ShouldBeFalse();
        after.GetProperty("data")[1].GetProperty("account").GetProperty("isDisabled").GetBoolean().ShouldBeTrue();
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
    public async Task AdminCaller_Returns200()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await AdminClient(factory).GetAsync(Url, Ct);

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
