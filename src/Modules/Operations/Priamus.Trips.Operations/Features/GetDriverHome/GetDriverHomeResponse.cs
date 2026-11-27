using Priamus.Trips.Operations.Features.ListTripCustomers;
using Priamus.Trips.Operations.Features.ListTripLocations;
using Priamus.Trips.Operations.Features.ListTripVehicles;

namespace Priamus.Trips.Operations.Features.GetDriverHome;

/// <summary>
/// What the driver home shows: the driver's name, the trip in progress, the planned trips, the trips the
/// driver can repeat and how many trips the driver ended today and in the last seven days.
/// </summary>
/// <param name="FirstName">The driver's first name.</param>
/// <param name="LastName">The driver's last name.</param>
/// <param name="InProgress">The trip in progress; <c>null</c> when there is none.</param>
/// <param name="Planned">The planned trips that have not expired, in the order they were added.</param>
/// <param name="RepeatableTrips">Every repeatable trip of the driver, built from ended trips, the one ended
/// last first; the page shows the first four and offers the rest.</param>
/// <param name="EndedToday">How many of the driver's ended trips have today as their day.</param>
/// <param name="EndedInLastSevenDays">How many of the driver's ended trips have a day of today or one of the 6
/// days before.</param>
public sealed record GetDriverHomeResponse(
    string FirstName,
    string LastName,
    InProgressTripDto? InProgress,
    IReadOnlyList<PlannedTripDto> Planned,
    IReadOnlyList<RepeatableTripDto> RepeatableTrips,
    int EndedToday,
    int EndedInLastSevenDays);

/// <summary>A trip in progress and what its end asks.</summary>
/// <param name="Trip">The trip.</param>
/// <param name="EndAsks">What ending it asks of the driver, by the contract of the trip day.</param>
public sealed record InProgressTripDto(TripDto Trip, TripEndAsks EndAsks);

/// <summary>A planned trip and the days left before it expires.</summary>
/// <param name="Trip">The trip.</param>
/// <param name="DaysToExpiry">7 on the day it was added, 0 on its last day.</param>
public sealed record PlannedTripDto(TripDto Trip, int DaysToExpiry);

/// <summary>
/// What a past trip offers to do again: customer, locations, leading vehicle and trailer, named as the
/// registry names them now (a decommissioned vehicle keeps its name).
/// </summary>
/// <param name="Customer">The customer.</param>
/// <param name="Pickup">The pickup location; <c>null</c> when the trip named none.</param>
/// <param name="Delivery">The delivery location; <c>null</c> when the trip named none.</param>
/// <param name="Vehicle">The tractor or standalone vehicle.</param>
/// <param name="Trailer">The trailer; <c>null</c> when the trip had none.</param>
public sealed record RepeatableTripDto(
    TripCustomerDto Customer,
    TripLocationDto? Pickup,
    TripLocationDto? Delivery,
    TripVehicleDto Vehicle,
    TripVehicleDto? Trailer);
