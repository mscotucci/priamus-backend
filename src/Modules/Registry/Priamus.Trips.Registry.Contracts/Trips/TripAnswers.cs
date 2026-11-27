namespace Priamus.Trips.Registry.Contracts;

/// <summary>The answers of a trip in the making, to be judged against the Registry on <paramref name="Day"/>.</summary>
/// <param name="VehicleId">The leading vehicle; <c>null</c> when not answered yet.</param>
/// <param name="TrailerId">The trailer; <c>null</c> when none is answered.</param>
/// <param name="CustomerId">The customer; <c>null</c> when not answered yet.</param>
/// <param name="Day">The day of the trip, which decides the contract in force.</param>
/// <param name="PickupLocationId">The pickup location; <c>null</c> when none is answered.</param>
/// <param name="DeliveryLocationId">The delivery location; <c>null</c> when none is answered.</param>
public sealed record TripAnswers(
    Guid? VehicleId,
    Guid? TrailerId,
    Guid? CustomerId,
    DateOnly Day,
    Guid? PickupLocationId,
    Guid? DeliveryLocationId);
