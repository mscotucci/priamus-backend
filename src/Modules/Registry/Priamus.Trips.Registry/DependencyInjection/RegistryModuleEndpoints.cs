using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Registry.Features.ChangeContractFromDate;
using Priamus.Trips.Registry.Features.CreateContract;
using Priamus.Trips.Registry.Features.CreateCustomer;
using Priamus.Trips.Registry.Features.CreateDriver;
using Priamus.Trips.Registry.Features.CreateLocation;
using Priamus.Trips.Registry.Features.CreateVehicle;
using Priamus.Trips.Registry.Features.DeactivateCustomer;
using Priamus.Trips.Registry.Features.DeactivateDriver;
using Priamus.Trips.Registry.Features.DeactivateLocation;
using Priamus.Trips.Registry.Features.DecommissionVehicle;
using Priamus.Trips.Registry.Features.GetContract;
using Priamus.Trips.Registry.Features.GetCustomer;
using Priamus.Trips.Registry.Features.GetNightBand;
using Priamus.Trips.Registry.Features.ListContracts;
using Priamus.Trips.Registry.Features.ListCustomers;
using Priamus.Trips.Registry.Features.ListDrivers;
using Priamus.Trips.Registry.Features.ListLinkableAccounts;
using Priamus.Trips.Registry.Features.ListLocations;
using Priamus.Trips.Registry.Features.ListRateableLocations;
using Priamus.Trips.Registry.Features.ListRateableVehicles;
using Priamus.Trips.Registry.Features.ListVehicles;
using Priamus.Trips.Registry.Features.ReactivateCustomer;
using Priamus.Trips.Registry.Features.ReactivateDriver;
using Priamus.Trips.Registry.Features.ReactivateLocation;
using Priamus.Trips.Registry.Features.ReactivateVehicle;
using Priamus.Trips.Registry.Features.SimulatePrice;
using Priamus.Trips.Registry.Features.UpdateContract;
using Priamus.Trips.Registry.Features.UpdateCustomer;
using Priamus.Trips.Registry.Features.UpdateDriver;
using Priamus.Trips.Registry.Features.UpdateLocation;
using Priamus.Trips.Registry.Features.UpdateNightBand;
using Priamus.Trips.Registry.Features.UpdateVehicle;

namespace Priamus.Trips.Registry.DependencyInjection;

/// <summary>
/// Enumerates all Registry module HTTP endpoints.
/// </summary>
public static class RegistryModuleEndpoints
{
    /// <summary>
    /// Maps all Registry module endpoints onto the given route builder.
    /// </summary>
    public static IEndpointRouteBuilder MapRegistryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapCreateVehicle();
        app.MapUpdateVehicle();
        app.MapListVehicles();
        app.MapListRateableVehicles();
        app.MapDecommissionVehicle();
        app.MapReactivateVehicle();
        app.MapCreateCustomer();
        app.MapUpdateCustomer();
        app.MapListCustomers();
        app.MapDeactivateCustomer();
        app.MapReactivateCustomer();
        app.MapCreateDriver();
        app.MapUpdateDriver();
        app.MapListDrivers();
        app.MapListLinkableAccounts();
        app.MapDeactivateDriver();
        app.MapReactivateDriver();
        app.MapGetCustomer();
        app.MapListContracts();
        app.MapGetContract();
        app.MapCreateContract();
        app.MapUpdateContract();
        app.MapChangeContractFromDate();
        app.MapGetNightBand();
        app.MapUpdateNightBand();
        app.MapSimulatePrice();
        app.MapCreateLocation();
        app.MapUpdateLocation();
        app.MapListLocations();
        app.MapListRateableLocations();
        app.MapDeactivateLocation();
        app.MapReactivateLocation();
        return app;
    }
}
