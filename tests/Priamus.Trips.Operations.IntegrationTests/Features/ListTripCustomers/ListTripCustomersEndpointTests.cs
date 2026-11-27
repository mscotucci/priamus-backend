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

namespace Priamus.Trips.Operations.IntegrationTests.Features.ListTripCustomers;

/// <summary>
/// Endpoint integration tests for <c>GET /api/operations/driver/customers</c>, with the registry
/// replaced by <see cref="FakeTripRegistry"/>, a clock fixed at 12:00 in Italy on 2026-06-15 and the
/// test authentication scheme.
/// </summary>
public sealed class ListTripCustomersEndpointTests : OperationsIntegrationTestBase
{
    private const string Url = "/api/operations/driver/customers";

    private static readonly DateTime NowUtc = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime FirstAddedAt = new(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);

    private readonly FakeTripRegistry _registry = new();
    private readonly Guid _driverId = Guid.NewGuid();
    private readonly List<TripCustomer> _customers =
    [
        new(Guid.NewGuid(), "Alfa SRL", "00000000001"),
        new(Guid.NewGuid(), "Beta SPA", null),
        new(Guid.NewGuid(), "Gamma SNC", "00000000003"),
        new(Guid.NewGuid(), "Delta SRL", "00000000004"),
        new(Guid.NewGuid(), "Epsilon SRL", "00000000005"),
        new(Guid.NewGuid(), "Zeta SRL", "00000000006"),
        new(Guid.NewGuid(), "Omega SRL", "00000000007"),
    ];

    public ListTripCustomersEndpointTests(PostgresFixture fixture) : base(fixture)
    {
        _registry.Customers.AddRange(_customers);
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

    private HttpClient DriverClient(WebApplicationFactory<Program> factory) =>
        TestCaller.DriverWith(_driverId).ApplyTo(factory.CreateClient());

    private static async Task<(List<Guid> Recent, List<Guid> Others)> ReadAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return (
            body.GetProperty("recent").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList(),
            body.GetProperty("others").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList());
    }

    /// <summary>Adds one ended trip of <paramref name="driverId"/> for each customer, one minute apart, in the order given.</summary>
    private async Task SeedTripsAsync(Guid driverId, params TripCustomer[] customers)
    {
        var seeder = new TripSeeder(ConnectionString);
        for (var i = 0; i < customers.Length; i++)
        {
            await seeder.AddAsync(driverId, customers[i].Id, FirstAddedAt.AddMinutes(i));
        }
    }

    [Fact]
    public async Task Get_DriverWithoutTrips_ReturnsNoRecentAndEveryCustomerInRegistryOrder()
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = DriverClient(factory);

        // Act
        var response = await client.GetAsync(Url, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var (recent, others) = await ReadAsync(response);
        recent.ShouldBeEmpty();
        others.ShouldBe(_customers.Select(c => c.Id));
    }

    [Fact]
    public async Task Get_Customer_CarriesCompanyNameAndVatNumber()
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = DriverClient(factory);

        // Act
        var response = await client.GetAsync(Url, Ct);

        // Assert
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var others = body.GetProperty("others").EnumerateArray().ToList();
        others[0].GetProperty("companyName").GetString().ShouldBe("Alfa SRL");
        others[0].GetProperty("vatNumber").GetString().ShouldBe("00000000001");
        others[1].GetProperty("vatNumber").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_DriverWithTrips_PutsTheCustomersOfTheLatestTripsFirstMostRecentFirstAndDistinct()
    {
        // Arrange: the third customer is used twice, the second use being the latest of all.
        await SeedTripsAsync(_driverId, _customers[2], _customers[0], _customers[2], _customers[1]);
        await using var factory = CreateFactory();
        var client = DriverClient(factory);

        // Act
        var response = await client.GetAsync(Url, Ct);

        // Assert
        var (recent, others) = await ReadAsync(response);
        recent.ShouldBe([_customers[1].Id, _customers[2].Id, _customers[0].Id]);
        others.ShouldBe(_customers.Skip(3).Select(c => c.Id));
    }

