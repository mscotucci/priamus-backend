using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Priamus.Trips.IntegrationTesting;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.ContractTestSupport;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.CustomerTestSupport;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.VehicleTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Features.GetContract;

/// <summary>
/// Endpoint integration tests for <c>GET /api/registry/customers/{customerId}/contracts/{contractId}</c>,
/// against the per-test PostgreSQL database and the test authentication scheme.
/// </summary>
public sealed class GetContractEndpointTests : RegistryIntegrationTestBase
{
    private static readonly DateTime Midday = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

    public GetContractEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Url(Guid customerId, Guid contractId) =>
        $"/api/registry/customers/{customerId}/contracts/{contractId}";

    private async Task<Customer> SeedCustomerAsync(string vatNumber = VatNumberA)
    {
        await using var db = CreateDbContext();
        return await CustomerTestSupport.SeedCustomerAsync(db, vatNumber: vatNumber, taxCode: null);
    }

    private async Task<Contract> SeedAsync(CustomerId customerId)
    {
        await using var db = CreateDbContext();
        return await SeedContractAsync(db, customerId, new DateOnly(2026, 4, 1), new DateOnly(2026, 12, 31));
    }

    [Fact]
    public async Task ReturnsTheContractWithItsStatusAndTerms()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var contract = await SeedAsync(customer.Id);
        await using var factory = CreateFactory(ConnectionString, Midday);

        // Act
        var response = await BackOfficeClient(factory).GetAsync(Url(customer.Id.Value, contract.Id.Value), Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("id").GetGuid().ShouldBe(contract.Id.Value);
        body.GetProperty("customerId").GetGuid().ShouldBe(customer.Id.Value);
        body.GetProperty("validFrom").GetString().ShouldBe("2026-04-01");
        body.GetProperty("validTo").GetString().ShouldBe("2026-12-31");
        body.GetProperty("status").GetString().ShouldBe("Current");
        body.GetProperty("billingLevel").GetString().ShouldBe("PerLocationPair");
        body.GetProperty("pricingMode").GetString().ShouldBe("ByTrip");
        body.GetProperty("requiredDocuments").EnumerateArray().Select(d => d.GetString()).ShouldBe(["DeliveryNote", "DailySummary"]);
        body.GetProperty("nightSurcharge").GetBoolean().ShouldBeTrue();
        body.GetProperty("version").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task ReturnsTheRateAndTheMinimumOfAFlatRateContract()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var rate = Rate.Create(45.25m, 60.5m, 10m, new MinimumRule.TieredOverage(100m, 4.5m, 22.25m)).Value;
        Contract contract;
        await using (var db = CreateDbContext())
        {
            contract = await SeedContractAsync(
                db, customer.Id, new DateOnly(2026, 4, 1), null, BillingLevel.FlatRate, PricingMode.ByHour,
                nightSurcharge: true, rate: rate);
        }

        await using var factory = CreateFactory(ConnectionString, Midday);

        // Act
        var response = await BackOfficeClient(factory).GetAsync(Url(customer.Id.Value, contract.Id.Value), Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("dayPrice").GetDecimal().ShouldBe(45.25m);
        body.GetProperty("nightPrice").GetDecimal().ShouldBe(60.5m);
        body.GetProperty("nightCallFee").GetDecimal().ShouldBe(10m);
        body.GetProperty("minimumRule").GetString().ShouldBe("TieredOverage");
        body.GetProperty("minimumAmount").GetDecimal().ShouldBe(100m);
        body.GetProperty("minimumThreshold").GetDecimal().ShouldBe(4.5m);
        body.GetProperty("minimumOveragePrice").GetDecimal().ShouldBe(22.25m);
    }

    [Fact]
    public async Task ContractWithoutARate_ReturnsNullRateMembers()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var contract = await SeedAsync(customer.Id);
        await using var factory = CreateFactory(ConnectionString, Midday);

        // Act
        var response = await BackOfficeClient(factory).GetAsync(Url(customer.Id.Value, contract.Id.Value), Ct);

        // Assert
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        foreach (var member in new[]
                 {
                     "dayPrice", "nightPrice", "nightCallFee", "minimumRule", "minimumAmount",
                     "minimumThreshold", "minimumOveragePrice",
                 })
        {
            body.GetProperty(member).ValueKind.ShouldBe(JsonValueKind.Null, member);
        }
    }

