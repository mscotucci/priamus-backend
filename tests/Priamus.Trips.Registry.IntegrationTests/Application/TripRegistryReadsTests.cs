using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.ContractTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Application;

/// <summary>
/// The reads of <see cref="ITripRegistry"/> against the per-test PostgreSQL database: the choices a
/// driver is offered and the names of what a trip refers to.
/// </summary>
public sealed class TripRegistryReadsTests : RegistryIntegrationTestBase
{
    private static readonly DateOnly Day = new(2026, 6, 15);

    public TripRegistryReadsTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<T> ReadAsync<T>(Func<ITripRegistry, Task<T>> read)
    {
        await using var factory = ApiFactory.Create(ConnectionString, useTestAuthentication: true);
        using var scope = factory.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<ITripRegistry>());
    }

    private static Rate DayOnly() => Rate.Create(45m, null, null, new MinimumRule.None()).Value;

    private async Task<Customer> SeedCustomerWithContractAsync(
        string companyName,
        DateOnly validFrom,
        DateOnly? validTo,
        BillingLevel level = BillingLevel.FlatRate,
        PricingMode mode = PricingMode.ByTrip,
        IReadOnlyList<LocationRate>? locationRates = null,
        bool deactivated = false,
        string? vatNumber = null)
    {
        await using var db = CreateDbContext();
        var customer = await CustomerTestSupport.SeedCustomerAsync(
            db, companyName, vatNumber, taxCode: null, deactivated: deactivated);
        await SeedContractAsync(
            db, customer.Id, validFrom, validTo, level, mode,
            nightSurcharge: false, rate: level == BillingLevel.FlatRate ? DayOnly() : null, locationRates: locationRates);
        return customer;
    }

    [Fact]
    public async Task FindDriverAsync_ReturnsTheNameOfTheDriver_AndNullForAnUnknownId()
    {
        await using var db = CreateDbContext();
        var driver = await DriverTestSupport.SeedDriverAsync(db, "Luca", "Di Febo");

        var found = await ReadAsync(r => r.FindDriverAsync(driver.Id.Value, Ct));
        var unknown = await ReadAsync(r => r.FindDriverAsync(Guid.NewGuid(), Ct));

        found.ShouldBe(new TripDriver("Luca", "Di Febo"));
        unknown.ShouldBeNull();
    }

    [Fact]
    public async Task ListVehiclesAsync_OffersActiveTractorsAndStandaloneVehiclesAsLeading_AndActiveTrailersAlone()
    {
        await using var db = CreateDbContext();
        await VehicleTestSupport.SeedVehicleAsync(db, "ZZ111ZZ", VehicleType.Tractor, requiresTrailer: true);
        await VehicleTestSupport.SeedVehicleAsync(db, "AA111AA", VehicleType.StandaloneVehicle);
        await VehicleTestSupport.SeedVehicleAsync(db, "BB111BB", VehicleType.Trailer);
        await VehicleTestSupport.SeedVehicleAsync(db, "CC111CC", VehicleType.Tractor, decommissioned: true);
        await VehicleTestSupport.SeedVehicleAsync(db, "DD111DD", VehicleType.Trailer, decommissioned: true);

        var leading = await ReadAsync(r => r.ListVehiclesAsync(TripVehicleRole.Leading, null, Ct));
        var trailers = await ReadAsync(r => r.ListVehiclesAsync(TripVehicleRole.Trailer, null, Ct));

        leading.Select(v => (v.LicensePlate, v.Kind, v.RequiresTrailer)).ShouldBe(
        [
            ("AA111AA", TripVehicleKind.StandaloneVehicle, false),
            ("ZZ111ZZ", TripVehicleKind.Tractor, true),
        ]);
        trailers.Select(v => (v.LicensePlate, v.Kind)).ShouldBe([("BB111BB", TripVehicleKind.Trailer)]);
    }

    [Fact]
    public async Task ListVehiclesAsync_SearchesThePlateAndTheDescription()
    {
        await using var db = CreateDbContext();
        await VehicleTestSupport.SeedVehicleAsync(db, "AB456CD");
        await VehicleTestSupport.SeedVehicleAsync(db, "EF789GH");

        var byPlate = await ReadAsync(r => r.ListVehiclesAsync(TripVehicleRole.Leading, "ab 456", Ct));
        var byDescription = await ReadAsync(r => r.ListVehiclesAsync(TripVehicleRole.Leading, "stralis", Ct));
        var none = await ReadAsync(r => r.ListVehiclesAsync(TripVehicleRole.Leading, "volvo", Ct));

        byPlate.Select(v => v.LicensePlate).ShouldBe(["AB456CD"]);
        byDescription.Count.ShouldBe(2);
        none.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListCustomersAsync_OffersTheActiveCustomersWithAContractInForceThatDay_ByName()
    {
        await SeedCustomerWithContractAsync("Zeta Srl", Day, null, vatNumber: CustomerTestSupport.VatNumberA);
        await SeedCustomerWithContractAsync("Alfa Srl", Day.AddMonths(-3), Day.AddMonths(3));
        await SeedCustomerWithContractAsync("Ended Srl", Day.AddMonths(-3), Day.AddDays(-1));
        await SeedCustomerWithContractAsync("Future Srl", Day.AddDays(1), null);
        await SeedCustomerWithContractAsync("Off Srl", Day.AddMonths(-3), null, deactivated: true);
        await using (var db = CreateDbContext())
        {
            await CustomerTestSupport.SeedCustomerAsync(db, "Bare Srl", "12345678903", taxCode: null);
        }

        var customers = await ReadAsync(r => r.ListCustomersAsync(Day, null, Ct));

        customers.Select(c => c.CompanyName).ShouldBe(["Alfa Srl", "Zeta Srl"]);
        customers[0].VatNumber.ShouldBeNull();
        customers[1].VatNumber.ShouldBe(CustomerTestSupport.VatNumberA);
    }

    [Fact]
    public async Task ListCustomersAsync_ListsACustomerOnTheFirstAndTheLastDayOfItsContract()
    {
        await SeedCustomerWithContractAsync("Alfa Srl", Day, Day.AddDays(10));

        var onFirst = await ReadAsync(r => r.ListCustomersAsync(Day, null, Ct));
        var onLast = await ReadAsync(r => r.ListCustomersAsync(Day.AddDays(10), null, Ct));
        var before = await ReadAsync(r => r.ListCustomersAsync(Day.AddDays(-1), null, Ct));
        var after = await ReadAsync(r => r.ListCustomersAsync(Day.AddDays(11), null, Ct));

        onFirst.Count.ShouldBe(1);
        onLast.Count.ShouldBe(1);
        before.ShouldBeEmpty();
        after.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListCustomersAsync_SearchesTheCompanyName()
    {
        await SeedCustomerWithContractAsync("Comune di Penne", Day, null);
        await SeedCustomerWithContractAsync("Acme Srl", Day, null);

        var found = await ReadAsync(r => r.ListCustomersAsync(Day, "penne", Ct));

        found.Select(c => c.CompanyName).ShouldBe(["Comune di Penne"]);
    }

    [Fact]
    public async Task ListCustomersAsync_SearchesTheVatNumberAsTheBackOfficeDoes_WithSpacesAndThePrefix()
    {
        await SeedCustomerWithContractAsync("Zeta Srl", Day, null, vatNumber: CustomerTestSupport.VatNumberA);
        await SeedCustomerWithContractAsync("Alfa Srl", Day, null, vatNumber: CustomerTestSupport.VatNumberB);

        var whole = await ReadAsync(r => r.ListCustomersAsync(Day, "IT 01455 280683", Ct));
        var part = await ReadAsync(r => r.ListCustomersAsync(Day, "5528", Ct));
        var barePrefix = await ReadAsync(r => r.ListCustomersAsync(Day, "it", Ct));

        whole.Select(c => c.CompanyName).ShouldBe(["Zeta Srl"]);
        part.Select(c => c.CompanyName).ShouldBe(["Zeta Srl"]);
        barePrefix.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListLocationsAsync_OffersTheActiveLocationsByNameThenMunicipality_AndSearchesBoth()
    {
        await using var db = CreateDbContext();
        await LocationTestSupport.SeedLocationAsync(db, "Discarica", "Teramo");
        await LocationTestSupport.SeedLocationAsync(db, "Discarica", "Chieti");
        await LocationTestSupport.SeedLocationAsync(db, "Cantiere", "Pescara");
        await LocationTestSupport.SeedLocationAsync(db, "Chiusa", "Pescara", deactivated: true);

        var all = await ReadAsync(r => r.ListLocationsAsync(null, Ct));
        var byName = await ReadAsync(r => r.ListLocationsAsync("cantiere", Ct));
        var byMunicipality = await ReadAsync(r => r.ListLocationsAsync("TERAMO", Ct));

        all.Select(l => (l.Name, l.Municipality)).ShouldBe(
            [("Cantiere", "Pescara"), ("Discarica", "Chieti"), ("Discarica", "Teramo")]);
        byName.Select(l => l.Name).ShouldBe(["Cantiere"]);
        byMunicipality.Select(l => l.Municipality).ShouldBe(["Teramo"]);
    }

    [Theory]
    [InlineData(BillingLevel.FlatRate, TripLocationsAsked.None)]
    [InlineData(BillingLevel.PerPickupLocation, TripLocationsAsked.Pickup)]
    [InlineData(BillingLevel.PerDeliveryLocation, TripLocationsAsked.Delivery)]
    [InlineData(BillingLevel.PerLocationPair, TripLocationsAsked.Both)]
    public async Task FindQuestionsAsync_MapsTheBillingLevelToTheLocationsAsked(
        BillingLevel level, TripLocationsAsked expected)
    {
        var customer = await SeedCustomerWithContractAsync("Alfa Srl", Day, null, level);

        var questions = await ReadAsync(r => r.FindQuestionsAsync(customer.Id.Value, Day, Ct));

        questions.ShouldNotBeNull().LocationsAsked.ShouldBe(expected);
    }

    [Theory]
    [InlineData(PricingMode.ByHour, TripMeasureKind.StartAndEnd)]
    [InlineData(PricingMode.ByWeight, TripMeasureKind.Weight)]
    [InlineData(PricingMode.ByDistance, TripMeasureKind.Distance)]
    [InlineData(PricingMode.ByTrip, TripMeasureKind.StartOnly)]
    public async Task FindQuestionsAsync_MapsThePricingModeToTheMeasure(PricingMode mode, TripMeasureKind expected)
    {
        var customer = await SeedCustomerWithContractAsync("Alfa Srl", Day, null, mode: mode);

        var questions = await ReadAsync(r => r.FindQuestionsAsync(customer.Id.Value, Day, Ct));

        questions.ShouldNotBeNull().Measure.ShouldBe(expected);
    }

    [Fact]
    public async Task FindQuestionsAsync_OffersTheRatedPairsWhoseLocationsAreAllActive()
    {
        Location a, b, c, off;
        await using (var db = CreateDbContext())
        {
            a = await LocationTestSupport.SeedLocationAsync(db, "Alfa", "Pescara");
            b = await LocationTestSupport.SeedLocationAsync(db, "Beta", "Chieti");
            c = await LocationTestSupport.SeedLocationAsync(db, "Gamma", "Teramo");
            off = await LocationTestSupport.SeedLocationAsync(db, "Spenta", "Teramo");
        }

        var customer = await SeedCustomerWithContractAsync(
            "Alfa Srl", Day, null, BillingLevel.PerLocationPair,
            locationRates:
            [
                new LocationRate(b.Id, c.Id, DayOnly()),
                new LocationRate(a.Id, c.Id, DayOnly()),
                new LocationRate(a.Id, off.Id, DayOnly()),
            ]);
        await using (var db = CreateDbContext())
        {
            var spenta = await db.Locations.FindAsync([off.Id], Ct);
            spenta!.Deactivate(new SystemClock(), FakeCurrentUser.ActorId);
            await db.SaveChangesAsync(Ct);
        }

        var questions = await ReadAsync(r => r.FindQuestionsAsync(customer.Id.Value, Day, Ct));

        questions.ShouldNotBeNull().Places
            .Select(p => (p.Pickup!.Name, p.Delivery!.Name))
            .ShouldBe([("Alfa", "Gamma"), ("Beta", "Gamma")]);
    }

    [Fact]
    public async Task FindQuestionsAsync_SortsThePlacesByNameIgnoringCaseAndAccents_LikeTheOtherLists()
    {
        Location lower, accented, upper;
        await using (var db = CreateDbContext())
        {
            lower = await LocationTestSupport.SeedLocationAsync(db, "beta", "Chieti");
            accented = await LocationTestSupport.SeedLocationAsync(db, "Èra", "Pescara");
            upper = await LocationTestSupport.SeedLocationAsync(db, "Alfa", "Teramo");
        }

        var customer = await SeedCustomerWithContractAsync(
            "Alfa Srl", Day, null, BillingLevel.PerPickupLocation,
            locationRates:
            [
                new LocationRate(accented.Id, null, DayOnly()),
                new LocationRate(lower.Id, null, DayOnly()),
                new LocationRate(upper.Id, null, DayOnly()),
            ]);

        var questions = await ReadAsync(r => r.FindQuestionsAsync(customer.Id.Value, Day, Ct));

        questions.ShouldNotBeNull().Places.Select(p => p.Pickup!.Name).ShouldBe(["Alfa", "beta", "Èra"]);
    }

    [Fact]
    public async Task Lists_WithAWhitespaceOnlySearch_BehaveAsWithNoSearch()
    {
        await using (var db = CreateDbContext())
        {
            await VehicleTestSupport.SeedVehicleAsync(db, "AB456CD");
            await LocationTestSupport.SeedLocationAsync(db, "Cantiere", "Pescara");
        }

        await SeedCustomerWithContractAsync("Alfa Srl", Day, null);

        var vehicles = await ReadAsync(r => r.ListVehiclesAsync(TripVehicleRole.Leading, "   ", Ct));
        var customers = await ReadAsync(r => r.ListCustomersAsync(Day, "   ", Ct));
        var locations = await ReadAsync(r => r.ListLocationsAsync("   ", Ct));

        vehicles.Count.ShouldBe(1);
        customers.Count.ShouldBe(1);
        locations.Count.ShouldBe(1);
    }

    [Fact]
    public async Task FindQuestionsAsync_OffersASingleLocationForAPerDeliveryContract_AndNoPlaceForAFlatRate()
    {
        Location a;
        await using (var db = CreateDbContext())
        {
            a = await LocationTestSupport.SeedLocationAsync(db, "Alfa", "Pescara");
        }

        var perDelivery = await SeedCustomerWithContractAsync(
            "Alfa Srl", Day, null, BillingLevel.PerDeliveryLocation,
            locationRates: [new LocationRate(null, a.Id, DayOnly())]);
        var flat = await SeedCustomerWithContractAsync("Beta Srl", Day, null);

        var deliveryQuestions = await ReadAsync(r => r.FindQuestionsAsync(perDelivery.Id.Value, Day, Ct));
        var flatQuestions = await ReadAsync(r => r.FindQuestionsAsync(flat.Id.Value, Day, Ct));

        var place = deliveryQuestions.ShouldNotBeNull().Places.ShouldHaveSingleItem();
        place.Pickup.ShouldBeNull();
        place.Delivery.ShouldNotBeNull().Name.ShouldBe("Alfa");
        flatQuestions.ShouldNotBeNull().Places.ShouldBeEmpty();
    }

    [Fact]
    public async Task FindQuestionsAsync_PicksTheContractInForceOnTheDay()
    {
        Customer customer;
        await using (var db = CreateDbContext())
        {
            customer = await CustomerTestSupport.SeedCustomerAsync(db, "Alfa Srl", null, taxCode: null);
            await SeedContractAsync(
                db, customer.Id, Day.AddMonths(-6), Day.AddDays(-1), BillingLevel.FlatRate, PricingMode.ByHour,
                nightSurcharge: false, rate: DayOnly());
            await SeedContractAsync(
                db, customer.Id, Day, null, BillingLevel.FlatRate, PricingMode.ByWeight,
                nightSurcharge: false, rate: DayOnly());
        }

        var before = await ReadAsync(r => r.FindQuestionsAsync(customer.Id.Value, Day.AddDays(-1), Ct));
        var onDay = await ReadAsync(r => r.FindQuestionsAsync(customer.Id.Value, Day, Ct));

        before.ShouldNotBeNull().Measure.ShouldBe(TripMeasureKind.StartAndEnd);
        onDay.ShouldNotBeNull().Measure.ShouldBe(TripMeasureKind.Weight);
    }

    [Fact]
    public async Task FindQuestionsAsync_IsNullForADeactivatedCustomer_AnUnknownOne_AndADayWithoutContract()
    {
        var off = await SeedCustomerWithContractAsync("Off Srl", Day, null, deactivated: true);
        var active = await SeedCustomerWithContractAsync("Alfa Srl", Day, null);

        var deactivated = await ReadAsync(r => r.FindQuestionsAsync(off.Id.Value, Day, Ct));
        var unknown = await ReadAsync(r => r.FindQuestionsAsync(Guid.NewGuid(), Day, Ct));
        var earlier = await ReadAsync(r => r.FindQuestionsAsync(active.Id.Value, Day.AddDays(-1), Ct));

        deactivated.ShouldBeNull();
        unknown.ShouldBeNull();
        earlier.ShouldBeNull();
    }

    [Fact]
    public async Task DescribeAsync_ReturnsDecommissionedAndDeactivatedRowsToo()
    {
        await using var db = CreateDbContext();
        var vehicle = await VehicleTestSupport.SeedVehicleAsync(db, "AB456CD", decommissioned: true);
        var customer = await CustomerTestSupport.SeedCustomerAsync(db, "Off Srl", null, taxCode: null, deactivated: true);
        var location = await LocationTestSupport.SeedLocationAsync(db, "Chiusa", "Pescara", deactivated: true);

        var names = await ReadAsync(r => r.DescribeAsync(
            [vehicle.Id.Value, Guid.NewGuid()], [customer.Id.Value], [location.Id.Value], Ct));

        names.Vehicles.Select(v => v.LicensePlate).ShouldBe(["AB456CD"]);
        names.Customers.Select(c => c.CompanyName).ShouldBe(["Off Srl"]);
        names.Locations.Select(l => l.Name).ShouldBe(["Chiusa"]);
    }

    [Fact]
    public void TheTypesOperationsReceives_NameNoTermOfAContract()
    {
        string[] forbidden = ["price", "rate", "amount", "minimum", "night", "band"];

        // The pricing of a trip is for the trip's record, never for a driver response: these types and this
        // member carry it from the Registry to the trip that keeps it.
        string[] forTheRecord =
            [nameof(PricedTrip), nameof(TripPricing), nameof(TripRateSource), nameof(TripMinimumKind), nameof(TripToPrice)];
        var types = typeof(ITripRegistry).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(ITripRegistry).Namespace
                && (t.Name.StartsWith("Trip", StringComparison.Ordinal) || t == typeof(ITripRegistry))
                && !forTheRecord.Contains(t.Name));

        var offending = types
            .SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Select(m => $"{t.Name}.{m.Name}")
                .Append(t.Name))
            .Where(n => n != $"{nameof(ITripRegistry)}.{nameof(ITripRegistry.PriceTripAsync)}")
            .Where(n => forbidden.Any(f => n.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        offending.ShouldBeEmpty();
    }
}
