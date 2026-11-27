using Priamus.Trips.Operations.Domain.Trips;
using Priamus.Trips.Operations.Features.ListBackOfficeTrips;
using Priamus.Trips.Operations.Features.ListTripCustomers;
using Priamus.Trips.Operations.Features.ListTripLocations;
using Priamus.Trips.Operations.Features.ListTripVehicles;

namespace Priamus.Trips.Operations.Features.GetBackOfficeTrip;

/// <summary>One trip in any state, as the back office reads it: the list's fields, its timeline, what it requires and, once ended, the calculation it was saved with.</summary>
/// <param name="Id">The trip's identifier.</param>
/// <param name="State">Planned, in progress or ended.</param>
/// <param name="Number">The trip number; <c>null</c> until it is ended.</param>
/// <param name="Day">The day the trip started; <c>null</c> while it is planned.</param>
/// <param name="Start">The start time; <c>null</c> while it is planned.</param>
/// <param name="End">The end time; <c>null</c> until it is ended.</param>
/// <param name="EndedOn">The day the end time falls on: the trip day, or the day after when the trip crossed midnight; <c>null</c> until it is ended.</param>
/// <param name="AddedAt">When the trip was added (UTC).</param>
/// <param name="DaysToExpiry">Days left before a planned trip expires; <c>null</c> for any other state.</param>
/// <param name="ExpiresOn">The day a planned trip expires; <c>null</c> for any other state.</param>
/// <param name="Customer">The customer.</param>
/// <param name="Pickup">The pickup location; <c>null</c> when none was named.</param>
/// <param name="Delivery">The delivery location; <c>null</c> when none was named.</param>
/// <param name="Driver">The driver.</param>
/// <param name="Vehicle">The tractor or standalone vehicle.</param>
/// <param name="Trailer">The trailer; <c>null</c> when none.</param>
/// <param name="MeasureKind">How the trip was measured; <c>null</c> until it is ended.</param>
/// <param name="RequiredDocuments">The documents the trip required, as saved; empty when none.</param>
/// <param name="Calculation">The calculation the trip was saved with; <c>null</c> until it is ended.</param>
public sealed record GetBackOfficeTripResponse(
    Guid Id,
    TripState State,
    long? Number,
    DateOnly? Day,
    TimeOnly? Start,
    TimeOnly? End,
    DateOnly? EndedOn,
    DateTime AddedAt,
    int? DaysToExpiry,
    DateOnly? ExpiresOn,
    TripCustomerDto Customer,
    TripLocationDto? Pickup,
    TripLocationDto? Delivery,
    BackOfficeDriverDto Driver,
    TripVehicleDto Vehicle,
    TripVehicleDto? Trailer,
    TripMeasureKind? MeasureKind,
    IReadOnlyList<string> RequiredDocuments,
    BackOfficeTripCalculationDto? Calculation);

/// <summary>Every step of the calculation an ended trip was saved with; later changes of the contract never reach it.</summary>
/// <param name="RateSource">Where the rate came from.</param>
/// <param name="RateVehicle">The vehicle whose own rate was used; <c>null</c> when none.</param>
/// <param name="RatePickup">The pickup location of the rate used; <c>null</c> when the rate has none.</param>
/// <param name="RateDelivery">The delivery location of the rate used; <c>null</c> when the rate has none.</param>
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
public sealed record BackOfficeTripCalculationDto(
    TripRateSource RateSource,
    TripVehicleDto? RateVehicle,
    TripLocationDto? RatePickup,
    TripLocationDto? RateDelivery,
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
