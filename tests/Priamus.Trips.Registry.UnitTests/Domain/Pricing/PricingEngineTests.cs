using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Pricing;
using Priamus.Trips.Registry.Domain.Vehicles;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Registry.UnitTests.Domain.Pricing;

public sealed class PricingEngineTests
{
    private static readonly NightBand Band = NightBand.Default;
    private static readonly VehicleId Tanker = VehicleId.New();
    private static readonly VehicleId OtherVehicle = VehicleId.New();

    private static Rate MakeRate(decimal day, decimal? night = null, decimal? fee = null, MinimumRule? minimum = null) =>
        Rate.Create(day, night, fee, minimum ?? new MinimumRule.None()).Value;

    private static PricingTerms Terms(
        PricingMode mode,
        Rate rate,
        bool nightSurcharge = false,
        params VehicleRate[] vehicleRates) =>
        new(mode, nightSurcharge, rate, vehicleRates);

    private static TripMeasure Hours(string start, string end) =>
        TripMeasure.ByHour.Create(TimeOnly.Parse(start), TimeOnly.Parse(end)).Value;

    private static TripMeasure Weight(int kilograms, string start = "10:00") =>
        TripMeasure.ByWeight.Create(kilograms, TimeOnly.Parse(start)).Value;

    private static TripMeasure Distance(int kilometres, string start = "10:00") =>
        TripMeasure.ByDistance.Create(kilometres, TimeOnly.Parse(start)).Value;

    private static PriceCalculation Calculate(PricingTerms terms, TripMeasure trip, VehicleId? vehicle = null) =>
        PricingEngine.Calculate(terms, vehicle, trip, Band).Value;

    public static TheoryData<string, PricingTerms, VehicleId?, TripMeasure, decimal> PrdExamples()
    {
        var beta = Terms(PricingMode.ByHour, MakeRate(10m, minimum: new MinimumRule.FixedMinimum(30m)));
        var gamma = Terms(PricingMode.ByHour, MakeRate(20m, minimum: new MinimumRule.TieredOverage(50m, 4m, 12m)));
        var delta = Terms(
            PricingMode.ByHour,
            MakeRate(30m, 42m, 35m),
            true,
            new VehicleRate(Tanker, MakeRate(55m, 72m, 50m)));
        var epsilon = Terms(PricingMode.ByHour, MakeRate(20m, 30m, 25m), true);
        var zeta = Terms(PricingMode.ByDistance, MakeRate(1.20m, 1.60m, 30m), true);
        var eta = Terms(PricingMode.ByTrip, MakeRate(150m, 200m, 40m), true);

        return new()
        {
            { "12.1 2 h", beta, null, Hours("10:00", "12:00"), 30m },
            { "12.1 5 h", beta, null, Hours("10:00", "15:00"), 50m },
            { "12.2 3 h", gamma, null, Hours("10:00", "13:00"), 50m },
            { "12.2 4 h", gamma, null, Hours("10:00", "14:00"), 50m },
            { "12.2 6 h", gamma, null, Hours("10:00", "16:00"), 74m },
            { "12.3 A", delta, null, Hours("10:00", "13:00"), 90m },
            { "12.3 B", delta, null, Hours("23:00", "02:00"), 161m },
            { "12.3 C", delta, Tanker, Hours("10:00", "13:00"), 165m },
            { "12.3 D", delta, Tanker, Hours("23:00", "02:00"), 266m },
            { "12.4 day", epsilon, null, Hours("10:00", "13:00"), 60m },
            { "12.4 night", epsilon, null, Hours("23:00", "02:00"), 115m },
            { "12.5 day", zeta, null, Distance(80, "09:00"), 96m },
            { "12.5 night", zeta, null, Distance(80, "23:00"), 158m },
            { "12.6 day", eta, null, new TripMeasure.ByTrip(new TimeOnly(14, 0)), 150m },
            { "12.6 night", eta, null, new TripMeasure.ByTrip(new TimeOnly(23, 0)), 240m },
        };
    }

