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

namespace Priamus.Trips.Registry.IntegrationTests.Features.ListContracts;

/// <summary>
/// Endpoint integration tests for <c>GET /api/registry/customers/{customerId}/contracts</c>, against
/// the per-test PostgreSQL database and the test authentication scheme.
/// </summary>
public sealed class ListContractsEndpointTests : RegistryIntegrationTestBase
{
    // 15 June 2026 in Italian time, whatever the offset.
    private static readonly DateTime Midday = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

    public ListContractsEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private static string Url(Guid customerId) => $"/api/registry/customers/{customerId}/contracts";

    private async Task<Customer> SeedCustomerAsync(string vatNumber = VatNumberA)
    {
        await using var db = CreateDbContext();
        return await CustomerTestSupport.SeedCustomerAsync(db, vatNumber: vatNumber, taxCode: null);
    }

    private async Task SeedAsync(
        CustomerId customerId,
        DateOnly from,
        DateOnly? to,
        bool nightSurcharge = true,
        RequiredDocument[]? documents = null,
        BillingLevel level = BillingLevel.PerLocationPair,
        PricingMode mode = PricingMode.ByTrip)
    {
        await using var db = CreateDbContext();
        var rate = level == BillingLevel.FlatRate ? Rate.Create(45m, null, null, new MinimumRule.None()).Value : null;
        await SeedContractAsync(db, customerId, from, to, level, mode, documents, nightSurcharge, rate);
    }

