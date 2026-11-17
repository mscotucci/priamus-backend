using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Registry.Features.DecommissionVehicle;

/// <summary>
/// HTTP endpoint for <c>POST /api/registry/vehicles/{id:guid}/decommission</c>.
/// </summary>
public static class DecommissionVehicleEndpoint
{
    /// <summary>
    /// Maps the DecommissionVehicle endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapDecommissionVehicle(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/registry/vehicles/{id:guid}/decommission", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.BackOfficeOrAdmin)
            .WithName("DecommissionVehicle")
            .WithTags("Registry.Vehicles")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        [FromBody] DecommissionVehicleRequest request,
        [FromServices] DecommissionVehicleHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, request, cancellationToken);

        return result.Match(
            onSuccess: () => Results.NoContent(),
            onFailure: error => error.ToProblemDetails());
    }
}
