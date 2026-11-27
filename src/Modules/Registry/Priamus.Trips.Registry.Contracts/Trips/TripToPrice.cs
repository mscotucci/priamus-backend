namespace Priamus.Trips.Registry.Contracts;

/// <summary>A trip that has ended, to be priced. Only the values the contract asks for are used.</summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="Day">The day of the trip, which decides the contract in force.</param>
/// <param name="LeadingVehicleId">The leading vehicle.</param>
/// <param name="TrailerId">The trailer; <c>null</c> when none.</param>
/// <param name="PickupLocationId">The pickup location; <c>null</c> when none.</param>
/// <param name="DeliveryLocationId">The delivery location; <c>null</c> when none.</param>
/// <param name="Start">The start time.</param>
/// <param name="End">The end time, used by a trip priced by hour.</param>
/// <param name="Kilograms">The kilograms carried, used by a trip priced by weight.</param>
/// <param name="Kilometres">The kilometres driven, used by a trip priced by distance.</param>
public sealed record TripToPrice(
    Guid CustomerId,
    DateOnly Day,
    Guid LeadingVehicleId,
    Guid? TrailerId,
    Guid? PickupLocationId,
    Guid? DeliveryLocationId,
    TimeOnly Start,
    TimeOnly End,
    int? Kilograms,
    int? Kilometres);
