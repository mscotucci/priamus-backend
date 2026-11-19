using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Registry.Features.ChangeContractFromDate;
using Priamus.Trips.Registry.Features.CreateContract;
using Priamus.Trips.Registry.Features.CreateCustomer;
using Priamus.Trips.Registry.Features.CreateVehicle;
using Priamus.Trips.Registry.Features.DeactivateCustomer;
using Priamus.Trips.Registry.Features.DecommissionVehicle;
using Priamus.Trips.Registry.Features.GetContract;
using Priamus.Trips.Registry.Features.GetCustomer;
using Priamus.Trips.Registry.Features.ListContracts;
using Priamus.Trips.Registry.Features.ListCustomers;
using Priamus.Trips.Registry.Features.ListVehicles;
using Priamus.Trips.Registry.Features.ReactivateCustomer;
using Priamus.Trips.Registry.Features.ReactivateVehicle;
using Priamus.Trips.Registry.Features.UpdateContract;
using Priamus.Trips.Registry.Features.UpdateCustomer;
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
        app.MapDecommissionVehicle();
        app.MapReactivateVehicle();
        app.MapCreateCustomer();
        app.MapUpdateCustomer();
        app.MapListCustomers();
        app.MapDeactivateCustomer();
        app.MapReactivateCustomer();
        app.MapGetCustomer();
        app.MapListContracts();
        app.MapGetContract();
        app.MapCreateContract();
        app.MapUpdateContract();
        app.MapChangeContractFromDate();
        return app;
    }
}