    [Fact]
    public async Task Get_MoreThanFiveCustomersUsed_ReturnsTheFiveMostRecentAndTheRestAmongTheOthersInRegistryOrder()
    {
        // Arrange: seven customers used, the first one longest ago.
        await SeedTripsAsync(_driverId, [.. _customers]);
        await using var factory = CreateFactory();
        var client = DriverClient(factory);

        // Act
        var response = await client.GetAsync(Url, Ct);

        // Assert
        var (recent, others) = await ReadAsync(response);
        recent.ShouldBe(_customers.Skip(2).Reverse().Select(c => c.Id));
        others.ShouldBe([_customers[0].Id, _customers[1].Id]);
    }

    [Fact]
    public async Task Get_TripsOfAnotherDriver_NeverCountAsRecent()
    {
        // Arrange
        await SeedTripsAsync(Guid.NewGuid(), _customers[3], _customers[4]);
        await SeedTripsAsync(_driverId, _customers[5]);
        await using var factory = CreateFactory();
        var client = DriverClient(factory);

        // Act
        var response = await client.GetAsync(Url, Ct);

        // Assert
        var (recent, _) = await ReadAsync(response);
        recent.ShouldBe([_customers[5].Id]);
    }

    [Fact]
    public async Task Get_CustomerOfAPastTripTheRegistryNoLongerOffersToday_IsNotRecent()
    {
        // Arrange
        var gone = new TripCustomer(Guid.NewGuid(), "Chiusa SRL", null);
        await new TripSeeder(ConnectionString).AddAsync(_driverId, gone.Id, FirstAddedAt.AddHours(1));
        await SeedTripsAsync(_driverId, _customers[1]);
        await using var factory = CreateFactory();
        var client = DriverClient(factory);

        // Act
        var response = await client.GetAsync(Url, Ct);

        // Assert
        var (recent, others) = await ReadAsync(response);
        recent.ShouldBe([_customers[1].Id]);
        others.ShouldNotContain(gone.Id);
        recent.ShouldNotContain(gone.Id);
    }

    [Fact]
    public async Task Get_WithSearch_ReturnsNoRecentAndTheMatchesAsOthers_WithTheTrimmedTextPassedToTheRegistry()
    {
        // Arrange
        await SeedTripsAsync(_driverId, _customers[0]);
        await using var factory = CreateFactory();
        var client = DriverClient(factory);

        // Act
        var response = await client.GetAsync($"{Url}?search=%20%20alf%20", Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var (recent, others) = await ReadAsync(response);
        recent.ShouldBeEmpty();
        others.ShouldBe(_customers.Select(c => c.Id));
        _registry.CustomerSearches.ShouldBe(["alf"]);
    }

    [Fact]
    public async Task Get_WhitespaceSearch_IsNoSearchAndKeepsTheRecent()
    {
        // Arrange
        await SeedTripsAsync(_driverId, _customers[4]);
        await using var factory = CreateFactory();
        var client = DriverClient(factory);

        // Act
        var response = await client.GetAsync($"{Url}?search=%20%20", Ct);

        // Assert
        var (recent, _) = await ReadAsync(response);
        recent.ShouldBe([_customers[4].Id]);
    }

    [Fact]
    public async Task Get_SearchTooLong_Returns400AndNeverReachesTheRegistry()
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = DriverClient(factory);

        // Act
        var response = await client.GetAsync($"{Url}?search={new string('a', 101)}", Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        _registry.CustomerSearches.ShouldBeEmpty();
    }

    [Fact]
    public async Task Get_AsksTheRegistryForTodayInItaly()
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = DriverClient(factory);

        // Act
        var response = await client.GetAsync(Url, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        _registry.CustomerDays.ShouldBe([new DateOnly(2026, 6, 15)]);
    }

    [Fact]
    public async Task Get_CustomerOfAPlannedTrip_CountsAsRecent()
    {
        // Arrange: the only trip for the customer was added and never started.
        await new TripSeeder(ConnectionString).AddPlannedAsync(_driverId, _customers[3].Id, FirstAddedAt);
        await using var factory = CreateFactory();
        var client = DriverClient(factory);

        // Act
        var response = await client.GetAsync(Url, Ct);

        // Assert
        var (recent, _) = await ReadAsync(response);
        recent.ShouldBe([_customers[3].Id]);
    }
}
