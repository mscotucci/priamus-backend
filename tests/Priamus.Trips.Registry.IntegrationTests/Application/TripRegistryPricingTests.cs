using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.Domain.Pricing;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.ContractTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Application;

/// <summary>
/// The checks and the pricing of <see cref="ITripRegistry"/> against the per-test PostgreSQL database: the
/// answers of a trip judged against the Registry, and a trip priced with the contract in force on its day.
/// </summary>
public sealed class TripRegistryPricingTests : RegistryIntegrationTestBase
{
    private static readonly DateOnly Day = new(2026, 6, 15);
    private static readonly TimeOnly Noon = new(12, 0);

    public TripRegistryPricingTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<T> ReadAsync<T>(Func<ITripRegistry, Task<T>> read)
    {
        await using var factory = ApiFactory.Create(ConnectionString, useTestAuthentication: true);
        using var scope = factory.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<ITripRegistry>());
    }

    private Task<Result<PricedTrip>> PriceAsync(TripToPrice trip) => ReadAsync(r => r.PriceTripAsync(trip, Ct));

    private Task<Result> ValidateAsync(TripAnswers answers) => ReadAsync(r => r.ValidateTripAsync(answers, Ct));

    private static TripAnswers AnswersOf(TripToPrice trip) => new(
        trip.LeadingVehicleId, trip.TrailerId, trip.CustomerId, trip.Day, trip.PickupLocationId, trip.DeliveryLocationId);

    private Task<TripAnswersCheck> CheckAsync(TripAnswers answers) => ReadAsync(r => r.CheckAnswersAsync(answers, Ct));

    private static Rate NightRate(MinimumRule? minimum = null) =>
        Rate.Create(45m, 55m, 5m, minimum ?? new MinimumRule.None()).Value;

    private static TripToPrice TripOf(
        Customer customer,
        Vehicle vehicle,
        DateOnly? day = null,
        TimeOnly? start = null,
        Vehicle? trailer = null,
        Location? pickup = null,
        Location? delivery = null,
        TimeOnly? end = null,
        int? kilograms = null,
        int? kilometres = null) =>
        new(
            customer.Id.Value,
            day ?? Day,
            vehicle.Id.Value,
            trailer?.Id.Value,
            pickup?.Id.Value,
            delivery?.Id.Value,
            start ?? Noon,
            end ?? Noon.AddHours(1),
            kilograms,
            kilometres);

    private async Task<Customer> SeedCustomerAsync(string name = "Alfa Srl", bool deactivated = false)
    {
        await using var db = CreateDbContext();
        return await CustomerTestSupport.SeedCustomerAsync(db, name, null, null, deactivated: deactivated);
    }

    private async Task<Vehicle> SeedVehicleAsync(
        string plate = "AB456CD",
        VehicleType type = VehicleType.Tractor,
        bool requiresTrailer = false,
        bool decommissioned = false)
    {
        await using var db = CreateDbContext();
        return await VehicleTestSupport.SeedVehicleAsync(db, plate, type, requiresTrailer, decommissioned);
    }

    private async Task<Location> SeedLocationAsync(string name, bool deactivated = false)
    {
        await using var db = CreateDbContext();
        return await LocationTestSupport.SeedLocationAsync(db, name, "Pescara", deactivated: deactivated);
    }

    private async Task<Contract> SeedContractFor(
        Customer customer,
        BillingLevel level = BillingLevel.FlatRate,
        PricingMode mode = PricingMode.ByTrip,
        Rate? rate = null,
        bool nightSurcharge = true,
        IReadOnlyList<VehicleRate>? vehicleRates = null,
        IReadOnlyList<LocationRate>? locationRates = null,
        DateOnly? validFrom = null,
        DateOnly? validTo = null,
        RequiredDocument[]? documents = null)
    {
        await using var db = CreateDbContext();
        return await SeedContractAsync(
            db,
            customer.Id,
            validFrom ?? Day.AddMonths(-1),
            validTo,
            level,
            mode,
            documents,
            nightSurcharge,
            level == BillingLevel.FlatRate ? rate ?? NightRate() : null,
            vehicleRates,
            locationRates ?? (level == BillingLevel.FlatRate ? [] : null));
    }

    /// <summary>The engine's own answer on the contract as stored, the night band as stored now.</summary>
    private async Task<PriceCalculation> EngineAsync(
        Guid contractId, Vehicle vehicle, Location? pickup, Location? delivery, TripMeasure measure)
    {
        await using var db = CreateDbContext();
        var contract = await db.Contracts.AsNoTracking().SingleAsync(c => c.Id == new ContractId(contractId), Ct);
        var band = (await db.NightBandSettings.AsNoTracking().SingleAsync(Ct)).Band;
        var terms = new PricingTerms(
            contract.PricingMode, contract.NightSurcharge, contract.BillingLevel,
            contract.Rate, contract.VehicleRates, contract.LocationRates);
        return PricingEngine.Calculate(terms, vehicle.Id, pickup?.Id, delivery?.Id, measure, band).Value;
    }

    private static void ShouldBeAnswersChanged(Result result)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Registry.Trip.AnswersChanged");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    private static void ShouldBeNotPriceable(Result<PricedTrip> result)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Registry.Trip.NotPriceable");
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    // ── Pricing: the contract of the day, day and night ──────────────────────

    [Fact]
    public async Task PriceTripAsync_OnADayTrip_KeepsTheContractTheRateAndEveryStepOfTheCalculation()
    {
        var customer = await SeedCustomerAsync();
        var tractor = await SeedVehicleAsync();
        var contract = await SeedContractFor(customer, documents: [RequiredDocument.DailySummary, RequiredDocument.WasteForm]);

        var result = await PriceAsync(TripOf(customer, tractor));

        var priced = result.Value;
        priced.ContractId.ShouldBe(contract.Id.Value);
        priced.Measure.ShouldBe(TripMeasureKind.StartOnly);
        priced.RequiredDocuments.ShouldBe(["WasteForm", "DailySummary"], ignoreOrder: true);
        priced.Pricing.ShouldBe(new TripPricing(
            TripRateSource.Contract, null, null, null,
            DayPrice: 45m, NightPrice: 55m, NightCallFee: 5m,
            TripMinimumKind.None, null, null, null,
            new TimeOnly(22, 0), new TimeOnly(6, 0), NightSurcharge: true, IsNight: false,
            Quantity: null, UnitPrice: 45m, BaseAmount: 45m, AmountAfterMinimum: 45m, MinimumApplied: false,
            AppliedNightCallFee: null, Total: 45m));
    }

    [Fact]
    public async Task PriceTripAsync_OnANightTrip_PricesWithTheNightPriceAndAddsTheCallFee()
    {
        var customer = await SeedCustomerAsync();
        var tractor = await SeedVehicleAsync();
        await SeedContractFor(customer);

        var priced = (await PriceAsync(TripOf(customer, tractor, start: new TimeOnly(23, 30)))).Value;

        priced.Pricing.IsNight.ShouldBeTrue();
        priced.Pricing.UnitPrice.ShouldBe(55m);
        priced.Pricing.AppliedNightCallFee.ShouldBe(5m);
        priced.Pricing.Total.ShouldBe(60m);
    }

    [Fact]
    public async Task PriceTripAsync_ClassifiesTheTripWithTheNightBandOfThatMoment()
    {
        var customer = await SeedCustomerAsync();
        var tractor = await SeedVehicleAsync();
        await SeedContractFor(customer);
        var trip = TripOf(customer, tractor, start: new TimeOnly(21, 0));
        var before = (await PriceAsync(trip)).Value;

        await using (var db = CreateDbContext())
        {
            var setting = await db.NightBandSettings.SingleAsync(Ct);
            setting.Update(
                NightBand.Create(new TimeOnly(20, 0), new TimeOnly(5, 0)).Value,
                new SystemClock(),
                FakeCurrentUser.ActorId);
            await db.SaveChangesAsync(Ct);
        }

        var after = (await PriceAsync(trip)).Value;

        before.Pricing.IsNight.ShouldBeFalse();
        before.Pricing.Total.ShouldBe(45m);
        after.Pricing.IsNight.ShouldBeTrue();
        after.Pricing.Total.ShouldBe(60m);
        after.Pricing.NightBandStart.ShouldBe(new TimeOnly(20, 0));
        after.Pricing.NightBandEnd.ShouldBe(new TimeOnly(5, 0));
    }

    [Fact]
    public async Task PriceTripAsync_PicksTheContractInForceOnTheTripDay_AmongTwoOfTheCustomer()
    {
        var customer = await SeedCustomerAsync();
        var tractor = await SeedVehicleAsync();
        var first = await SeedContractFor(
            customer, rate: Rate.Create(45m, 55m, 5m, new MinimumRule.None()).Value,
            validFrom: Day.AddMonths(-3), validTo: Day.AddDays(-1));
        var second = await SeedContractFor(
            customer, rate: Rate.Create(70m, 80m, 5m, new MinimumRule.None()).Value, validFrom: Day);

        var onFirst = (await PriceAsync(TripOf(customer, tractor, day: Day.AddDays(-1)))).Value;
        var onSecond = (await PriceAsync(TripOf(customer, tractor, day: Day))).Value;

        onFirst.ContractId.ShouldBe(first.Id.Value);
        onFirst.Pricing.Total.ShouldBe(45m);
        onSecond.ContractId.ShouldBe(second.Id.Value);
        onSecond.Pricing.Total.ShouldBe(70m);
    }

    // ── Pricing: every pricing mode, as the engine gives it ──────────────────

    [Theory]
    [InlineData(PricingMode.ByHour, TripMeasureKind.StartAndEnd, 180, 135)]
    [InlineData(PricingMode.ByWeight, TripMeasureKind.Weight, 12000, 540)]
    [InlineData(PricingMode.ByDistance, TripMeasureKind.Distance, 100, 4500)]
    [InlineData(PricingMode.ByTrip, TripMeasureKind.StartOnly, null, 45)]
    public async Task PriceTripAsync_MeasuresByThePricingModeOfTheContract(
        PricingMode mode, TripMeasureKind kind, int? quantity, int total)
    {
        var customer = await SeedCustomerAsync();
        var tractor = await SeedVehicleAsync();
        var contract = await SeedContractFor(customer, mode: mode);
        var start = new TimeOnly(8, 0);
        var trip = TripOf(
            customer, tractor, start: start,
            end: mode == PricingMode.ByHour ? new TimeOnly(11, 0) : null,
            kilograms: mode == PricingMode.ByWeight ? 12000 : null,
            kilometres: mode == PricingMode.ByDistance ? 100 : null);

        var priced = (await PriceAsync(trip)).Value;

        TripMeasure measure = mode switch
        {
            PricingMode.ByHour => TripMeasure.ByHour.Create(start, new TimeOnly(11, 0)).Value,
            PricingMode.ByWeight => TripMeasure.ByWeight.Create(12000, start).Value,
            PricingMode.ByDistance => TripMeasure.ByDistance.Create(100, start).Value,
            _ => new TripMeasure.ByTrip(start),
        };
        var expected = await EngineAsync(contract.Id.Value, tractor, null, null, measure);
        priced.Measure.ShouldBe(kind);
        priced.Pricing.Quantity.ShouldBe(quantity);
        priced.Pricing.Total.ShouldBe(total);
        priced.Pricing.Total.ShouldBe(expected.Total);
        priced.Pricing.BaseAmount.ShouldBe(expected.BaseAmount);
        priced.Pricing.UnitPrice.ShouldBe(expected.UnitPrice);
    }

    [Theory]
    [InlineData(PricingMode.ByHour, 12000, null)]
    [InlineData(PricingMode.ByHour, null, 100)]
    [InlineData(PricingMode.ByWeight, null, null)]
    [InlineData(PricingMode.ByWeight, 12000, 100)]
    [InlineData(PricingMode.ByWeight, null, 100)]
    [InlineData(PricingMode.ByDistance, null, null)]
    [InlineData(PricingMode.ByDistance, 12000, 100)]
    [InlineData(PricingMode.ByDistance, 12000, null)]
    [InlineData(PricingMode.ByTrip, 12000, null)]
    [InlineData(PricingMode.ByTrip, null, 100)]
    public async Task PriceTripAsync_WhenTheMeasureIsNotTheOneOfThePricingMode_AnswersMeasureChanged(
        PricingMode mode, int? kilograms, int? kilometres)
    {
        var customer = await SeedCustomerAsync();
        var tractor = await SeedVehicleAsync();
        await SeedContractFor(customer, mode: mode);

        var result = await PriceAsync(TripOf(customer, tractor, kilograms: kilograms, kilometres: kilometres));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Registry.Trip.MeasureChanged");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    [Theory]
    [InlineData(PricingMode.ByWeight, 12000, null)]
    [InlineData(PricingMode.ByDistance, null, 100)]
    [InlineData(PricingMode.ByTrip, null, null)]
    public async Task PriceTripAsync_UsesTheEndTimeOnlyWhenThePricingModeIsByHour(
        PricingMode mode, int? kilograms, int? kilometres)
    {
        var customer = await SeedCustomerAsync();
        var tractor = await SeedVehicleAsync();
        await SeedContractFor(customer, mode: mode);

        var early = await PriceAsync(TripOf(
            customer, tractor, end: new TimeOnly(12, 30), kilograms: kilograms, kilometres: kilometres));
        var late = await PriceAsync(TripOf(
            customer, tractor, end: new TimeOnly(18, 0), kilograms: kilograms, kilometres: kilometres));

        early.Value.Pricing.Total.ShouldBe(late.Value.Pricing.Total);
        early.Value.Pricing.Quantity.ShouldBe(late.Value.Pricing.Quantity);
    }

    [Fact]
    public async Task PriceTripAsync_WhenAMinimumRuleChangesTheAmount_KeepsTheRuleAndThatItApplied()
    {
        var customer = await SeedCustomerAsync();
        var tractor = await SeedVehicleAsync();
        await SeedContractFor(
            customer, mode: PricingMode.ByHour, rate: NightRate(new MinimumRule.FixedMinimum(200m)));

        var priced = (await PriceAsync(
            TripOf(customer, tractor, start: new TimeOnly(8, 0), end: new TimeOnly(10, 0)))).Value;

        priced.Pricing.MinimumKind.ShouldBe(TripMinimumKind.FixedMinimum);
        priced.Pricing.MinimumAmount.ShouldBe(200m);
        priced.Pricing.BaseAmount.ShouldBe(90m);
        priced.Pricing.AmountAfterMinimum.ShouldBe(200m);
        priced.Pricing.MinimumApplied.ShouldBeTrue();
        priced.Pricing.Total.ShouldBe(200m);
    }

    [Fact]
    public async Task PriceTripAsync_WithATieredOverage_KeepsItsThresholdAndOveragePrice()
    {
        var customer = await SeedCustomerAsync();
        var tractor = await SeedVehicleAsync();
        await SeedContractFor(
            customer, mode: PricingMode.ByDistance, rate: NightRate(new MinimumRule.TieredOverage(100m, 50m, 2m)));

        var priced = (await PriceAsync(TripOf(customer, tractor, kilometres: 60))).Value;

        priced.Pricing.MinimumKind.ShouldBe(TripMinimumKind.TieredOverage);
        priced.Pricing.MinimumAmount.ShouldBe(100m);
        priced.Pricing.MinimumThreshold.ShouldBe(50m);
        priced.Pricing.MinimumOveragePrice.ShouldBe(2m);
        priced.Pricing.AmountAfterMinimum.ShouldBe(120m);
    }

    // ── Pricing: the source of the rate ──────────────────────────────────────

    [Fact]
    public async Task PriceTripAsync_OnAFlatRateContract_UsesTheRateOfAVehicleThatHasOne()
    {
        var customer = await SeedCustomerAsync();
        var tractor = await SeedVehicleAsync("AB456CD");
        var other = await SeedVehicleAsync("EF789GH");
        var ownRate = Rate.Create(30m, 40m, 2m, new MinimumRule.None()).Value;
        await SeedContractFor(customer, vehicleRates: [new VehicleRate(tractor.Id, null, null, ownRate)]);

        var own = (await PriceAsync(TripOf(customer, tractor))).Value;
        var plain = (await PriceAsync(TripOf(customer, other))).Value;

        own.Pricing.RateSource.ShouldBe(TripRateSource.Vehicle);
        own.Pricing.RateVehicleId.ShouldBe(tractor.Id.Value);
        own.Pricing.DayPrice.ShouldBe(30m);
        own.Pricing.Total.ShouldBe(30m);
        plain.Pricing.RateSource.ShouldBe(TripRateSource.Contract);
        plain.Pricing.RateVehicleId.ShouldBeNull();
        plain.Pricing.Total.ShouldBe(45m);
    }

    [Fact]
    public async Task PriceTripAsync_PerPickupLocation_UsesTheRateOfThePickup_AndKeepsWhereItCameFrom()
    {
        var customer = await SeedCustomerAsync();
        var tractor = await SeedVehicleAsync();
        var pickup = await SeedLocationAsync("Cantiere");
        var extra = await SeedLocationAsync("Discarica");
        await SeedContractFor(
            customer, BillingLevel.PerPickupLocation,
            locationRates: [new LocationRate(pickup.Id, null, Rate.Create(33m, 44m, 3m, new MinimumRule.None()).Value)]);

        var priced = (await PriceAsync(TripOf(customer, tractor, pickup: pickup, delivery: extra))).Value;

        priced.Pricing.RateSource.ShouldBe(TripRateSource.Location);
        priced.Pricing.RatePickupLocationId.ShouldBe(pickup.Id.Value);
        priced.Pricing.RateDeliveryLocationId.ShouldBeNull();
        priced.Pricing.Total.ShouldBe(33m);
    }

    [Fact]
    public async Task PriceTripAsync_PerDeliveryLocation_UsesTheRateOfTheDelivery_AndKeepsWhereItCameFrom()
    {
        var customer = await SeedCustomerAsync();
        var tractor = await SeedVehicleAsync();
        var delivery = await SeedLocationAsync("Discarica");
        await SeedContractFor(
            customer, BillingLevel.PerDeliveryLocation,
            locationRates: [new LocationRate(null, delivery.Id, Rate.Create(33m, 44m, 3m, new MinimumRule.None()).Value)]);

        var priced = (await PriceAsync(TripOf(customer, tractor, delivery: delivery))).Value;

        priced.Pricing.RateSource.ShouldBe(TripRateSource.Location);
        priced.Pricing.RatePickupLocationId.ShouldBeNull();
        priced.Pricing.RateDeliveryLocationId.ShouldBe(delivery.Id.Value);
        priced.Pricing.Total.ShouldBe(33m);
    }

    [Fact]
    public async Task PriceTripAsync_PerLocationPair_UsesThePairRate_OrTheVehicleRateOnThatPair()
    {
        var customer = await SeedCustomerAsync();
        var tractor = await SeedVehicleAsync("AB456CD");
        var other = await SeedVehicleAsync("EF789GH");
        var pickup = await SeedLocationAsync("Cantiere");
        var delivery = await SeedLocationAsync("Discarica");
        var pairRate = Rate.Create(80m, 90m, 3m, new MinimumRule.None()).Value;
        var ownRate = Rate.Create(60m, 70m, 3m, new MinimumRule.None()).Value;
        await SeedContractFor(
            customer, BillingLevel.PerLocationPair,
            locationRates: [new LocationRate(pickup.Id, delivery.Id, pairRate)],
            vehicleRates: [new VehicleRate(tractor.Id, pickup.Id, delivery.Id, ownRate)]);

        var pair = (await PriceAsync(TripOf(customer, other, pickup: pickup, delivery: delivery))).Value;
        var own = (await PriceAsync(TripOf(customer, tractor, pickup: pickup, delivery: delivery))).Value;

        pair.Pricing.RateSource.ShouldBe(TripRateSource.LocationPair);
        pair.Pricing.RatePickupLocationId.ShouldBe(pickup.Id.Value);
        pair.Pricing.RateDeliveryLocationId.ShouldBe(delivery.Id.Value);
        pair.Pricing.Total.ShouldBe(80m);
        own.Pricing.RateSource.ShouldBe(TripRateSource.Vehicle);
        own.Pricing.RateVehicleId.ShouldBe(tractor.Id.Value);
        own.Pricing.RatePickupLocationId.ShouldBe(pickup.Id.Value);
        own.Pricing.Total.ShouldBe(60m);
    }

    [Fact]
    public async Task PriceTripAsync_WithATrailerBehindATractorThatRequiresOne_Prices()
    {
        var customer = await SeedCustomerAsync();
        var tractor = await SeedVehicleAsync("AB456CD", requiresTrailer: true);
        var trailer = await SeedVehicleAsync("EF789GH", VehicleType.Trailer);
        await SeedContractFor(customer);

        var result = await PriceAsync(TripOf(customer, tractor, trailer: trailer));

        result.IsSuccess.ShouldBeTrue();
    }

    // ── Pricing checks no status ─────────────────────────────────────────────

    [Fact]
    public async Task PriceTripAsync_WhenTheVehicleTrailerCustomerOrLocationLostItsStatus_StillPrices()
    {
        var customer = await SeedCustomerAsync(deactivated: true);
        var decommissioned = await SeedVehicleAsync("AA111AA", decommissioned: true);
        var decommissionedTrailer = await SeedVehicleAsync("BB111BB", VehicleType.Trailer, decommissioned: true);
        var gone = await SeedLocationAsync("Chiusa", deactivated: true);
        await SeedContractFor(customer);

        var result = await PriceAsync(TripOf(
            customer, decommissioned, trailer: decommissionedTrailer, pickup: gone, delivery: gone));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Pricing.Total.ShouldBe(45m);
    }

    [Theory]
    [InlineData(PricingMode.ByHour, TripMeasureKind.StartAndEnd)]
    [InlineData(PricingMode.ByTrip, TripMeasureKind.StartOnly)]
    [InlineData(PricingMode.ByWeight, TripMeasureKind.Weight)]
    [InlineData(PricingMode.ByDistance, TripMeasureKind.Distance)]
    public async Task FindMeasureAsync_AsksTheMeasureOfTheContractInForce_WhateverTheStatusOfTheCustomer(
        PricingMode mode, TripMeasureKind expected)
    {
        var customer = await SeedCustomerAsync(deactivated: true);
        await SeedContractFor(customer, mode: mode);

        var measure = await ReadAsync(r => r.FindMeasureAsync(customer.Id.Value, Day, Ct));

        measure.ShouldBe(expected);
    }

    [Fact]
    public async Task FindMeasureAsync_WhenNoContractOfTheCustomerCoversTheDay_AnswersNull()
    {
        var customer = await SeedCustomerAsync();
        await SeedContractFor(customer, validTo: Day.AddDays(-1));

        var measure = await ReadAsync(r => r.FindMeasureAsync(customer.Id.Value, Day, Ct));

        measure.ShouldBeNull();
    }

    [Fact]
    public async Task PriceTripAsync_WhenTheCustomerHasNoContractCoveringTheDay_AnswersNotPriceable()
    {
        var customer = await SeedCustomerAsync("Alfa Srl");
        var future = await SeedCustomerAsync("Gamma Srl");
        var ended = await SeedCustomerAsync("Delta Srl");
        var tractor = await SeedVehicleAsync();
        await SeedContractFor(customer);
        await SeedContractFor(future, validFrom: Day.AddDays(1));
        await SeedContractFor(ended, validTo: Day.AddDays(-1));

        ShouldBeNotPriceable(await PriceAsync(TripOf(future, tractor)));
        ShouldBeNotPriceable(await PriceAsync(TripOf(ended, tractor)));
        ShouldBeNotPriceable(await PriceAsync(TripOf(customer, tractor) with { CustomerId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task PriceTripAsync_WhenTheLocationTheLevelNamesIsMissingOrUnrated_AnswersNotPriceable()
    {
        var customer = await SeedCustomerAsync();
        var tractor = await SeedVehicleAsync();
        var rated = await SeedLocationAsync("Cantiere");
        var unrated = await SeedLocationAsync("Discarica");
        await SeedContractFor(
            customer, BillingLevel.PerPickupLocation,
            locationRates: [new LocationRate(rated.Id, null, NightRate())]);

        ShouldBeNotPriceable(await PriceAsync(TripOf(customer, tractor)));
        ShouldBeNotPriceable(await PriceAsync(TripOf(customer, tractor, pickup: unrated)));
        (await PriceAsync(TripOf(customer, tractor, pickup: rated))).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task PriceTripAsync_OnAPairContract_AnswersNotPriceableForAPairWithoutARate()
    {
        var customer = await SeedCustomerAsync();
        var tractor = await SeedVehicleAsync();
        var pickup = await SeedLocationAsync("Cantiere");
        var delivery = await SeedLocationAsync("Discarica");
        var other = await SeedLocationAsync("Magazzino");
        await SeedContractFor(
            customer, BillingLevel.PerLocationPair,
            locationRates: [new LocationRate(pickup.Id, delivery.Id, NightRate())]);

        ShouldBeNotPriceable(await PriceAsync(TripOf(customer, tractor, pickup: pickup, delivery: other)));
        ShouldBeNotPriceable(await PriceAsync(TripOf(customer, tractor, pickup: pickup)));
        (await PriceAsync(TripOf(customer, tractor, pickup: pickup, delivery: delivery))).IsSuccess.ShouldBeTrue();
    }

    // ── Validating the answers of a trip ─────────────────────────────────────

    [Theory]
    [InlineData("DecommissionedVehicle")]
    [InlineData("TrailerAsLeadingVehicle")]
    [InlineData("MissingRequiredTrailer")]
    [InlineData("TrailerWithStandaloneVehicle")]
    [InlineData("DecommissionedTrailer")]
    [InlineData("TrailerThatIsNotATrailer")]
    [InlineData("LeadingVehicleAlsoTheTrailer")]
    [InlineData("UnknownVehicle")]
    [InlineData("DeactivatedCustomer")]
    [InlineData("CustomerWithoutContractThatDay")]
    [InlineData("UnknownCustomer")]
    public async Task ValidateTripAsync_WhenAVehicleOrCustomerFactDoesNotHold_AnswersTheOneConflict(string broken)
    {
        var customer = await SeedCustomerAsync("Alfa Srl");
        var deactivated = await SeedCustomerAsync("Beta Srl", deactivated: true);
        var future = await SeedCustomerAsync("Gamma Srl");
        await SeedContractFor(customer);
        await SeedContractFor(deactivated);
        await SeedContractFor(future, validFrom: Day.AddDays(1));
        var tractor = await SeedVehicleAsync("AA111AA");
        var needsTrailer = await SeedVehicleAsync("BB111BB", requiresTrailer: true);
        var standalone = await SeedVehicleAsync("CC111CC", VehicleType.StandaloneVehicle);
        var trailer = await SeedVehicleAsync("DD111DD", VehicleType.Trailer);
        var decommissioned = await SeedVehicleAsync("EE111EE", decommissioned: true);
        var decommissionedTrailer = await SeedVehicleAsync("FF111FF", VehicleType.Trailer, decommissioned: true);

        var trip = broken switch
        {
            "DecommissionedVehicle" => TripOf(customer, decommissioned),
            "TrailerAsLeadingVehicle" => TripOf(customer, trailer),
            "MissingRequiredTrailer" => TripOf(customer, needsTrailer),
            "TrailerWithStandaloneVehicle" => TripOf(customer, standalone, trailer: trailer),
            "DecommissionedTrailer" => TripOf(customer, tractor, trailer: decommissionedTrailer),
            "TrailerThatIsNotATrailer" => TripOf(customer, tractor, trailer: standalone),
            "LeadingVehicleAlsoTheTrailer" => TripOf(customer, tractor, trailer: tractor),
            "UnknownVehicle" => TripOf(customer, tractor) with { LeadingVehicleId = Guid.NewGuid() },
            "DeactivatedCustomer" => TripOf(deactivated, tractor),
            "CustomerWithoutContractThatDay" => TripOf(future, tractor),
            _ => TripOf(customer, tractor) with { CustomerId = Guid.NewGuid() },
        };

        ShouldBeAnswersChanged(await ValidateAsync(AnswersOf(trip)));
    }

    [Fact]
    public async Task ValidateTripAsync_WhenTheLeadingVehicleOrTheCustomerIsNotGiven_AnswersTheOneConflict()
    {
        var customer = await SeedCustomerAsync();
        var tractor = await SeedVehicleAsync();
        await SeedContractFor(customer);
        var answers = AnswersOf(TripOf(customer, tractor));

        ShouldBeAnswersChanged(await ValidateAsync(answers with { VehicleId = null }));
        ShouldBeAnswersChanged(await ValidateAsync(answers with { CustomerId = null }));
        (await ValidateAsync(answers)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateTripAsync_JudgesTheContractInForceOnTheDayGiven()
    {
        var customer = await SeedCustomerAsync();
        var tractor = await SeedVehicleAsync();
        await SeedContractFor(customer, validFrom: Day);
        var answers = AnswersOf(TripOf(customer, tractor));

        (await ValidateAsync(answers)).IsSuccess.ShouldBeTrue();
        ShouldBeAnswersChanged(await ValidateAsync(answers with { Day = Day.AddDays(-1) }));
    }

    [Fact]
    public async Task ValidateTripAsync_WhenTheLocationTheLevelNamesIsMissingUnratedOrInactive_AnswersTheOneConflict()
    {
        var customer = await SeedCustomerAsync();
        var tractor = await SeedVehicleAsync();
        var rated = await SeedLocationAsync("Cantiere");
        var unrated = await SeedLocationAsync("Discarica");
        var gone = await SeedLocationAsync("Chiusa", deactivated: true);
        await SeedContractFor(
            customer, BillingLevel.PerPickupLocation,
            locationRates: [new LocationRate(rated.Id, null, NightRate())]);

        ShouldBeAnswersChanged(await ValidateAsync(AnswersOf(TripOf(customer, tractor))));
        ShouldBeAnswersChanged(await ValidateAsync(AnswersOf(TripOf(customer, tractor, pickup: unrated))));
        ShouldBeAnswersChanged(await ValidateAsync(AnswersOf(TripOf(customer, tractor, pickup: rated, delivery: gone))));
        (await ValidateAsync(AnswersOf(TripOf(customer, tractor, pickup: rated)))).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateTripAsync_OnAPairContract_AnswersTheConflictForAPairWithoutARate()
    {
        var customer = await SeedCustomerAsync();
        var tractor = await SeedVehicleAsync();
        var pickup = await SeedLocationAsync("Cantiere");
        var delivery = await SeedLocationAsync("Discarica");
        var other = await SeedLocationAsync("Magazzino");
        await SeedContractFor(
            customer, BillingLevel.PerLocationPair,
            locationRates: [new LocationRate(pickup.Id, delivery.Id, NightRate())]);

        ShouldBeAnswersChanged(await ValidateAsync(AnswersOf(TripOf(customer, tractor, pickup: pickup, delivery: other))));
        ShouldBeAnswersChanged(await ValidateAsync(AnswersOf(TripOf(customer, tractor, pickup: pickup))));
        (await ValidateAsync(AnswersOf(TripOf(customer, tractor, pickup: pickup, delivery: delivery)))).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateTripAsync_OnAFlatRateContract_AnswersTheConflictForADeactivatedOptionalLocation()
    {
        var customer = await SeedCustomerAsync();
        var tractor = await SeedVehicleAsync();
        var gone = await SeedLocationAsync("Chiusa", deactivated: true);
        var active = await SeedLocationAsync("Cantiere");
        await SeedContractFor(customer);

        ShouldBeAnswersChanged(await ValidateAsync(AnswersOf(TripOf(customer, tractor, pickup: gone))));
        (await ValidateAsync(AnswersOf(TripOf(customer, tractor, pickup: active)))).IsSuccess.ShouldBeTrue();
    }

    // ── Refusals of the measure ──────────────────────────────────────────────

    [Theory]
    [InlineData(PricingMode.ByWeight, 100_000, null, null, "Registry.Pricing.KilogramsTooLarge")]
    [InlineData(PricingMode.ByWeight, 0, null, null, "Registry.Pricing.QuantityNotPositive")]
    [InlineData(PricingMode.ByDistance, null, 10_000, null, "Registry.Pricing.KilometresTooLarge")]
    [InlineData(PricingMode.ByDistance, null, 0, null, "Registry.Pricing.QuantityNotPositive")]
    [InlineData(PricingMode.ByHour, null, null, "12:00", "Registry.Pricing.TripEndEqualsStart")]
    public async Task PriceTripAsync_WhenTheMeasureIsOutOfRange_AnswersAValidationError(
        PricingMode mode, int? kilograms, int? kilometres, string? end, string code)
    {
        var customer = await SeedCustomerAsync();
        var tractor = await SeedVehicleAsync();
        await SeedContractFor(customer, mode: mode);

        var result = await PriceAsync(TripOf(
            customer, tractor, kilograms: kilograms, kilometres: kilometres, end: end is null ? null : TimeOnly.Parse(end)));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public async Task PriceTripAsync_WhenTheAmountIsAboveWhatTheSystemHolds_AnswersNotPriceable()
    {
        var customer = await SeedCustomerAsync();
        var tractor = await SeedVehicleAsync();
        await SeedContractFor(
            customer, mode: PricingMode.ByDistance, rate: Rate.Create(2_000_000m, 2_100_000m, 0m, new MinimumRule.None()).Value);

        var result = await PriceAsync(TripOf(customer, tractor, kilometres: 9_999));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Registry.Trip.NotPriceable");
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    // ── Checking the answers ─────────────────────────────────────────────────

    [Fact]
    public async Task CheckAnswersAsync_WhenEverythingHolds_AnswersTrueForEveryAnswer()
    {
        var customer = await SeedCustomerAsync();
        var tractor = await SeedVehicleAsync("AB456CD", requiresTrailer: true);
        var trailer = await SeedVehicleAsync("EF789GH", VehicleType.Trailer);
        var pickup = await SeedLocationAsync("Cantiere");
        var delivery = await SeedLocationAsync("Discarica");
        await SeedContractFor(
            customer, BillingLevel.PerLocationPair,
            locationRates: [new LocationRate(pickup.Id, delivery.Id, NightRate())]);

        var check = await CheckAsync(new TripAnswers(
            tractor.Id.Value, trailer.Id.Value, customer.Id.Value, Day, pickup.Id.Value, delivery.Id.Value));

        check.ShouldBe(new TripAnswersCheck(true, true, true, true, true));
    }

    [Fact]
    public async Task CheckAnswersAsync_WhenNothingIsGiven_AnswersTrue_ThereIsNothingToEmpty()
    {
        var check = await CheckAsync(new TripAnswers(null, null, null, Day, null, null));

        check.ShouldBe(new TripAnswersCheck(true, true, true, true, true));
    }

    [Fact]
    public async Task CheckAnswersAsync_AnswersFalseForADecommissionedVehicleAndTrailer_AndForATrailerBehindAStandaloneVehicle()
    {
        var customer = await SeedCustomerAsync();
        var decommissioned = await SeedVehicleAsync("AA111AA", decommissioned: true);
        var standalone = await SeedVehicleAsync("BB111BB", VehicleType.StandaloneVehicle);
        var trailer = await SeedVehicleAsync("CC111CC", VehicleType.Trailer);
        var decommissionedTrailer = await SeedVehicleAsync("DD111DD", VehicleType.Trailer, decommissioned: true);
        await SeedContractFor(customer);

        var vehicle = await CheckAsync(new TripAnswers(
            decommissioned.Id.Value, trailer.Id.Value, customer.Id.Value, Day, null, null));
        var behindStandalone = await CheckAsync(new TripAnswers(
            standalone.Id.Value, trailer.Id.Value, customer.Id.Value, Day, null, null));
        var oldTrailer = await CheckAsync(new TripAnswers(
            standalone.Id.Value, decommissionedTrailer.Id.Value, null, Day, null, null));

        vehicle.ShouldBe(new TripAnswersCheck(false, true, true, true, true));
        behindStandalone.ShouldBe(new TripAnswersCheck(true, false, true, true, true));
        oldTrailer.Trailer.ShouldBeFalse();
    }

    [Fact]
    public async Task CheckAnswersAsync_AnswersFalseForTheTrailer_WhenItIsTheLeadingVehicle()
    {
        var tractor = await SeedVehicleAsync("AB456CD");

        var check = await CheckAsync(new TripAnswers(tractor.Id.Value, tractor.Id.Value, null, Day, null, null));

        check.ShouldBe(new TripAnswersCheck(true, false, true, true, true));
    }

    [Fact]
    public async Task CheckAnswersAsync_AnswersFalseForACustomerWithoutAContractThatDay_AndForItsLocations()
    {
        var customer = await SeedCustomerAsync("Alfa Srl");
        var deactivated = await SeedCustomerAsync("Beta Srl", deactivated: true);
        var pickup = await SeedLocationAsync("Cantiere");
        await SeedContractFor(customer, validFrom: Day.AddMonths(-3), validTo: Day.AddDays(-1));
        await SeedContractFor(deactivated);

        var ended = await CheckAsync(new TripAnswers(null, null, customer.Id.Value, Day, pickup.Id.Value, null));
        var stillInForce = await CheckAsync(
            new TripAnswers(null, null, customer.Id.Value, Day.AddDays(-1), pickup.Id.Value, null));
        var off = await CheckAsync(new TripAnswers(null, null, deactivated.Id.Value, Day, pickup.Id.Value, null));
        var unknown = await CheckAsync(new TripAnswers(null, null, Guid.NewGuid(), Day, null, null));

        ended.ShouldBe(new TripAnswersCheck(true, true, false, false, true));
        stillInForce.ShouldBe(new TripAnswersCheck(true, true, true, true, true));
        off.Customer.ShouldBeFalse();
        unknown.Customer.ShouldBeFalse();
    }

    [Fact]
    public async Task CheckAnswersAsync_AnswersFalseForALocationWithoutARateAndForADeactivatedLocation()
    {
        var customer = await SeedCustomerAsync();
        var rated = await SeedLocationAsync("Cantiere");
        var unrated = await SeedLocationAsync("Discarica");
        var gone = await SeedLocationAsync("Chiusa", deactivated: true);
        await SeedContractFor(
            customer, BillingLevel.PerPickupLocation,
            locationRates: [new LocationRate(rated.Id, null, NightRate())]);

        var unratedPickup = await CheckAsync(
            new TripAnswers(null, null, customer.Id.Value, Day, unrated.Id.Value, rated.Id.Value));
        var goneDelivery = await CheckAsync(
            new TripAnswers(null, null, customer.Id.Value, Day, rated.Id.Value, gone.Id.Value));

        unratedPickup.ShouldBe(new TripAnswersCheck(true, true, true, false, true));
        goneDelivery.ShouldBe(new TripAnswersCheck(true, true, true, true, false));
    }

    [Fact]
    public async Task CheckAnswersAsync_JudgesTheLocationsAgainstTheContractOfTheDay()
    {
        var customer = await SeedCustomerAsync();
        var oldPickup = await SeedLocationAsync("Cantiere");
        var newPickup = await SeedLocationAsync("Discarica");
        await SeedContractFor(
            customer, BillingLevel.PerPickupLocation,
            locationRates: [new LocationRate(oldPickup.Id, null, NightRate())],
            validFrom: Day.AddMonths(-3), validTo: Day.AddDays(-1));
        await SeedContractFor(
            customer, BillingLevel.PerPickupLocation,
            locationRates: [new LocationRate(newPickup.Id, null, NightRate())],
            validFrom: Day);

        var before = await CheckAsync(
            new TripAnswers(null, null, customer.Id.Value, Day.AddDays(-1), oldPickup.Id.Value, null));
        var after = await CheckAsync(new TripAnswers(null, null, customer.Id.Value, Day, oldPickup.Id.Value, null));

        before.Pickup.ShouldBeTrue();
        after.Pickup.ShouldBeFalse();
    }
}
