using Priamus.Trips.Operations.Application.Services;
using Priamus.Trips.Operations.Domain.Trips;
using Priamus.Trips.Operations.Features.ListTripCustomers;
using Priamus.Trips.Operations.Features.ListTripLocations;
using Priamus.Trips.Operations.Features.ListTripVehicles;
using Priamus.Trips.Registry.Contracts;

namespace Priamus.Trips.Operations.Features.ListBackOfficeTrips;

/// <summary>What a back-office trip refers to, named: the parts the list item and the detail both carry.</summary>
/// <param name="Customer">The customer.</param>
/// <param name="Pickup">The pickup location; <c>null</c> when none was named.</param>
/// <param name="Delivery">The delivery location; <c>null</c> when none was named.</param>
/// <param name="Driver">The driver.</param>
/// <param name="Vehicle">The tractor or standalone vehicle.</param>
/// <param name="Trailer">The trailer; <c>null</c> when none.</param>
internal sealed record BackOfficeTripParts(
    TripCustomerDto Customer,
    TripLocationDto? Pickup,
    TripLocationDto? Delivery,
    BackOfficeDriverDto Driver,
    TripVehicleDto Vehicle,
    TripVehicleDto? Trailer)
{
    /// <summary>Names the parts of <paramref name="trip"/> with what the registry gave.</summary>
    /// <param name="trip">The trip.</param>
    /// <param name="names">The names of its vehicles, customer and locations.</param>
    /// <param name="drivers">The names of the drivers, its own among them.</param>
    public static BackOfficeTripParts Of(Trip trip, TripNames names, IReadOnlyList<TripDriverName> drivers)
    {
        var driver = drivers.Single(d => d.Id == trip.DriverId);

        return new BackOfficeTripParts(
            names.CustomerOf(trip.CustomerId),
            trip.PickupLocationId is { } pickupId ? names.LocationOf(pickupId) : null,
            trip.DeliveryLocationId is { } deliveryId ? names.LocationOf(deliveryId) : null,
            new BackOfficeDriverDto(driver.Id, driver.FirstName, driver.LastName),
            names.VehicleOf(trip.LeadingVehicleId),
            trip.TrailerId is { } trailerId ? names.VehicleOf(trailerId) : null);
    }
}
