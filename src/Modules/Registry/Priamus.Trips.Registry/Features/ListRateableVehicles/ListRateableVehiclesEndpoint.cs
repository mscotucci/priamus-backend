using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Registry.Features.ListRateableVehicles;

/// <summary>
/// HTTP endpoint for <c>GET /api/registry/vehicles/rateable</c>.
/// </summary>
public static class ListRateableVehiclesEndpoint
{
    /// <summary>
    /// Maps the ListRateableVehicles endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapListRateableVehicles(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/registry/vehicles/rateable", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.BackOfficeOrAdmin)
            .WithName("ListRateableVehicles")
            .WithTags("Registry.Vehicles")
            .Produces<IReadOnlyList<RateableVehicleDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [FromServices] ListRateableVehiclesHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(cancellationToken);

        return result.Match(
            onSuccess: response => Results.Ok(response),
            onFailure: error => error.ToProblemDetails());
    }
}
