using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Pricing;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Registry.UnitTests.Domain.Pricing;

public sealed class TripMeasureTests
{
    private static readonly TimeOnly Start = new(10, 0);

    [Theory]
    [InlineData("10:00", "13:00", 180)]
    [InlineData("23:00", "02:00", 180)]
    [InlineData("10:00", "12:20", 140)]
    [InlineData("00:00", "23:59", 1439)]
    [InlineData("23:59", "00:00", 1)]
    [InlineData("10:01", "10:00", 1439)]
    public void ByHourCreate_WithStartAndEnd_HoldsTheExactMinutes(string start, string end, int minutes)
    {
        // Act
        var result = TripMeasure.ByHour.Create(TimeOnly.Parse(start), TimeOnly.Parse(end));

        // Assert
        result.Value.Minutes.ShouldBe(minutes);
        result.Value.Mode.ShouldBe(PricingMode.ByHour);
        result.Value.Start.ShouldBe(TimeOnly.Parse(start));
        result.Value.End.ShouldBe(TimeOnly.Parse(end));
    }

    [Fact]
    public void ByHourCreate_WithEndEqualToStart_Fails()
    {
        // Act
        var result = TripMeasure.ByHour.Create(Start, Start);

        // Assert
        result.Error.ShouldBe(PricingErrors.TripEndEqualsStart);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ByWeightCreate_WithKilogramsNotPositive_Fails(int kilograms)
    {
        // Act
        var result = TripMeasure.ByWeight.Create(kilograms, Start);

        // Assert
        result.Error.ShouldBe(PricingErrors.QuantityNotPositive);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(99_999)]
    public void ByWeightCreate_WithKilogramsInRange_HoldsThem(int kilograms)
    {
        // Act
        var result = TripMeasure.ByWeight.Create(kilograms, Start);

        // Assert
        result.Value.Kilograms.ShouldBe(kilograms);
        result.Value.Mode.ShouldBe(PricingMode.ByWeight);
    }

    [Fact]
    public void ByWeightCreate_AboveTheLimit_Fails()
    {
        // Act
        var result = TripMeasure.ByWeight.Create(100_000, Start);

        // Assert
        result.Error.ShouldBe(PricingErrors.KilogramsTooLarge);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ByDistanceCreate_WithKilometresNotPositive_Fails(int kilometres)
    {
        // Act
        var result = TripMeasure.ByDistance.Create(kilometres, Start);

        // Assert
        result.Error.ShouldBe(PricingErrors.QuantityNotPositive);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9_999)]
    public void ByDistanceCreate_WithKilometresInRange_HoldsThem(int kilometres)
    {
        // Act
        var result = TripMeasure.ByDistance.Create(kilometres, Start);

        // Assert
        result.Value.Kilometres.ShouldBe(kilometres);
        result.Value.Mode.ShouldBe(PricingMode.ByDistance);
    }

    [Fact]
    public void ByDistanceCreate_AboveTheLimit_Fails()
    {
        // Act
        var result = TripMeasure.ByDistance.Create(10_000, Start);

        // Assert
        result.Error.ShouldBe(PricingErrors.KilometresTooLarge);
    }

    [Fact]
    public void ByTrip_HoldsItsStartAndMode()
    {
        // Act
        var trip = new TripMeasure.ByTrip(Start);

        // Assert
        trip.Start.ShouldBe(Start);
        trip.Mode.ShouldBe(PricingMode.ByTrip);
    }
}
