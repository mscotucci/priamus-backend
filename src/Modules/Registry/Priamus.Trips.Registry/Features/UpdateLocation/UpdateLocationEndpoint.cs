using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Registry.Features.UpdateLocation;

/// <summary>
/// HTTP endpoint for <c>PUT /api/registry/locations/{id:guid}</c>.
/// </summary>
public static class UpdateLocationEndpoint
{
    /// <summary>
    /// Maps the UpdateLocation endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapUpdateLocation(this IEndpointRouteBuilder app)
    {
        app.MapPut("/api/registry/locations/{id:guid}", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.BackOfficeOrAdmin)
            .WithName("UpdateLocation")
            .WithTags("Registry.Locations")
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
        [FromBody] UpdateLocationRequest request,
        [FromServices] UpdateLocationHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, request, cancellationToken);

        return result.Match(
            onSuccess: () => Results.NoContent(),
            onFailure: error => error.ToProblemDetails());
    }
}
