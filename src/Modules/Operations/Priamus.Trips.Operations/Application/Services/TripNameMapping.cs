using Priamus.Trips.Operations.Features.ListTripCustomers;
using Priamus.Trips.Operations.Features.ListTripLocations;
using Priamus.Trips.Operations.Features.ListTripVehicles;
using Priamus.Trips.Registry.Contracts;

namespace Priamus.Trips.Operations.Application.Services;

/// <summary>
/// Turns the names the registry gives for the identifiers of a trip into the response records of the
/// choice endpoints. The registry answers whatever the status of the record, so a missing identifier is a
/// wiring bug, not a refusal.
/// </summary>
internal static class TripNameMapping
{
    /// <summary>The vehicle <paramref name="id"/> of <paramref name="names"/> as a response record.</summary>
    public static TripVehicleDto VehicleOf(this TripNames names, Guid id)
    {
        var vehicle = names.Vehicles.Single(v => v.Id == id);
        return new TripVehicleDto(vehicle.Id, vehicle.LicensePlate, vehicle.Description, vehicle.Kind, vehicle.RequiresTrailer);
    }

    /// <summary>The location <paramref name="id"/> of <paramref name="names"/> as a response record.</summary>
    public static TripLocationDto LocationOf(this TripNames names, Guid id)
    {
        var location = names.Locations.Single(l => l.Id == id);
        return new TripLocationDto(location.Id, location.Name, location.Municipality);
    }

    /// <summary>The customer <paramref name="id"/> of <paramref name="names"/> as a response record.</summary>
    public static TripCustomerDto CustomerOf(this TripNames names, Guid id)
    {
        var customer = names.Customers.Single(c => c.Id == id);
        return new TripCustomerDto(customer.Id, customer.CompanyName, customer.VatNumber);
    }
}
