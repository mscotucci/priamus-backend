using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Domain.Customers.ValueObjects;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.CustomerTestSupport;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.VehicleTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Features.ListCustomers;

/// <summary>
/// Endpoint integration tests for <c>GET /api/registry/customers</c>, against the per-test PostgreSQL
/// database and the test authentication scheme.
/// </summary>
public sealed class ListCustomersEndpointTests : RegistryIntegrationTestBase
{
    private const string Url = "/api/registry/customers";

    private const string PenneVat = VatNumberA; // 01455280683
    private const string PenneTax = "MRCGLI72D45G438K";

    public ListCustomersEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> CreateFactory() =>
        ApiFactory.Create(ConnectionString, useTestAuthentication: true);

    private sealed class FixedClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;

        public DateTimeOffset UtcNowOffset => new(now, TimeSpan.Zero);
    }

    private async Task SeedAsync(
        string companyName,
        string? vatNumber = null,
        string? taxCode = null,
        string? address = null,
        bool deactivated = false,
        int minute = 0)
    {
        await using var db = CreateDbContext();
        var clock = new FixedClock(new DateTime(2026, 1, 1, 10, minute, 0, DateTimeKind.Utc));
        var customer = Customer.Create(
            CustomerId.New(),
            companyName,
            vatNumber is null ? null : VatNumber.Create(vatNumber),
            taxCode is null ? null : TaxCode.Create(taxCode),
            address,
            clock,
            FakeCurrentUser.ActorId);
        if (deactivated)
        {
            customer.Deactivate(clock, FakeCurrentUser.ActorId);
        }

        db.Customers.Add(customer);
        await db.SaveChangesAsync(Ct);
    }

    private async Task SeedSortSetAsync()
    {
        await SeedAsync("Zeta Srl", minute: 1);
        await SeedAsync("Alfa Spa", minute: 3);
        await SeedAsync("Mike Snc", minute: 2);
    }

    private async Task SeedSearchSetAsync()
    {
        await SeedAsync("Comune di Penne", PenneVat, PenneTax);
        await SeedAsync("Acme Srl", VatNumberB, "12345678901", deactivated: true);
        await SeedAsync("Foreign Ltd");
        await SeedAsync("Penne Rigate Spa", null, "ABCDEF12G34H567I");
    }

    private async Task<JsonElement> GetPageAsync(string query = "")
    {
        await using var factory = CreateFactory();
        var response = await BackOfficeClient(factory).GetAsync(Url + query, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static string[] Names(JsonElement page) =>
        [.. page.GetProperty("data").EnumerateArray().Select(i => i.GetProperty("companyName").GetString()!)];

    private static string[] Split(string commaSeparated) =>
        commaSeparated.Length == 0 ? [] : commaSeparated.Split(',');

    [Fact]
    public async Task NoParameters_ReturnsEveryFieldOrderedByCompanyNameAscendingWithTheVersion()
    {
        // Arrange
        await SeedAsync("Zeta Srl", PenneVat, PenneTax, "Via Roma 1, Penne", deactivated: true, minute: 5);
        await SeedAsync("Alfa Spa");

        // Act
        var page = await GetPageAsync();

        // Assert
        Names(page).ShouldBe(["Alfa Spa", "Zeta Srl"]);
        page.GetProperty("totalCount").GetInt32().ShouldBe(2);
        var alfa = page.GetProperty("data")[0];
        alfa.GetProperty("vatNumber").ValueKind.ShouldBe(JsonValueKind.Null);
        alfa.GetProperty("taxCode").ValueKind.ShouldBe(JsonValueKind.Null);
        alfa.GetProperty("address").ValueKind.ShouldBe(JsonValueKind.Null);
        alfa.GetProperty("status").GetString().ShouldBe("Active");
        alfa.GetProperty("version").GetInt32().ShouldBe(0);
        var zeta = page.GetProperty("data")[1];
        zeta.GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);
        zeta.GetProperty("vatNumber").GetString().ShouldBe(PenneVat);
        zeta.GetProperty("taxCode").GetString().ShouldBe(PenneTax);
        zeta.GetProperty("address").GetString().ShouldBe("Via Roma 1, Penne");
        zeta.GetProperty("status").GetString().ShouldBe("Deactivated");
        zeta.GetProperty("lastModifiedAt").GetDateTime().ShouldBe(new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc));
        zeta.GetProperty("version").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task TwentyOneCustomers_FirstPageHoldsTwentyAndTheSecondTheLast()
    {
        // Arrange
        for (var i = 1; i <= 21; i++)
        {
            await SeedAsync($"Cliente {i:000}");
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
        Names(second).ShouldBe(["Cliente 021"]);
    }

    [Fact]
    public async Task EqualCompanyNamesAcrossAPageBoundary_AppearOnceEachInAStableOrder()
    {
        // Arrange
        await SeedAsync("Alfa Spa");
        await SeedAsync("Gemello Srl", VatNumberA);
        await SeedAsync("Gemello Srl", VatNumberB);
        await SeedAsync("Zeta Srl");

        // Act
        var first = await GetPageAsync("?pageSize=2");
        var second = await GetPageAsync("?pageSize=2&page=2");

        // Assert
        var ids = first.GetProperty("data").EnumerateArray()
            .Concat(second.GetProperty("data").EnumerateArray())
            .Select(i => i.GetProperty("id").GetGuid())
            .ToList();
        ids.Distinct().Count().ShouldBe(4);
        Names(first).ShouldBe(["Alfa Spa", "Gemello Srl"]);
        Names(second).ShouldBe(["Gemello Srl", "Zeta Srl"]);
        var twins = new[] { first.GetProperty("data")[1], second.GetProperty("data")[0] }
            .Select(i => i.GetProperty("id").GetGuid()).ToList();
        twins.ShouldBe([.. twins.Order()]);
    }

    [Theory]
    [InlineData(1, "Cliente 1,Cliente 2")]
    [InlineData(2, "Cliente 3,Cliente 4")]
    [InlineData(3, "Cliente 5")]
    public async Task MultiplePages_FirstMiddleAndLastHoldTheirSlice(int page, string expected)
    {
        // Arrange
        for (var i = 1; i <= 5; i++)
        {
            await SeedAsync($"Cliente {i}");
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
    [InlineData("companyName", "Ascending", "Alfa Spa,Mike Snc,Zeta Srl")]
    [InlineData("companyName", "Descending", "Zeta Srl,Mike Snc,Alfa Spa")]
    [InlineData("lastModifiedAt", "Ascending", "Zeta Srl,Mike Snc,Alfa Spa")]
    [InlineData("lastModifiedAt", "Descending", "Alfa Spa,Mike Snc,Zeta Srl")]
    public async Task EverySortColumn_SortsInBothDirections(string column, string direction, string expected)
    {
        // Arrange
        await SeedSortSetAsync();

        // Act
        var page = await GetPageAsync($"?sortColumn={column}&sortDirection={direction}");

        // Assert
        Names(page).ShouldBe(Split(expected));
    }

    [Fact]
    public async Task SortColumnName_IsCaseInsensitive()
    {
        // Arrange
        await SeedSortSetAsync();

        // Act
        var page = await GetPageAsync("?sortColumn=COMPANYNAME&sortDirection=Descending");

        // Assert
        Names(page).ShouldBe(["Zeta Srl", "Mike Snc", "Alfa Spa"]);
    }

    [Theory]
    [InlineData("vatNumber")]
    [InlineData("unknown")]
    [InlineData("status")]
    [InlineData("address")]
    public async Task ColumnOutsideTheWhitelist_FallsBackToCompanyNameAscendingEvenWhenDescendingIsAsked(string column)
    {
        // Arrange
        await SeedSortSetAsync();

        // Act
        var page = await GetPageAsync($"?sortColumn={column}&sortDirection=Descending");

        // Assert
        Names(page).ShouldBe(["Alfa Spa", "Mike Snc", "Zeta Srl"]);
    }

    [Theory]
    [InlineData("penne", "Comune di Penne,Penne Rigate Spa")]
    [InlineData("PENNE", "Comune di Penne,Penne Rigate Spa")]
    [InlineData("IT 0145", "Comune di Penne")]
    [InlineData("it01455", "Comune di Penne")]
    [InlineData("0145", "Comune di Penne")]
    [InlineData("1234", "Acme Srl")]
    [InlineData("mrcgli", "Comune di Penne")]
    [InlineData("abcdef 12", "Penne Rigate Spa")]
    [InlineData("zzzz", "")]
    [InlineData("IT", "")]
    [InlineData("   ", "Acme Srl,Comune di Penne,Foreign Ltd,Penne Rigate Spa")]
    public async Task Search_MatchesCompanyNameVatNumberOrTaxCodeNormalised(string search, string expected)
    {
        // Arrange
        await SeedSearchSetAsync();

        // Act
        var page = await GetPageAsync($"?search={Uri.EscapeDataString(search)}");

        // Assert
        Names(page).ShouldBe(Split(expected));
        page.GetProperty("totalCount").GetInt32().ShouldBe(Split(expected).Length);
    }

    [Fact]
    public async Task SearchOnACustomerWithoutVatNumberAndTaxCode_StillFindsItByName()
    {
        // Arrange
        await SeedSearchSetAsync();

        // Act
        var page = await GetPageAsync("?search=foreign");

        // Assert
        Names(page).ShouldBe(["Foreign Ltd"]);
    }

    [Theory]
    [InlineData("", "Acme Srl,Comune di Penne,Foreign Ltd,Penne Rigate Spa")]
    [InlineData("?status=Active", "Comune di Penne,Foreign Ltd,Penne Rigate Spa")]
    [InlineData("?status=Deactivated", "Acme Srl")]
    [InlineData("?search=penne&status=Active", "Comune di Penne,Penne Rigate Spa")]
    [InlineData("?search=penne&status=Deactivated", "")]
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