    [Theory]
    [MemberData(nameof(PrdExamples))]
    public void Calculate_PrdExample_ReturnsTheStatedAmount(
        string example,
        PricingTerms terms,
        VehicleId? vehicle,
        TripMeasure trip,
        decimal expected)
    {
        // Act
        var result = PricingEngine.Calculate(terms, vehicle, trip, Band);

        // Assert
        result.Value.Total.ShouldBe(expected, example);
    }

    [Fact]
    public void Calculate_ByHourWithTwentyMinutes_UsesTheExactMinutes()
    {
        // Arrange
        var terms = Terms(PricingMode.ByHour, MakeRate(30m));

        // Act
        var calculation = Calculate(terms, Hours("10:00", "12:20"));

        // Assert
        calculation.Total.ShouldBe(70.00m);
        calculation.Quantity.ShouldBe(140);
    }

    [Fact]
    public void Calculate_ByHourWithTenMinutes_RoundsTheAmountToTheCent()
    {
        // Arrange
        var terms = Terms(PricingMode.ByHour, MakeRate(25m));

        // Act
        var calculation = Calculate(terms, Hours("10:00", "12:10"));

        // Assert
        calculation.Total.ShouldBe(54.17m);
    }

    [Fact]
    public void Calculate_WithAHalfCent_RoundsUp()
    {
        // Arrange: 1 minute at 0.30 EUR/h is 0.005 EUR
        var terms = Terms(PricingMode.ByHour, MakeRate(0.30m));

        // Act
        var calculation = Calculate(terms, Hours("10:00", "10:01"));

        // Assert
        calculation.BaseAmount.ShouldBe(0.01m);
    }

    [Fact]
    public void Calculate_ByWeight_PricesKilogramsPerTon()
    {
        // Arrange
        var terms = Terms(PricingMode.ByWeight, MakeRate(18.50m));

        // Act
        var calculation = Calculate(terms, Weight(12_300));

        // Assert
        calculation.Total.ShouldBe(227.55m);
        calculation.Quantity.ShouldBe(12_300);
    }

    [Fact]
    public void Calculate_ByTrip_UsesThePriceWithoutQuantity()
    {
        // Arrange
        var terms = Terms(PricingMode.ByTrip, MakeRate(150m));

        // Act
        var calculation = Calculate(terms, new TripMeasure.ByTrip(new TimeOnly(10, 0)));

        // Assert
        calculation.Total.ShouldBe(150m);
        calculation.Quantity.ShouldBeNull();
        calculation.UnitPrice.ShouldBe(150m);
    }

