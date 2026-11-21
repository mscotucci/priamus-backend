using Priamus.Trips.Registry.Domain.NightBands;
using Priamus.Trips.Registry.Domain.Pricing;

namespace Priamus.Trips.Registry.Features.GetNightBand;

/// <summary>
/// The system night band as the API shows it.
/// </summary>
/// <param name="Start">First minute of the band, <c>HH:mm</c>.</param>
/// <param name="End">First minute after the band, <c>HH:mm</c>.</param>
/// <param name="Version">Version to send back with a change.</param>
public sealed record NightBandDto(string Start, string End, int Version)
{
    /// <summary>Builds the DTO of <paramref name="setting"/>.</summary>
    internal static NightBandDto From(NightBandSetting setting) =>
        new(
            TimeOfDayText.ToText(setting.Band.Start),
            TimeOfDayText.ToText(setting.Band.End),
            setting.Version);
}
