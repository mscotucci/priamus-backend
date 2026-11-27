using Priamus.Trips.Operations.Domain.Trips;
using Priamus.Trips.Operations.Features.ListTripCustomers;
using Priamus.Trips.Operations.Features.ListTripLocations;
using Priamus.Trips.Operations.Features.ListTripVehicles;
using Priamus.Trips.SharedKernel.Paging;

namespace Priamus.Trips.Operations.Features.ListBackOfficeTrips;

/// <summary>One page of trips of a view and, for the ended view, the figures of the whole selection.</summary>
/// <param name="Trips">The page.</param>
/// <param name="Figures">Trips, customers and amount of the whole selection; <c>null</c> outside the ended view.</param>
public sealed record BackOfficeTripListResponse(
    PagedResponse<BackOfficeTripListItemDto> Trips,
    BackOfficeTripFigures? Figures);

/// <summary>What the ended view adds up, over every trip of the selection and not only the page.</summary>
/// <param name="Trips">How many trips the selection has.</param>
/// <param name="Customers">How many distinct customers they are done for.</param>
/// <param name="Amount">The sum of their totals.</param>
public sealed record BackOfficeTripFigures(int Trips, int Customers, decimal Amount);

/// <summary>A driver as the back office names them.</summary>
/// <param name="Id">The driver's identifier.</param>
/// <param name="FirstName">The first name.</param>
/// <param name="LastName">The last name.</param>
public sealed record BackOfficeDriverDto(Guid Id, string FirstName, string LastName);

/// <summary>A trip of the back-office list, in any state; the measure and amount fields exist for an ended trip only.</summary>
/// <param name="Id">The trip's identifier.</param>
/// <param name="State">Planned, in progress or ended.</param>
/// <param name="Number">The trip number; <c>null</c> until it is ended.</param>
/// <param name="Day">The day the trip started; <c>null</c> while it is planned.</param>
/// <param name="Start">The start time; <c>null</c> while it is planned.</param>
/// <param name="End">The end time; <c>null</c> until it is ended.</param>
/// <param name="AddedAt">When the trip was added (UTC).</param>
/// <param name="DaysToExpiry">Days left before a planned trip expires; <c>null</c> for any other state.</param>
/// <param name="Customer">The customer.</param>
/// <param name="Pickup">The pickup location; <c>null</c> when none was named.</param>
/// <param name="Delivery">The delivery location; <c>null</c> when none was named.</param>
/// <param name="Driver">The driver.</param>
/// <param name="Vehicle">The tractor or standalone vehicle.</param>
/// <param name="Trailer">The trailer; <c>null</c> when none.</param>
/// <param name="MeasureKind">How the trip was measured; <c>null</c> until it is ended.</param>
/// <param name="Quantity">Minutes, kilograms or kilometres; <c>null</c> for a trip priced by trip and until it is ended.</param>
/// <param name="UnitPrice">The unit price the trip was priced with; <c>null</c> until it is ended.</param>
/// <param name="Total">The amount of the trip; <c>null</c> until it is ended.</param>
/// <param name="NightPriceApplied">Whether the night price was applied; <c>null</c> until the trip is ended.</param>
public sealed record BackOfficeTripListItemDto(
    Guid Id,
    TripState State,
    long? Number,
    DateOnly? Day,
    TimeOnly? Start,
    TimeOnly? End,
    DateTime AddedAt,
    int? DaysToExpiry,
    TripCustomerDto Customer,
    TripLocationDto? Pickup,
    TripLocationDto? Delivery,
    BackOfficeDriverDto Driver,
    TripVehicleDto Vehicle,
    TripVehicleDto? Trailer,
    TripMeasureKind? MeasureKind,
    int? Quantity,
    decimal? UnitPrice,
    decimal? Total,
    bool? NightPriceApplied);