    [Theory]
    [InlineData("30", "30", false)]
    [InlineData("20", "30", true)]
    [InlineData("40", "40", false)]
    public void Calculate_WithFixedMinimum_RaisesOnlyAmountsBelowIt(string hourlyPrice, string expected, bool applied)
    {
        // Arrange: 1 h at the hourly price against a minimum of 30
        var terms = Terms(
            PricingMode.ByHour,
            MakeRate(decimal.Parse(hourlyPrice, System.Globalization.CultureInfo.InvariantCulture), minimum: new MinimumRule.FixedMinimum(30m)));

        // Act
        var calculation = Calculate(terms, Hours("10:00", "11:00"));

        // Assert
        calculation.Total.ShouldBe(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
        calculation.MinimumApplied.ShouldBe(applied);
    }

    [Fact]
    public void Calculate_NightTripWithFixedMinimum_AddsTheCallFeeAfterTheMinimum()
    {
        // Arrange
        var terms = Terms(
            PricingMode.ByHour,
            MakeRate(30m, 42m, 35m, new MinimumRule.FixedMinimum(100m)),
            nightSurcharge: true);

        // Act
        var calculation = Calculate(terms, Hours("23:00", "00:00"));

        // Assert
        calculation.BaseAmount.ShouldBe(42m);
        calculation.AmountAfterMinimum.ShouldBe(100m);
        calculation.MinimumApplied.ShouldBeTrue();
        calculation.NightCallFee.ShouldBe(35m);
        calculation.Total.ShouldBe(135.00m);
    }

    [Fact]
    public void Calculate_WithTieredOverageByHour_IgnoresTheDayOrNightPriceAndAddsTheCallFeeAfterIt()
    {
        // Arrange
        var terms = Terms(
            PricingMode.ByHour,
            MakeRate(20m, 30m, 35m, new MinimumRule.TieredOverage(50m, 4m, 12m)),
            nightSurcharge: true);

        // Act
        var day = Calculate(terms, Hours("10:00", "13:00"));
        var night = Calculate(terms, Hours("23:00", "02:00"));

        // Assert
        day.Total.ShouldBe(50.00m);
        night.Total.ShouldBe(85.00m);
        night.AmountAfterMinimum.ShouldBe(50.00m);
    }

    [Fact]
    public void Calculate_WithTieredOverageByHour_ComparesTheThresholdInMinutes()
    {
        // Arrange
        var terms = Terms(PricingMode.ByHour, MakeRate(20m, minimum: new MinimumRule.TieredOverage(50m, 4m, 12m)));

        // Act
        var atThreshold = Calculate(terms, Hours("10:00", "14:00"));
        var overThreshold = Calculate(terms, Hours("10:00", "14:20"));

        // Assert
        atThreshold.Total.ShouldBe(50.00m);
        overThreshold.Total.ShouldBe(54.00m);
        overThreshold.MinimumApplied.ShouldBeTrue();
    }

    [Theory]
    [InlineData(3_999, "100")]
    [InlineData(4_000, "100")]
    [InlineData(5_000, "110")]
    [InlineData(4_500, "105")]
    public void Calculate_WithTieredOverageByWeight_ComparesTheThresholdInKilograms(int kilograms, string expected)
    {
        // Arrange: 100 up to 4 t, then 10 per ton
        var terms = Terms(PricingMode.ByWeight, MakeRate(18m, minimum: new MinimumRule.TieredOverage(100m, 4m, 10m)));

        // Act
        var calculation = Calculate(terms, Weight(kilograms));

        // Assert
        calculation.Total.ShouldBe(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Calculate_WithTieredOverageByDistance_PricesTheKilometresBeyondTheThreshold()
    {
        // Arrange
        var terms = Terms(PricingMode.ByDistance, MakeRate(1m, minimum: new MinimumRule.TieredOverage(80m, 50m, 1.5m)));

        // Act
        var calculation = Calculate(terms, Distance(70));

        // Assert
        calculation.Total.ShouldBe(110m);
    }

    [Fact]
    public void Calculate_WithTieredOverage_RoundsTheOverageOnce()
    {
        // Arrange: 1 minute over at 0.30 EUR/h is 0.005 EUR
        var terms = Terms(PricingMode.ByHour, MakeRate(20m, minimum: new MinimumRule.TieredOverage(50m, 1m, 0.30m)));

        // Act
        var calculation = Calculate(terms, Hours("10:00", "11:01"));

        // Assert
        calculation.Total.ShouldBe(50.01m);
    }

    [Theory]
    [InlineData("23:00", "02:00", TimeBand.Night)]
    [InlineData("21:00", "23:30", TimeBand.Day)]
    [InlineData("05:00", "09:00", TimeBand.Night)]
    public void Calculate_ByHour_ClassifiesTheTripByItsStartAlone(string start, string end, TimeBand expected)
    {
        // Arrange
        var terms = Terms(PricingMode.ByHour, MakeRate(20m, 30m, 25m), nightSurcharge: true);

        // Act
        var calculation = Calculate(terms, Hours(start, end));

        // Assert
        calculation.Band.ShouldBe(expected);
    }

    [Theory]
    [InlineData("22:00", TimeBand.Night)]
    [InlineData("05:59", TimeBand.Night)]
    [InlineData("06:00", TimeBand.Day)]
    [InlineData("21:59", TimeBand.Day)]
    public void Calculate_ByWeightAtTheBoundariesOfTheBand_ClassifiesByStart(string start, TimeBand expected)
    {
        // Arrange
        var terms = Terms(PricingMode.ByWeight, MakeRate(20m, 30m, 25m), nightSurcharge: true);

        // Act
        var calculation = Calculate(terms, Weight(1_000, start));

        // Assert
        calculation.Band.ShouldBe(expected);
        calculation.NightBand.ShouldBe(Band);
    }

    [Fact]
    public void Calculate_DayTrip_UsesTheDayPriceAndNoCallFee()
    {
        // Arrange
        var terms = Terms(PricingMode.ByDistance, MakeRate(1.20m, 1.60m, 30m), nightSurcharge: true);

        // Act
        var calculation = Calculate(terms, Distance(80, "09:00"));

        // Assert
        calculation.Band.ShouldBe(TimeBand.Day);
        calculation.UnitPrice.ShouldBe(1.20m);
        calculation.NightCallFee.ShouldBeNull();
    }

    [Fact]
    public void Calculate_NightTripWithSurcharge_UsesTheNightPriceAndTheCallFee()
    {
        // Arrange
        var terms = Terms(PricingMode.ByDistance, MakeRate(1.20m, 1.60m, 30m), nightSurcharge: true);

        // Act
        var calculation = Calculate(terms, Distance(80, "23:00"));

        // Assert
        calculation.Band.ShouldBe(TimeBand.Night);
        calculation.NightSurcharge.ShouldBeTrue();
        calculation.UnitPrice.ShouldBe(1.60m);
        calculation.NightCallFee.ShouldBe(30m);
    }

    [Fact]
    public void Calculate_NightTripWithSurchargeAndZeroCallFee_ReportsTheZeroFee()
    {
        // Arrange
        var terms = Terms(PricingMode.ByTrip, MakeRate(150m, 200m, 0m), nightSurcharge: true);

        // Act
        var calculation = Calculate(terms, new TripMeasure.ByTrip(new TimeOnly(23, 0)));

        // Assert
        calculation.NightCallFee.ShouldBe(0m);
        calculation.Total.ShouldBe(200m);
    }

    [Fact]
    public void Calculate_NightTripWithoutSurcharge_StaysNightWithTheDayPriceAndNoFee()
    {
        // Arrange
        var terms = Terms(PricingMode.ByDistance, MakeRate(1.20m), nightSurcharge: false);

        // Act
        var calculation = Calculate(terms, Distance(80, "23:00"));

        // Assert
        calculation.Band.ShouldBe(TimeBand.Night);
        calculation.NightSurcharge.ShouldBeFalse();
        calculation.UnitPrice.ShouldBe(1.20m);
        calculation.NightCallFee.ShouldBeNull();
        calculation.Total.ShouldBe(96m);
    }

    [Fact]
    public void Calculate_WithTheVehicleOfAVehicleRate_UsesThatRateForEveryStep()
    {
        // Arrange
        var terms = Terms(
            PricingMode.ByHour,
            MakeRate(30m, minimum: new MinimumRule.FixedMinimum(10m)),
            false,
            new VehicleRate(Tanker, MakeRate(55m, minimum: new MinimumRule.FixedMinimum(200m))));

        // Act
        var calculation = Calculate(terms, Hours("10:00", "13:00"), Tanker);

        // Assert
        calculation.RateSource.ShouldBe(RateSource.Vehicle);
        calculation.VehicleId.ShouldBe(Tanker);
        calculation.UnitPrice.ShouldBe(55m);
        calculation.Minimum.ShouldBe(new MinimumRule.FixedMinimum(200m));
        calculation.Total.ShouldBe(200m);
    }

    [Fact]
    public void Calculate_WithAVehicleWithoutARateOfItsOwn_UsesTheContractRate()
    {
        // Arrange
        var terms = Terms(
            PricingMode.ByHour,
            MakeRate(30m),
            false,
            new VehicleRate(Tanker, MakeRate(55m)));

        // Act
        var calculation = Calculate(terms, Hours("10:00", "13:00"), OtherVehicle);

        // Assert
        calculation.RateSource.ShouldBe(RateSource.Contract);
        calculation.VehicleId.ShouldBeNull();
        calculation.Total.ShouldBe(90m);
    }

    [Fact]
    public void Calculate_WithoutAVehicle_UsesTheContractRate()
    {
        // Arrange
        var terms = Terms(PricingMode.ByHour, MakeRate(30m), false, new VehicleRate(Tanker, MakeRate(55m)));

        // Act
        var calculation = Calculate(terms, Hours("10:00", "13:00"));

        // Assert
        calculation.RateSource.ShouldBe(RateSource.Contract);
        calculation.Total.ShouldBe(90m);
    }

    [Fact]
    public void Calculate_WithATotalOverTheMaximum_FailsWithAmountTooLarge()
    {
        // Arrange
        var terms = Terms(PricingMode.ByTrip, MakeRate(Rate.MaxAmount, Rate.MaxAmount, 1m), nightSurcharge: true);

        // Act
        var result = PricingEngine.Calculate(terms, null, new TripMeasure.ByTrip(new TimeOnly(23, 0)), Band);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(PricingErrors.AmountTooLarge);
    }

    [Fact]
    public void Calculate_WithATotalEqualToTheMaximum_Succeeds()
    {
        // Arrange
        var terms = Terms(PricingMode.ByTrip, MakeRate(Rate.MaxAmount));

        // Act
        var result = PricingEngine.Calculate(terms, null, new TripMeasure.ByTrip(new TimeOnly(10, 0)), Band);

        // Assert
        result.Value.Total.ShouldBe(Rate.MaxAmount);
    }

    [Fact]
    public void Calculate_ByWeightAtTheLimitWithTheLargestPrice_FailsWithAmountTooLarge()
    {
        // Arrange
        var terms = Terms(PricingMode.ByWeight, MakeRate(Rate.MaxAmount));

        // Act
        var result = PricingEngine.Calculate(terms, null, Weight(TripMeasure.MaxKilograms), Band);

        // Assert
        result.Error.ShouldBe(PricingErrors.AmountTooLarge);
    }

    [Fact]
    public void Calculate_WithATripOfAnotherMode_Throws()
    {
        // Arrange
        var terms = Terms(PricingMode.ByHour, MakeRate(30m));

        // Act and assert
        Should.Throw<ArgumentException>(() => PricingEngine.Calculate(terms, null, Weight(100), Band));
    }

    [Fact]
    public void Calculate_NightSurchargeWithoutNightPrice_Throws()
    {
        // Arrange
        var terms = Terms(PricingMode.ByTrip, MakeRate(150m), nightSurcharge: true);

        // Act and assert
        Should.Throw<InvalidOperationException>(
            () => PricingEngine.Calculate(terms, null, new TripMeasure.ByTrip(new TimeOnly(23, 0)), Band));
    }

    [Fact]
    public void Calculate_NightSurchargeWithoutNightCallFee_Throws()
    {
        // Arrange
        var terms = Terms(PricingMode.ByTrip, MakeRate(150m, 200m), nightSurcharge: true);

        // Act and assert
        Should.Throw<InvalidOperationException>(
            () => PricingEngine.Calculate(terms, null, new TripMeasure.ByTrip(new TimeOnly(23, 0)), Band));
    }

    [Fact]
    public void Calculate_ByTripWithTieredOverage_Throws()
    {
        // Arrange
        var terms = Terms(PricingMode.ByTrip, MakeRate(150m, minimum: new MinimumRule.TieredOverage(100m, 1m, 10m)));

        // Act and assert
        Should.Throw<InvalidOperationException>(
            () => PricingEngine.Calculate(terms, null, new TripMeasure.ByTrip(new TimeOnly(10, 0)), Band));
    }
}
