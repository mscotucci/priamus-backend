using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.DriverTestSupport;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.VehicleTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Features.ListLinkableAccounts;

/// <summary>
/// Endpoint integration tests for <c>GET /api/registry/drivers/linkable-accounts</c>, against the
/// per-test PostgreSQL database and the test authentication scheme.
/// </summary>
public sealed class ListLinkableAccountsEndpointTests : RegistryIntegrationTestBase
{
    private const string Url = "/api/registry/drivers/linkable-accounts";

    public ListLinkableAccountsEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> CreateFactory() =>
        ApiFactory.Create(ConnectionString, useTestAuthentication: true);

    private static async Task<string[]> UsernamesAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return [.. body.EnumerateArray().Select(a => a.GetProperty("username").GetString()!)];
    }

    [Fact]
    public async Task OnlyEnabledDriverAccountsNotLinkedToAnyDriver_AreOfferedSortedByUserName()
    {
        // Arrange
        await using var factory = CreateFactory();
        await CreateAccountAsync(factory, "zeta.libero");
        await CreateAccountAsync(factory, "alfa.libero");
        await CreateAccountAsync(factory, "spento", disabled: true);
        await CreateAccountAsync(factory, "segreteria", role: "BackOffice");
        await CreateAccountAsync(factory, "direzione", role: "Admin");
        var linkedActive = await CreateAccountAsync(factory, "collegato.attivo");
        var linkedDeactivated = await CreateAccountAsync(factory, "collegato.disattivo");
        await using (var db = CreateDbContext())
        {
            await SeedDriverAsync(db, "Anna", "Alfa", userId: linkedActive);
            await SeedDriverAsync(db, "Bruno", "Beta", userId: linkedDeactivated, deactivated: true);
        }

        // Act
        var response = await BackOfficeClient(factory).GetAsync(Url, Ct);

        // Assert
        (await UsernamesAsync(response)).ShouldBe(["alfa.libero", "zeta.libero"]);
    }

    [Fact]
    public async Task WithDriverId_TheAccountOfThatDriverIsAddedEvenWhenDisabled()
    {
        // Arrange
        await using var factory = CreateFactory();
        await CreateAccountAsync(factory, "libero");
        var own = await CreateAccountAsync(factory, "proprio.spento", disabled: true);
        var other = await CreateAccountAsync(factory, "altrui");
        Guid driverId;
        await using (var db = CreateDbContext())
        {
            driverId = (await SeedDriverAsync(db, "Anna", "Alfa", userId: own)).Id.Value;
            await SeedDriverAsync(db, "Bruno", "Beta", userId: other);
        }

        // Act
        var response = await BackOfficeClient(factory).GetAsync($"{Url}?driverId={driverId}", Ct);

        // Assert
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var accounts = body.EnumerateArray().ToList();
        accounts.Select(a => a.GetProperty("username").GetString()).ShouldBe(["libero", "proprio.spento"]);
        var kept = accounts[1];
        kept.GetProperty("userId").GetGuid().ShouldBe(own);
        kept.GetProperty("isDisabled").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task WithDriverIdOfADriverWithoutAccount_OffersOnlyTheFreeAccounts()
    {
        // Arrange
        await using var factory = CreateFactory();
        await CreateAccountAsync(factory, "libero");
        Guid driverId;
        await using (var db = CreateDbContext())
        {
            driverId = (await SeedDriverAsync(db, "Anna", "Alfa")).Id.Value;
        }

        // Act
        var response = await BackOfficeClient(factory).GetAsync($"{Url}?driverId={driverId}", Ct);

        // Assert
        (await UsernamesAsync(response)).ShouldBe(["libero"]);
    }

    [Fact]
    public async Task UnknownDriverId_Returns404WithTheNotFoundCode()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await BackOfficeClient(factory).GetAsync($"{Url}?driverId={Guid.NewGuid()}", Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("errorCode").GetString().ShouldBe("Registry.Driver.NotFound");
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
