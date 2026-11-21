using System.Globalization;
using Priamus.Trips.Registry.Domain.Pricing;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Registry.UnitTests.Domain.Pricing;

public sealed class TimeOfDayTextTests
{
    [Theory]
    [InlineData(0, 0, "00:00")]
    [InlineData(6, 5, "06:05")]
    [InlineData(22, 0, "22:00")]
    [InlineData(23, 59, "23:59")]
    public void ToText_TimeOfDay_IsHoursAndMinutesWithTwoDigits(int hour, int minute, string expected)
    {
        // Act
        var text = TimeOfDayText.ToText(new TimeOnly(hour, minute));

        // Assert
        text.ShouldBe(expected);
    }

    [Fact]
    public void ToText_CultureWithAnotherTimeSeparator_StillUsesAColon()
    {
        // Arrange
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("fi-FI");

        try
        {
            // Act
            var text = TimeOfDayText.ToText(new TimeOnly(22, 0));

            // Assert
            text.ShouldBe("22:00");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
