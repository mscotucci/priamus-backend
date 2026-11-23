using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Pricing;

namespace Priamus.Trips.Registry.Features.SimulatePrice;

/// <summary>
/// The night band a simulation classified the trip with.
/// </summary>
/// <param name="Start">First minute of the band, <c>HH:mm</c>.</param>
/// <param name="End">First minute after the band, <c>HH:mm</c>.</param>
public sealed record SimulatedNightBandDto(string Start, string End);

/// <summary>
/// A simulated amount with the steps that led to it, as the engine computed them.
/// </summary>
/// <param name="Total">The amount: <paramref name="AmountAfterMinimum"/> plus <paramref name="NightCallFee"/>.</param>
/// <param name="Band">Day or night, by the start time.</param>
/// <param name="NightSurcharge">Whether the terms have the night surcharge.</param>
/// <param name="NightBand">The night band the trip was classified with.</param>
/// <param name="RateSource">Where the rate came from.</param>
/// <param name="VehicleId">The vehicle whose rate was used; <c>null</c> when no vehicle rate was used.</param>
/// <param name="Quantity">Minutes, kilograms or kilometres; <c>null</c> for a trip priced by trip.</param>
/// <param name="UnitPrice">The price used: day or night, of the rate chosen.</param>
/// <param name="BaseAmount">The amount before the minimum rule.</param>
/// <param name="MinimumRule">The kind of minimum rule of the rate chosen.</param>
/// <param name="MinimumAmount">The minimum amount M; <c>null</c> unless the rule has one.</param>
/// <param name="MinimumThreshold">The threshold X; <c>null</c> unless the rule is tiered overage.</param>
/// <param name="MinimumOveragePrice">The overage price Y; <c>null</c> unless the rule is tiered overage.</param>
/// <param name="AmountAfterMinimum">The amount after the minimum rule, before the night call fee.</param>
/// <param name="MinimumApplied">Whether the minimum rule changed the base amount.</param>
/// <param name="NightCallFee">The night call fee added; <c>null</c> when none is added.</param>
public sealed record PriceSimulationDto(
    decimal Total,
    TimeBand Band,
    bool NightSurcharge,
    SimulatedNightBandDto NightBand,
    RateSource RateSource,
    Guid? VehicleId,
    int? Quantity,
    decimal UnitPrice,
    decimal BaseAmount,
    MinimumRuleKind MinimumRule,
    decimal? MinimumAmount,
    decimal? MinimumThreshold,
    decimal? MinimumOveragePrice,
    decimal AmountAfterMinimum,
    bool MinimumApplied,
    decimal? NightCallFee)
{
    /// <summary>Builds the DTO of <paramref name="calculation"/>.</summary>
    internal static PriceSimulationDto From(PriceCalculation calculation) =>
        new(
            calculation.Total,
            calculation.Band,
            calculation.NightSurcharge,
            new SimulatedNightBandDto(
                TimeOfDayText.ToText(calculation.NightBand.Start),
                TimeOfDayText.ToText(calculation.NightBand.End)),
            calculation.RateSource,
            calculation.VehicleId?.Value,
            calculation.Quantity,
            calculation.UnitPrice,
            calculation.BaseAmount,
            calculation.Minimum.Kind,
            calculation.Minimum.Amount,
            calculation.Minimum.Threshold,
            calculation.Minimum.OveragePrice,
            calculation.AmountAfterMinimum,
            calculation.MinimumApplied,
            calculation.NightCallFee);
}