    private async Task<JsonElement> GetListAsync(Guid customerId, DateTime utcNow)
    {
        await using var factory = CreateFactory(ConnectionString, utcNow);
        var response = await BackOfficeClient(factory).GetAsync(Url(customerId), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static string[] Statuses(JsonElement list) =>
        [.. list.EnumerateArray().Select(c => c.GetProperty("status").GetString()!)];

    [Fact]
    public async Task ReturnsEveryFieldOfTheTableNewestStartFirstNotPaged()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var other = await SeedCustomerAsync(VatNumberB);
        await SeedAsync(customer.Id, D(2025, 1, 1), D(2025, 12, 31), nightSurcharge: false,
            documents: [], level: BillingLevel.FlatRate, mode: PricingMode.ByHour);
        await SeedAsync(customer.Id, D(2027, 1, 1), null);
        await SeedAsync(customer.Id, D(2026, 4, 1), D(2026, 12, 31),
            documents: [RequiredDocument.WasteForm, RequiredDocument.DeliveryNote, RequiredDocument.WasteForm]);
        await SeedAsync(other.Id, D(2028, 1, 1), null);
        for (var year = 2000; year < 2024; year++)
        {
            await SeedAsync(customer.Id, D(year, 1, 1), D(year, 1, 31));
        }

        // Act
        var list = await GetListAsync(customer.Id.Value, Midday);

        // Assert
        list.GetArrayLength().ShouldBe(27);
        var starts = list.EnumerateArray().Select(c => c.GetProperty("validFrom").GetString()!).ToList();
        starts.ShouldBe([.. starts.OrderByDescending(s => s, StringComparer.Ordinal)]);
        list.EnumerateArray().Select(c => c.GetProperty("customerId").GetGuid()).Distinct().ShouldBe([customer.Id.Value]);

        var open = list[0];
        open.GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);
        open.GetProperty("validFrom").GetString().ShouldBe("2027-01-01");
        open.GetProperty("validTo").ValueKind.ShouldBe(JsonValueKind.Null);
        open.GetProperty("status").GetString().ShouldBe("Upcoming");
        open.GetProperty("billingLevel").GetString().ShouldBe("PerLocationPair");
        open.GetProperty("pricingMode").GetString().ShouldBe("ByTrip");
        open.GetProperty("requiredDocuments").EnumerateArray().Select(d => d.GetString()).ShouldBe(["DeliveryNote", "DailySummary"]);
        open.GetProperty("nightSurcharge").GetBoolean().ShouldBeTrue();
        open.GetProperty("version").GetInt32().ShouldBe(0);

        var current = list[1];
        current.GetProperty("validFrom").GetString().ShouldBe("2026-04-01");
        current.GetProperty("validTo").GetString().ShouldBe("2026-12-31");
        current.GetProperty("status").GetString().ShouldBe("Current");
        current.GetProperty("requiredDocuments").EnumerateArray().Select(d => d.GetString()).ShouldBe(["DeliveryNote", "WasteForm"]);

        var expired = list[2];
        expired.GetProperty("validFrom").GetString().ShouldBe("2025-01-01");
        expired.GetProperty("status").GetString().ShouldBe("Expired");
        expired.GetProperty("billingLevel").GetString().ShouldBe("FlatRate");
        expired.GetProperty("pricingMode").GetString().ShouldBe("ByHour");
        expired.GetProperty("requiredDocuments").GetArrayLength().ShouldBe(0);
        expired.GetProperty("nightSurcharge").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task ReturnsTheRateOfEachContractAndNullsForOneWithout()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var rate = Rate.Create(30m, null, null, new MinimumRule.FixedMinimum(90m)).Value;
        await using (var db = CreateDbContext())
        {
            await SeedContractAsync(
                db, customer.Id, D(2026, 4, 1), null, BillingLevel.FlatRate, PricingMode.ByDistance,
                nightSurcharge: false, rate: rate);
            await SeedContractAsync(db, customer.Id, D(2025, 1, 1), D(2025, 12, 31));
        }

        // Act
        var list = await GetListAsync(customer.Id.Value, Midday);

        // Assert
        var withRate = list[0];
        withRate.GetProperty("dayPrice").GetDecimal().ShouldBe(30m);
        withRate.GetProperty("nightPrice").ValueKind.ShouldBe(JsonValueKind.Null);
        withRate.GetProperty("nightCallFee").ValueKind.ShouldBe(JsonValueKind.Null);
        withRate.GetProperty("minimumRule").GetString().ShouldBe("FixedMinimum");
        withRate.GetProperty("minimumAmount").GetDecimal().ShouldBe(90m);
        withRate.GetProperty("minimumThreshold").ValueKind.ShouldBe(JsonValueKind.Null);
        withRate.GetProperty("minimumOveragePrice").ValueKind.ShouldBe(JsonValueKind.Null);
        var withoutRate = list[1];
        withoutRate.GetProperty("dayPrice").ValueKind.ShouldBe(JsonValueKind.Null);
        withoutRate.GetProperty("minimumRule").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task CustomerWithoutContracts_ReturnsAnEmptyArray()
    {
        // Arrange
        var customer = await SeedCustomerAsync();

        // Act
        var list = await GetListAsync(customer.Id.Value, Midday);

        // Assert
        list.ValueKind.ShouldBe(JsonValueKind.Array);
        list.GetArrayLength().ShouldBe(0);
    }

    [Theory]
    [InlineData("2026-06-30T21:59:00Z", "2026-06-30", "Upcoming", "Current")] // 23:59 in Rome (summer time), still 30 June
    [InlineData("2026-06-30T22:00:00Z", "2026-06-30", "Current", "Expired")]  // 00:00 in Rome, 1 July
    [InlineData("2026-12-31T22:59:00Z", "2026-12-31", "Upcoming", "Current")] // 23:59 in Rome (winter time), still 31 December
    [InlineData("2026-12-31T23:00:00Z", "2026-12-31", "Current", "Expired")]  // 00:00 in Rome, 1 January
    public async Task StatusFollowsTheItalianDay(string utcNow, string lastDay, string laterContract, string earlierContract)
    {
        // Arrange: the earlier contract ends on lastDay, the later one starts the day after.
        var now = DateTime.SpecifyKind(DateTime.Parse(utcNow, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal), DateTimeKind.Utc);
        var end = DateOnly.Parse(lastDay, System.Globalization.CultureInfo.InvariantCulture);
        var customer = await SeedCustomerAsync();
        await SeedAsync(customer.Id, D(2020, 1, 1), end);
        await SeedAsync(customer.Id, end.AddDays(1), null);

        // Act
        var list = await GetListAsync(customer.Id.Value, now);

        // Assert
        Statuses(list).ShouldBe([laterContract, earlierContract]);
    }

    [Fact]
    public async Task StatusIsCurrentUpcomingOrExpiredAgainstToday()
    {
        // Arrange: 22:30 UTC on 30 June is 00:30 on 1 July in Rome.
        var now = new DateTime(2026, 6, 30, 22, 30, 0, DateTimeKind.Utc);
        var customer = await SeedCustomerAsync();
        await SeedAsync(customer.Id, D(2026, 7, 1), D(2026, 7, 31));
        await SeedAsync(customer.Id, D(2026, 8, 1), null);
        await SeedAsync(customer.Id, D(2026, 1, 1), D(2026, 6, 30));

        // Act
        var list = await GetListAsync(customer.Id.Value, now);

        // Assert
        Statuses(list).ShouldBe(["Upcoming", "Current", "Expired"]);
    }

    [Fact]
    public async Task UnknownCustomer_Returns404CustomerNotFound()
    {
        // Arrange
        await using var factory = CreateFactory(ConnectionString, Midday);

        // Act
        var response = await BackOfficeClient(factory).GetAsync(Url(Guid.NewGuid()), Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, CustomerErrors.NotFound, Ct);
    }

    [Theory]
    [InlineData("BackOffice", HttpStatusCode.OK)]
    [InlineData("Admin", HttpStatusCode.OK)]
    [InlineData("Driver", HttpStatusCode.Forbidden)]
    public async Task Role_DecidesWhoListsContracts(string role, HttpStatusCode expected)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        await SeedAsync(customer.Id, D(2026, 1, 1), null);
        await using var factory = CreateFactory(ConnectionString, Midday);
        var client = TestCaller.WithRole(role).ApplyTo(factory.CreateClient());

        // Act
        var response = await client.GetAsync(Url(customer.Id.Value), Ct);

        // Assert
        response.StatusCode.ShouldBe(expected);
    }

