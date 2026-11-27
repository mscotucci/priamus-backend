using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Priamus.Trips.IntegrationTesting;
using Priamus.Trips.Operations.IntegrationTests.Infrastructure;
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Operations.IntegrationTests.Features.CheckTripAnswers;

/// <summary>
/// Endpoint integration tests for <c>GET /api/operations/driver/answers-check</c>, with the registry
/// replaced by <see cref="FakeTripRegistry"/>, a clock fixed at 12:00 in Italy on 2026-06-15 and the
/// test authentication scheme.
/// </summary>
public sealed class CheckTripAnswersEndpointTests : OperationsIntegrationTestBase
{
    private const string Url = "/api/operations/driver/answers-check";

    private static readonly DateTime NowUtc = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

    private readonly FakeTripRegistry _registry = new();

    public CheckTripAnswersEndpointTests(PostgresFixture fixture) : base(fixture) { }

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

    private static HttpClient DriverClient(WebApplicationFactory<Program> factory) =>
        TestCaller.DriverWith(Guid.NewGuid()).ApplyTo(factory.CreateClient());

    [Fact]
    public async Task Get_PassesEachJudgmentOfTheRegistryThrough()
    {
        // Arrange: each answer gets a different verdict, so a swapped member would be seen.
        _registry.AnswersCheck = new TripAnswersCheck(Vehicle: true, Trailer: false, Customer: true, Pickup: false, Delivery: true);
        await using var factory = CreateFactory();
        var url = $"{Url}?vehicleId={Guid.NewGuid()}&trailerId={Guid.NewGuid()}&customerId={Guid.NewGuid()}" +
                  $"&pickupLocationId={Guid.NewGuid()}&deliveryLocationId={Guid.NewGuid()}";

        // Act
        var response = await DriverClient(factory).GetAsync(url, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("vehicle").GetBoolean().ShouldBeTrue();
        body.GetProperty("trailer").GetBoolean().ShouldBeFalse();
        body.GetProperty("customer").GetBoolean().ShouldBeTrue();
        body.GetProperty("pickup").GetBoolean().ShouldBeFalse();
        body.GetProperty("delivery").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Get_JudgesTheAnswersGivenAgainstTodayInItaly()
    {
        // Arrange
        var vehicleId = Guid.NewGuid();
        var trailerId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var pickupId = Guid.NewGuid();
        var deliveryId = Guid.NewGuid();
        await using var factory = CreateFactory();
        var url = $"{Url}?vehicleId={vehicleId}&trailerId={trailerId}&customerId={customerId}" +
                  $"&pickupLocationId={pickupId}&deliveryLocationId={deliveryId}";

        // Act
        var response = await DriverClient(factory).GetAsync(url, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        _registry.CheckedAnswers.ShouldBe(
            [new TripAnswers(vehicleId, trailerId, customerId, new DateOnly(2026, 6, 15), pickupId, deliveryId)]);
    }

    [Fact]
    public async Task Get_AnIdThatIsNotAGuid_Returns400()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await DriverClient(factory).GetAsync($"{Url}?vehicleId=not-a-guid", Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_AsABackOfficeCaller_Returns403()
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = TestCaller.WithRole("BackOffice").ApplyTo(factory.CreateClient());

        // Act
        var response = await client.GetAsync(Url, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
