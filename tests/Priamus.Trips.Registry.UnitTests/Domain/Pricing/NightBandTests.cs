using Priamus.Trips.Registry.Domain.Pricing;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Registry.UnitTests.Domain.Pricing;

public sealed class NightBandTests
{
    [Fact]
    public void Default_IsTenPmToSixAm()
    {
        // Act
        var band = NightBand.Default;

        // Assert
        band.Start.ShouldBe(new TimeOnly(22, 0));
        band.End.ShouldBe(new TimeOnly(6, 0));
    }

    [Theory]
    [InlineData("22:00", true)]
    [InlineData("05:59", true)]
    [InlineData("06:00", false)]
    [InlineData("21:59", false)]
    [InlineData("00:00", true)]
    [InlineData("12:00", false)]
    public void IsNight_AtTheBoundariesOfTheDefaultBand_IncludesTheStartAndExcludesTheEnd(string start, bool expected)
    {
        // Arrange
        var band = NightBand.Default;

        // Act
        var isNight = band.IsNight(TimeOnly.Parse(start));

        // Assert
        isNight.ShouldBe(expected);
    }

    [Theory]
    [InlineData("09:00", true)]
    [InlineData("16:59", true)]
    [InlineData("17:00", false)]
    [InlineData("08:59", false)]
    [InlineData("23:00", false)]
    public void IsNight_WithABandInsideTheDay_IsNightOnlyBetweenStartAndEnd(string start, bool expected)
    {
        // Arrange
        var band = NightBand.Create(new TimeOnly(9, 0), new TimeOnly(17, 0)).Value;

        // Act
        var isNight = band.IsNight(TimeOnly.Parse(start));

        // Assert
        isNight.ShouldBe(expected);
    }

    [Fact]
    public void Create_WithEndEqualToStart_Fails()
    {
        // Act
        var result = NightBand.Create(new TimeOnly(22, 0), new TimeOnly(22, 0));

        // Assert
        result.Error.ShouldBe(PricingErrors.NightBandEndEqualsStart);
    }

    [Fact]
    public void Create_WithEndInTheSameMinuteAsStart_Fails()
    {
        // Act
        var result = NightBand.Create(new TimeOnly(22, 0, 10), new TimeOnly(22, 0, 50));

        // Assert
        result.Error.ShouldBe(PricingErrors.NightBandEndEqualsStart);
    }

    [Fact]
    public void Create_WithSecondsAndFractions_KeepsWholeMinutes()
    {
        // Act
        var band = NightBand.Create(new TimeOnly(21, 30, 45, 500), new TimeOnly(5, 15, 59)).Value;

        // Assert
        band.Start.ShouldBe(new TimeOnly(21, 30));
        band.End.ShouldBe(new TimeOnly(5, 15));
    }
}
