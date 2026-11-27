using Priamus.Trips.Operations.Features.ListTripLocations;

namespace Priamus.Trips.Operations.Features.GetTripQuestions;

/// <summary>
/// A location, or a pair, a contract has a rate for. Only the sides the contract asks for are set.
/// </summary>
/// <param name="Pickup">The pickup location; <c>null</c> when the contract does not ask for it.</param>
/// <param name="Delivery">The delivery location; <c>null</c> when the contract does not ask for it.</param>
public sealed record TripPlaceDto(TripLocationDto? Pickup, TripLocationDto? Delivery);
