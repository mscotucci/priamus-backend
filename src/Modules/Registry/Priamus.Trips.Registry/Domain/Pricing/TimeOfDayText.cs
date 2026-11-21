using System.Globalization;

namespace Priamus.Trips.Registry.Domain.Pricing;

/// <summary>
/// The <c>HH:mm</c> text of a time of day, the one definition that reading and writing share.
/// </summary>
internal static class TimeOfDayText
{
    /// <summary>The format of the text, with a literal colon.</summary>
    internal const string Format = "HH':'mm";

    /// <summary>The <c>HH:mm</c> text of <paramref name="time"/>, whatever the current culture.</summary>
    internal static string ToText(TimeOnly time) => time.ToString(Format, CultureInfo.InvariantCulture);
}