    [Fact]
    public async Task UnknownContract_Returns404ContractNotFound()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        await using var factory = CreateFactory(ConnectionString, Midday);

        // Act
        var response = await BackOfficeClient(factory).GetAsync(Url(customer.Id.Value, Guid.NewGuid()), Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, ContractErrors.NotFound, Ct);
    }

    [Fact]
    public async Task ContractOfAnotherCustomer_Returns404ContractNotFound()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var other = await SeedCustomerAsync(VatNumberB);
        var contract = await SeedAsync(other.Id);
        await using var factory = CreateFactory(ConnectionString, Midday);

        // Act
        var response = await BackOfficeClient(factory).GetAsync(Url(customer.Id.Value, contract.Id.Value), Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, ContractErrors.NotFound, Ct);
    }

    [Fact]
    public async Task UnknownCustomer_Returns404CustomerNotFound()
    {
        // Arrange
        await using var factory = CreateFactory(ConnectionString, Midday);

        // Act
        var response = await BackOfficeClient(factory).GetAsync(Url(Guid.NewGuid(), Guid.NewGuid()), Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, CustomerErrors.NotFound, Ct);
    }

    [Theory]
    [InlineData("BackOffice", HttpStatusCode.OK)]
    [InlineData("Admin", HttpStatusCode.OK)]
    [InlineData("Driver", HttpStatusCode.Forbidden)]
    public async Task Role_DecidesWhoReadsAContract(string role, HttpStatusCode expected)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var contract = await SeedAsync(customer.Id);
        await using var factory = CreateFactory(ConnectionString, Midday);
        var client = TestCaller.WithRole(role).ApplyTo(factory.CreateClient());

        // Act
        var response = await client.GetAsync(Url(customer.Id.Value, contract.Id.Value), Ct);

        // Assert
        response.StatusCode.ShouldBe(expected);
    }

    [Fact]
    public async Task ReturnsTheVehicleRatesByPlateWithTheStatusOfEachVehicle()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        Contract contract;
        await using (var db = CreateDbContext())
        {
            var last = await SeedVehicleAsync(db, "EF789GH");
            var decommissioned = await SeedVehicleAsync(db, "CD123EF", decommissioned: true);
            var first = await SeedVehicleAsync(db, "AB456CD");
            contract = await SeedContractAsync(
                db, customer.Id, new DateOnly(2026, 4, 1), null, BillingLevel.FlatRate, PricingMode.ByHour,
                nightSurcharge: true, rate: Rate.Create(45m, 60m, 10m, new MinimumRule.None()).Value,
                vehicleRates:
                [
                    new VehicleRate(last.Id, Rate.Create(48m, 62m, 0m, new MinimumRule.FixedMinimum(120m)).Value),
                    new VehicleRate(decommissioned.Id, Rate.Create(52.5m, 70.25m, 12m, new MinimumRule.TieredOverage(100m, 4.5m, 22.25m)).Value),
                    new VehicleRate(first.Id, Rate.Create(50m, 65m, 5m, new MinimumRule.None()).Value),
                ]);
        }

        await using var factory = CreateFactory(ConnectionString, Midday);

        // Act
        var response = await BackOfficeClient(factory).GetAsync(Url(customer.Id.Value, contract.Id.Value), Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var rates = body.GetProperty("vehicleRates").EnumerateArray().ToList();
        rates.Select(r => r.GetProperty("licensePlate").GetString()).ShouldBe(["AB456CD", "CD123EF", "EF789GH"]);
        rates.Select(r => r.GetProperty("vehicleStatus").GetString()).ShouldBe(["Active", "Decommissioned", "Active"]);
        rates[0].GetProperty("description").GetString().ShouldBe("Iveco Stralis 480");
        rates[0].GetProperty("minimumRule").GetString().ShouldBe("None");
        rates[1].GetProperty("dayPrice").GetDecimal().ShouldBe(52.5m);
        rates[1].GetProperty("nightPrice").GetDecimal().ShouldBe(70.25m);
        rates[1].GetProperty("nightCallFee").GetDecimal().ShouldBe(12m);
        rates[1].GetProperty("minimumRule").GetString().ShouldBe("TieredOverage");
        rates[1].GetProperty("minimumAmount").GetDecimal().ShouldBe(100m);
        rates[1].GetProperty("minimumThreshold").GetDecimal().ShouldBe(4.5m);
        rates[1].GetProperty("minimumOveragePrice").GetDecimal().ShouldBe(22.25m);
        rates[2].GetProperty("minimumAmount").GetDecimal().ShouldBe(120m);
    }

    [Fact]
    public async Task ContractWithoutVehicleRates_ReturnsAnEmptyList()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var contract = await SeedAsync(customer.Id);
        await using var factory = CreateFactory(ConnectionString, Midday);

        // Act
        var response = await BackOfficeClient(factory).GetAsync(Url(customer.Id.Value, contract.Id.Value), Ct);

        // Assert
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("vehicleRates").ValueKind.ShouldBe(JsonValueKind.Array);
        body.GetProperty("vehicleRates").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task ReturnsThePairRatesOrderedByPickupThenDeliveryWithTheStatusOfEachLocation()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        Contract contract;
        await using (var db = CreateDbContext())
        {
            var cavaSud = await LocationTestSupport.SeedLocationAsync(db, "Cava Sud", "Chieti");
            var cavaNordPescara = await LocationTestSupport.SeedLocationAsync(db, "Cava Nord", "Pescara");
            var cavaNordChieti = await LocationTestSupport.SeedLocationAsync(db, "Cava Nord", "Chieti");
            var discarica = await LocationTestSupport.SeedLocationAsync(db, "Discarica Est", "Spoltore", deactivated: true);
            contract = await SeedContractAsync(
                db, customer.Id, new DateOnly(2026, 4, 1), null, BillingLevel.PerLocationPair, PricingMode.ByHour,
                nightSurcharge: true,
                locationRates:
                [
                    new LocationRate(cavaSud.Id, discarica.Id, Rate.Create(48m, 62m, 0m, new MinimumRule.FixedMinimum(120m)).Value),
                    new LocationRate(cavaNordPescara.Id, discarica.Id, Rate.Create(50m, 65m, 5m, new MinimumRule.None()).Value),
                    new LocationRate(cavaNordChieti.Id, discarica.Id, Rate.Create(52.5m, 70.25m, 12m, new MinimumRule.TieredOverage(100m, 4.5m, 22.25m)).Value),
                    new LocationRate(cavaNordChieti.Id, cavaSud.Id, Rate.Create(40m, 41m, 1m, new MinimumRule.None()).Value),
                ]);
        }

        await using var factory = CreateFactory(ConnectionString, Midday);

        // Act
        var response = await BackOfficeClient(factory).GetAsync(Url(customer.Id.Value, contract.Id.Value), Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var rates = body.GetProperty("locationRates").EnumerateArray().ToList();
        rates.Select(r => r.GetProperty("pickup").GetProperty("name").GetString() + "/" + r.GetProperty("pickup").GetProperty("municipality").GetString()
                + " -> " + r.GetProperty("delivery").GetProperty("name").GetString() + "/" + r.GetProperty("delivery").GetProperty("municipality").GetString())
            .ShouldBe(
            [
                "Cava Nord/Chieti -> Cava Sud/Chieti",
                "Cava Nord/Chieti -> Discarica Est/Spoltore",
                "Cava Nord/Pescara -> Discarica Est/Spoltore",
                "Cava Sud/Chieti -> Discarica Est/Spoltore",
            ]);
        rates.Select(r => r.GetProperty("pickup").GetProperty("status").GetString()).ShouldAllBe(s => s == "Active");
        rates.Select(r => r.GetProperty("delivery").GetProperty("status").GetString()).ShouldBe(["Active", "Deactivated", "Deactivated", "Deactivated"]);
        rates[1].GetProperty("dayPrice").GetDecimal().ShouldBe(52.5m);
        rates[1].GetProperty("nightPrice").GetDecimal().ShouldBe(70.25m);
        rates[1].GetProperty("nightCallFee").GetDecimal().ShouldBe(12m);
        rates[1].GetProperty("minimumRule").GetString().ShouldBe("TieredOverage");
        rates[1].GetProperty("minimumAmount").GetDecimal().ShouldBe(100m);
        rates[1].GetProperty("minimumThreshold").GetDecimal().ShouldBe(4.5m);
        rates[1].GetProperty("minimumOveragePrice").GetDecimal().ShouldBe(22.25m);
        rates[3].GetProperty("minimumAmount").GetDecimal().ShouldBe(120m);
        rates[0].GetProperty("pickup").GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task ReturnsTheLocationRatesOfALocationContractWithNullMembersOnTheUnusedSide()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        Contract contract;
        await using (var db = CreateDbContext())
        {
            var last = await LocationTestSupport.SeedLocationAsync(db, "Discarica Est", "Spoltore");
            var first = await LocationTestSupport.SeedLocationAsync(db, "Cava Nord", "Pescara");
            contract = await SeedContractAsync(
                db, customer.Id, new DateOnly(2026, 4, 1), null, BillingLevel.PerDeliveryLocation, PricingMode.ByHour,
                nightSurcharge: false,
                locationRates:
                [
                    new LocationRate(null, last.Id, Rate.Create(48m, null, null, new MinimumRule.None()).Value),
                    new LocationRate(null, first.Id, Rate.Create(50m, null, null, new MinimumRule.None()).Value),
                ]);
        }

        await using var factory = CreateFactory(ConnectionString, Midday);

        // Act
        var response = await BackOfficeClient(factory).GetAsync(Url(customer.Id.Value, contract.Id.Value), Ct);

        // Assert
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var rates = body.GetProperty("locationRates").EnumerateArray().ToList();
        rates.Select(r => r.GetProperty("delivery").GetProperty("name").GetString()).ShouldBe(["Cava Nord", "Discarica Est"]);
        foreach (var rate in rates)
        {
            rate.GetProperty("pickup").ValueKind.ShouldBe(JsonValueKind.Null);
        }
    }

    [Fact]
    public async Task LocationContractStoredWithoutRates_ReturnsAnEmptyLocationRatesList()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        Contract contract;
        await using (var db = CreateDbContext())
        {
            contract = await SeedLocationContractWithoutRatesAsync(
                db, customer.Id, new DateOnly(2026, 4, 1), new DateOnly(2026, 12, 31));
        }
        await using var factory = CreateFactory(ConnectionString, Midday);

        // Act
        var response = await BackOfficeClient(factory).GetAsync(Url(customer.Id.Value, contract.Id.Value), Ct);

        // Assert
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("locationRates").ValueKind.ShouldBe(JsonValueKind.Array);
        body.GetProperty("locationRates").GetArrayLength().ShouldBe(0);
    }

    [Theory]
    [InlineData("BackOffice", HttpStatusCode.OK)]
    [InlineData("Admin", HttpStatusCode.OK)]
    [InlineData("Driver", HttpStatusCode.Forbidden)]
    public async Task Role_DecidesWhoReadsAContractWithLocationRates(string role, HttpStatusCode expected)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        Contract contract;
        await using (var db = CreateDbContext())
        {
            var location = await LocationTestSupport.SeedLocationAsync(db);
            contract = await SeedContractAsync(
                db, customer.Id, new DateOnly(2026, 4, 1), null, BillingLevel.PerPickupLocation, PricingMode.ByHour,
                nightSurcharge: false,
                locationRates: [new LocationRate(location.Id, null, Rate.Create(48m, null, null, new MinimumRule.None()).Value)]);
        }

        await using var factory = CreateFactory(ConnectionString, Midday);
        var client = TestCaller.WithRole(role).ApplyTo(factory.CreateClient());

        // Act
        var response = await client.GetAsync(Url(customer.Id.Value, contract.Id.Value), Ct);

        // Assert
        response.StatusCode.ShouldBe(expected);
        if (expected == HttpStatusCode.Forbidden)
        {
            (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain("dayPrice");
        }
    }
}
