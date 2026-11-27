using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Operations.Features.ListTripVehicles;

/// <summary>
/// HTTP endpoint for <c>GET /api/operations/driver/vehicles</c>.
/// </summary>
public static class ListTripVehiclesEndpoint
{
    /// <summary>
    /// Maps the ListTripVehicles endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapListTripVehicles(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/operations/driver/vehicles", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.Driver)
            .WithName("ListTripVehicles")
            .WithTags("Operations.Driver")
            .Produces<IReadOnlyList<TripVehicleDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [AsParameters] ListTripVehiclesRequest request,
        [FromServices] ListTripVehiclesHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.Match(
            onSuccess: response => Results.Ok(response),
            onFailure: error => error.ToProblemDetails());
    }
}
