namespace Priamus.Trips.Registry.Contracts;

/// <summary>
/// What a trip was priced with and every step of the calculation. For the record of the trip, never for a
/// driver response.
/// </summary>
/// <param name="RateSource">Where the rate came from.</param>
/// <param name="RateVehicleId">The vehicle whose own rate was used; <c>null</c> when none.</param>
/// <param name="RatePickupLocationId">The pickup location of the rate used; <c>null</c> when the rate has none.</param>
/// <param name="RateDeliveryLocationId">The delivery location of the rate used; <c>null</c> when the rate has none.</param>
/// <param name="DayPrice">The day price of the rate used.</param>
/// <param name="NightPrice">The night price of the rate used; <c>null</c> without the night surcharge.</param>
/// <param name="NightCallFee">The night call fee of the rate used; <c>null</c> without the night surcharge.</param>
/// <param name="MinimumKind">The kind of minimum rule of the rate used.</param>
/// <param name="MinimumAmount">The amount of the minimum rule; <c>null</c> when none.</param>
/// <param name="MinimumThreshold">The threshold of a tiered overage; <c>null</c> otherwise.</param>
/// <param name="MinimumOveragePrice">The overage price of a tiered overage; <c>null</c> otherwise.</param>
/// <param name="NightBandStart">First minute of the night band the trip was classified with.</param>
/// <param name="NightBandEnd">First minute after that night band.</param>
/// <param name="NightSurcharge">Whether the contract has the night surcharge.</param>
/// <param name="IsNight">Whether the trip starts inside the night band.</param>
/// <param name="Quantity">Minutes, kilograms or kilometres; <c>null</c> for a trip priced by trip.</param>
/// <param name="UnitPrice">The price used: day or night.</param>
/// <param name="BaseAmount">The amount before the minimum rule.</param>
/// <param name="AmountAfterMinimum">The amount after the minimum rule, before the night call fee.</param>
/// <param name="MinimumApplied">Whether the minimum rule changed the base amount.</param>
/// <param name="AppliedNightCallFee">The night call fee added; <c>null</c> when none is added.</param>
/// <param name="Total">The amount of the trip.</param>
public sealed record TripPricing(
    TripRateSource RateSource,
    Guid? RateVehicleId,
    Guid? RatePickupLocationId,
    Guid? RateDeliveryLocationId,
    decimal DayPrice,
    decimal? NightPrice,
    decimal? NightCallFee,
    TripMinimumKind MinimumKind,
    decimal? MinimumAmount,
    decimal? MinimumThreshold,
    decimal? MinimumOveragePrice,
    TimeOnly NightBandStart,
    TimeOnly NightBandEnd,
    bool NightSurcharge,
    bool IsNight,
    int? Quantity,
    decimal UnitPrice,
    decimal BaseAmount,
    decimal AmountAfterMinimum,
    bool MinimumApplied,
    decimal? AppliedNightCallFee,
    decimal Total);
