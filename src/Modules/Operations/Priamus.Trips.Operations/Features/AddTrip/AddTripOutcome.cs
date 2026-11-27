namespace Priamus.Trips.Operations.Features.AddTrip;

/// <summary>What adding a trip answers: the trip, and whether this act saved it.</summary>
/// <param name="Trip">The trip as it is.</param>
/// <param name="Created"><c>true</c> when this act saved the trip, <c>false</c> when it already existed.</param>
internal sealed record AddTripOutcome(TripDto Trip, bool Created);
