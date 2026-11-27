namespace Priamus.Trips.Operations.Features.GetTrip;

/// <summary>
/// One trip of the driver and what its page needs besides it.
/// </summary>
/// <param name="Trip">The trip.</param>
/// <param name="DaysToExpiry">For a planned trip, 7 on the day it was added and 0 on its last day;
/// <c>null</c> otherwise.</param>
/// <param name="ProposedStart">For a planned trip, the end time of the driver's latest trip ended today,
/// proposed as the start time; <c>null</c> when none ended today and for any other trip.</param>
/// <param name="EndAsks">For a trip in progress, what ending it asks of the driver, by the contract of the
/// trip day; <c>null</c> for any other trip.</param>
public sealed record GetTripResponse(
    TripDto Trip,
    int? DaysToExpiry,
    TimeOnly? ProposedStart,
    TripEndAsks? EndAsks);
