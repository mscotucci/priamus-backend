using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Operations.Features.AddTrip;
using Priamus.Trips.Operations.Features.CheckTripAnswers;
using Priamus.Trips.Operations.Features.DeleteTrip;
using Priamus.Trips.Operations.Features.EndTrip;
using Priamus.Trips.Operations.Features.GetDriverHome;
using Priamus.Trips.Operations.Features.GetNewTrip;
using Priamus.Trips.Operations.Features.GetTrip;
using Priamus.Trips.Operations.Features.GetTripQuestions;
using Priamus.Trips.Operations.Features.ListTripCustomers;
using Priamus.Trips.Operations.Features.ListTripLocations;
using Priamus.Trips.Operations.Features.ListTripVehicles;
using Priamus.Trips.Operations.Features.StartTrip;

namespace Priamus.Trips.Operations.DependencyInjection;

/// <summary>
/// Enumerates all Operations module HTTP endpoints.
/// </summary>
public static class OperationsModuleEndpoints
{
    /// <summary>
    /// Maps all Operations module endpoints onto the given route builder.
    /// </summary>
    public static IEndpointRouteBuilder MapOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapListTripLocations();
        app.MapListTripVehicles();
        app.MapListTripCustomers();
        app.MapGetTripQuestions();
        app.MapGetDriverHome();
        app.MapGetNewTrip();
        app.MapGetTrip();
        app.MapCheckTripAnswers();
        app.MapAddTrip();
        app.MapStartTrip();
        app.MapEndTrip();
        app.MapDeleteTrip();
        return app;
    }
}