    [Fact]
    public async Task ReturnsEachContractsVehicleRatesWithTheStatusOfTheVehicle()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        await using (var db = CreateDbContext())
        {
            var decommissioned = await SeedVehicleAsync(db, "CD123EF", decommissioned: true);
            var active = await SeedVehicleAsync(db, "AB456CD");
            var rate = Rate.Create(45m, 60m, 10m, new MinimumRule.None()).Value;
            await SeedContractAsync(
                db, customer.Id, D(2025, 1, 1), D(2025, 12, 31), BillingLevel.FlatRate, PricingMode.ByHour,
                nightSurcharge: true, rate: rate,
                vehicleRates:
                [
                    new VehicleRate(decommissioned.Id, Rate.Create(52.5m, 70.25m, 12m, new MinimumRule.None()).Value),
                    new VehicleRate(active.Id, Rate.Create(50m, 65m, 5m, new MinimumRule.FixedMinimum(120m)).Value),
                ]);
            await SeedContractAsync(
                db, customer.Id, D(2026, 1, 1), null, BillingLevel.FlatRate, PricingMode.ByHour,
                nightSurcharge: true, rate: rate,
                vehicleRates: [new VehicleRate(active.Id, Rate.Create(51m, 66m, 6m, new MinimumRule.None()).Value)]);
            await SeedContractAsync(db, customer.Id, D(2024, 1, 1), D(2024, 12, 31));
        }

        // Act
        var list = await GetListAsync(customer.Id.Value, Midday);

