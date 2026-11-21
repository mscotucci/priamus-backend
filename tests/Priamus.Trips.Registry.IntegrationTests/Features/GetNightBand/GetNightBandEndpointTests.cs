using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Priamus.Trips.IntegrationTesting;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.DriverTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Features.GetNightBand;

/// <summary>
/// Endpoint integration tests for <c>GET /api/registry/night-band</c>, against the per-test PostgreSQL
/// database and the test authentication scheme.
/// </summary>
public sealed class GetNightBandEndpointTests : RegistryIntegrationTestBase
{
    public GetNightBandEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> CreateFactory() =>
        ApiFactory.Create(ConnectionString, useTestAuthentication: true);

    [Fact]
    public async Task Admin_ReadsTheSeededBandAsHoursAndMinutesWithItsVersion()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await AdminClient(factory).GetAsync("/api/registry/night-band", Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("start").GetString().ShouldBe("22:00");
        body.GetProperty("end").GetString().ShouldBe("06:00");
        body.GetProperty("version").GetInt32().ShouldBe(0);
    }

    [Theory]
    [InlineData("BackOffice")]
    [InlineData("Driver")]
    public async Task NonAdminRole_Returns403(string role)
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = TestCaller.WithRole(role).ApplyTo(factory.CreateClient());

        // Act
        var response = await client.GetAsync("/api/registry/night-band", Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnonymousCaller_Returns401()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await factory.CreateClient().GetAsync("/api/registry/night-band", Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
