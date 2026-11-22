using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Vehicles;

namespace Priamus.Trips.Registry.Domain.Pricing;

/// <summary>Whether a trip starts in the night band.</summary>
public enum TimeBand
{
    /// <summary>The start is outside the night band.</summary>
    Day,

    /// <summary>The start is inside the night band.</summary>
    Night,
}

/// <summary>Where the rate of a computation came from.</summary>
public enum RateSource
{
    /// <summary>The rate of the contract.</summary>
    Contract,

    /// <summary>The rate a vehicle has of its own in the contract.</summary>
    Vehicle,

    /// <summary>The rate of the pickup location or of the delivery location.</summary>
    Location,

    /// <summary>The rate of the pair of pickup and delivery location.</summary>
    LocationPair,
}

/// <summary>The amount of a trip with the steps that led to it.</summary>
/// <param name="Total">The amount: <paramref name="AmountAfterMinimum"/> plus <paramref name="NightCallFee"/>.</param>
/// <param name="Band">Day or night, by the start time.</param>
/// <param name="NightSurcharge">Whether the contract has the night surcharge.</param>
/// <param name="NightBand">The night band the trip was classified with.</param>
/// <param name="RateSource">Where the rate came from.</param>
/// <param name="VehicleId">The vehicle whose rate was used; <c>null</c> for the contract's rate.</param>
/// <param name="Quantity">Minutes, kilograms or kilometres; <c>null</c> for a trip priced by trip.</param>
/// <param name="UnitPrice">The price used: day or night, of the rate chosen.</param>
/// <param name="BaseAmount">The amount before the minimum rule.</param>
/// <param name="Minimum">The minimum rule of the rate chosen.</param>
/// <param name="AmountAfterMinimum">The amount after the minimum rule, before the night call fee.</param>
/// <param name="MinimumApplied">Whether the minimum rule changed the base amount.</param>
/// <param name="NightCallFee">The night call fee added; <c>null</c> when none is added.</param>
public sealed record PriceCalculation(
    decimal Total,
    TimeBand Band,
    bool NightSurcharge,
    NightBand NightBand,
    RateSource RateSource,
    VehicleId? VehicleId,
    int? Quantity,
    decimal UnitPrice,
    decimal BaseAmount,
    MinimumRule Minimum,
    decimal AmountAfterMinimum,
    bool MinimumApplied,
    decimal? NightCallFee);