        // Assert
        var contracts = list.EnumerateArray().ToList();
        contracts.Count.ShouldBe(3);
        var current = contracts[0].GetProperty("vehicleRates").EnumerateArray().Single();
        current.GetProperty("licensePlate").GetString().ShouldBe("AB456CD");
        current.GetProperty("dayPrice").GetDecimal().ShouldBe(51m);
        var earlier = contracts[1].GetProperty("vehicleRates").EnumerateArray().ToList();
        earlier.Select(r => r.GetProperty("licensePlate").GetString()).ShouldBe(["AB456CD", "CD123EF"]);
        earlier.Select(r => r.GetProperty("vehicleStatus").GetString()).ShouldBe(["Active", "Decommissioned"]);
        earlier[0].GetProperty("minimumAmount").GetDecimal().ShouldBe(120m);
        contracts[2].GetProperty("vehicleRates").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task ReturnsEachContractsLocationRatesOrderedWithTheStatusOfTheLocations()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        await using (var db = CreateDbContext())
        {
            var deactivated = await LocationTestSupport.SeedLocationAsync(db, "Cava Sud", "Chieti", deactivated: true);
            var active = await LocationTestSupport.SeedLocationAsync(db, "Cava Nord", "Pescara");
            await SeedContractAsync(
                db, customer.Id, D(2025, 1, 1), D(2025, 12, 31), BillingLevel.PerPickupLocation, PricingMode.ByHour,
                nightSurcharge: true,
                locationRates:
                [
                    new LocationRate(deactivated.Id, null, Rate.Create(52.5m, 70.25m, 12m, new MinimumRule.FixedMinimum(120m)).Value),
                    new LocationRate(active.Id, null, Rate.Create(50m, 65m, 5m, new MinimumRule.None()).Value),
                ]);
            await SeedContractAsync(
                db, customer.Id, D(2026, 1, 1), null, BillingLevel.PerLocationPair, PricingMode.ByHour,
                nightSurcharge: false,
                locationRates: [new LocationRate(active.Id, deactivated.Id, Rate.Create(51m, null, null, new MinimumRule.None()).Value)]);
            await SeedLocationContractWithoutRatesAsync(db, customer.Id, D(2024, 1, 1), D(2024, 12, 31));
        }

        // Act
        var list = await GetListAsync(customer.Id.Value, Midday);

        // Assert
        var contracts = list.EnumerateArray().ToList();
        contracts.Count.ShouldBe(3);
        var pair = contracts[0].GetProperty("locationRates").EnumerateArray().Single();
        pair.GetProperty("pickup").GetProperty("name").GetString().ShouldBe("Cava Nord");
        pair.GetProperty("delivery").GetProperty("name").GetString().ShouldBe("Cava Sud");
        pair.GetProperty("delivery").GetProperty("municipality").GetString().ShouldBe("Chieti");
        pair.GetProperty("pickup").GetProperty("status").GetString().ShouldBe("Active");
        pair.GetProperty("delivery").GetProperty("status").GetString().ShouldBe("Deactivated");
        pair.GetProperty("dayPrice").GetDecimal().ShouldBe(51m);
        var earlier = contracts[1].GetProperty("locationRates").EnumerateArray().ToList();
        earlier.Select(r => r.GetProperty("pickup").GetProperty("name").GetString()).ShouldBe(["Cava Nord", "Cava Sud"]);
        earlier.Select(r => r.GetProperty("pickup").GetProperty("status").GetString()).ShouldBe(["Active", "Deactivated"]);
        earlier[1].GetProperty("minimumAmount").GetDecimal().ShouldBe(120m);
        earlier[1].GetProperty("delivery").ValueKind.ShouldBe(JsonValueKind.Null);
        contracts[2].GetProperty("locationRates").GetArrayLength().ShouldBe(0);
    }

    [Theory]
    [InlineData("BackOffice", HttpStatusCode.OK)]
    [InlineData("Admin", HttpStatusCode.OK)]
    [InlineData("Driver", HttpStatusCode.Forbidden)]
    public async Task Role_DecidesWhoListsContractsWithLocationRates(string role, HttpStatusCode expected)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        await using (var db = CreateDbContext())
        {
            var location = await LocationTestSupport.SeedLocationAsync(db);
            await SeedContractAsync(
                db, customer.Id, D(2026, 1, 1), null, BillingLevel.PerPickupLocation, PricingMode.ByHour,
                nightSurcharge: false,
                locationRates: [new LocationRate(location.Id, null, Rate.Create(48m, null, null, new MinimumRule.None()).Value)]);
        }

        await using var factory = CreateFactory(ConnectionString, Midday);
        var client = TestCaller.WithRole(role).ApplyTo(factory.CreateClient());

        // Act
        var response = await client.GetAsync(Url(customer.Id.Value), Ct);

        // Assert
        response.StatusCode.ShouldBe(expected);
        if (expected == HttpStatusCode.Forbidden)
        {
            (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain("dayPrice");
        }
    }
}
