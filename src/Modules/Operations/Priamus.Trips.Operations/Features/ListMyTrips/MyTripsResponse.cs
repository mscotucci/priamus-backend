namespace Priamus.Trips.Operations.Features.ListMyTrips;

/// <summary>The driver's ended trips of the last seven days, without any price.</summary>
/// <param name="Today">The day in Italy the seven days end on.</param>
/// <param name="Trips">The driver's ended trips whose day is today or one of the 6 days before, the newest
/// first (by day, then by start time).</param>
public sealed record MyTripsResponse(DateOnly Today, IReadOnlyList<TripDto> Trips);
