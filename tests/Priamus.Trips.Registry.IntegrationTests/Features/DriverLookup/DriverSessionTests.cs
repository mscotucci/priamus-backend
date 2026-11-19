using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Priamus.Trips.Registry.Domain.Drivers;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.DriverTestSupport;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.VehicleTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Features.DriverLookup;

/// <summary>
/// End-to-end tests of the session a driver account receives, through the real JWT scheme: login,
/// refresh and <c>GET /api/identity/me</c> against the real driver lookup. Management calls (create
/// the account, deactivate, reactivate) go through a second host on the same database that uses
/// the test authentication scheme.
/// </summary>
public sealed class DriverSessionTests : RegistryIntegrationTestBase
{
    public DriverSessionTests(PostgresFixture fixture) : base(fixture) { }

    private const string Username = "luca.difebo";
    private const string Password = "Str0ng-session-pass!";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> CreateManagementFactory() =>
        ApiFactory.Create(ConnectionString, useTestAuthentication: true);

    private WebApplicationFactory<Program> CreateSessionFactory() =>
        ApiFactory.Create(ConnectionString);

    private static async Task<Guid> CreateAccountAsync(WebApplicationFactory<Program> management, string role)
    {
        var response = await AdminClient(management).PostAsJsonAsync(
            "/api/identity/users", new { username = Username, password = Password, role }, TestJson.Options, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private static async Task<(string AccessToken, string RefreshToken)> LoginAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/identity/auth/login", new { username = Username, password = Password }, TestJson.Options, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return (body.GetProperty("accessToken").GetString()!, body.GetProperty("refreshToken").GetString()!);
    }

    private static async Task<(string AccessToken, string RefreshToken)> RefreshAsync(HttpClient client, string refreshToken)
    {
        var response = await client.PostAsJsonAsync(
            "/api/identity/auth/refresh", new { refreshToken }, TestJson.Options, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return (body.GetProperty("accessToken").GetString()!, body.GetProperty("refreshToken").GetString()!);
    }

    private static string? DriverClaim(string accessToken) =>
        new JwtSecurityTokenHandler().ReadJwtToken(accessToken).Claims
            .SingleOrDefault(c => c.Type == "driver_id")?.Value;

    private static async Task<Guid?> MeDriverIdAsync(HttpClient client, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/identity/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await client.SendAsync(request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var driverId = body.GetProperty("driverId");
        return driverId.ValueKind == JsonValueKind.Null ? null : driverId.GetGuid();
    }

    private async Task<Driver> SeedLinkedDriverAsync(Guid userId, bool deactivated = false)
    {
        await using var db = CreateDbContext();
        return await SeedDriverAsync(db, userId: userId, deactivated: deactivated);
    }

    private static async Task SetStatusAsync(HttpClient management, Guid driverId, string action, int version)
    {
        var response = await management.PostAsJsonAsync(
            $"/api/registry/drivers/{driverId}/{action}", new { version }, TestJson.Options, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ActiveLinkedDriver_LoginAndRefreshCarryTheDriver()
    {
        // Arrange
        await using var management = CreateManagementFactory();
        var userId = await CreateAccountAsync(management, "Driver");
        var driver = await SeedLinkedDriverAsync(userId);
        await using var session = CreateSessionFactory();
        var client = session.CreateClient();

        // Act
        var login = await LoginAsync(client);
        var refreshed = await RefreshAsync(client, login.RefreshToken);

        // Assert
        DriverClaim(login.AccessToken).ShouldBe(driver.Id.Value.ToString());
        DriverClaim(refreshed.AccessToken).ShouldBe(driver.Id.Value.ToString());
        (await MeDriverIdAsync(client, login.AccessToken)).ShouldBe(driver.Id.Value);
    }

    [Fact]
    public async Task DriverAccountWithoutDriver_SessionCarriesNone()
    {
        // Arrange
        await using var management = CreateManagementFactory();
        await CreateAccountAsync(management, "Driver");
        await using var session = CreateSessionFactory();
        var client = session.CreateClient();

        // Act
        var login = await LoginAsync(client);
        var refreshed = await RefreshAsync(client, login.RefreshToken);

        // Assert
        DriverClaim(login.AccessToken).ShouldBeNull();
        DriverClaim(refreshed.AccessToken).ShouldBeNull();
        (await MeDriverIdAsync(client, login.AccessToken)).ShouldBeNull();
    }

    [Fact]
    public async Task DriverAccountLinkedToADeactivatedDriver_SessionCarriesNone()
    {
        // Arrange
        await using var management = CreateManagementFactory();
        var userId = await CreateAccountAsync(management, "Driver");
        await SeedLinkedDriverAsync(userId, deactivated: true);
        await using var session = CreateSessionFactory();
        var client = session.CreateClient();

        // Act
        var login = await LoginAsync(client);

        // Assert
        DriverClaim(login.AccessToken).ShouldBeNull();
        (await MeDriverIdAsync(client, login.AccessToken)).ShouldBeNull();
    }

    [Fact]
    public async Task DeactivateThenReactivate_SessionLosesAndRegainsTheDriverAtTheNextRefresh()
    {
        // Arrange
        await using var management = CreateManagementFactory();
        var userId = await CreateAccountAsync(management, "Driver");
        var driver = await SeedLinkedDriverAsync(userId);
        var managementClient = BackOfficeClient(management);
        await using var session = CreateSessionFactory();
        var client = session.CreateClient();
        var login = await LoginAsync(client);
        DriverClaim(login.AccessToken).ShouldBe(driver.Id.Value.ToString());

        // Act and assert: deactivated, the next refresh drops the driver
        await SetStatusAsync(managementClient, driver.Id.Value, "deactivate", driver.Version);
        var afterDeactivate = await RefreshAsync(client, login.RefreshToken);
        DriverClaim(afterDeactivate.AccessToken).ShouldBeNull();
        (await MeDriverIdAsync(client, afterDeactivate.AccessToken)).ShouldBeNull();

        // Act and assert: reactivated, the next refresh gives the same driver back, with the same account
        await SetStatusAsync(managementClient, driver.Id.Value, "reactivate", driver.Version + 1);
        var afterReactivate = await RefreshAsync(client, afterDeactivate.RefreshToken);
        DriverClaim(afterReactivate.AccessToken).ShouldBe(driver.Id.Value.ToString());
        (await MeDriverIdAsync(client, afterReactivate.AccessToken)).ShouldBe(driver.Id.Value);

        // The login itself is unchanged by the whole round trip.
        var secondLogin = await LoginAsync(client);
        DriverClaim(secondLogin.AccessToken).ShouldBe(driver.Id.Value.ToString());
    }
}
