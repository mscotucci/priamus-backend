using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Operations.Features.EndTrip;

/// <summary>
/// HTTP endpoint for <c>POST /api/operations/driver/trips/{tripId}/end</c>.
/// </summary>
public static class EndTripEndpoint
{
    /// <summary>
    /// Maps the EndTrip endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapEndTrip(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/operations/driver/trips/{tripId:guid}/end", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.Driver)
            .WithName("EndTrip")
            .WithTags("Operations.Driver")
            .Produces<TripDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [FromRoute] Guid tripId,
        [FromBody] EndTripRequest request,
        [FromServices] EndTripHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(tripId, request, cancellationToken);

        return result.Match(
            onSuccess: trip => Results.Ok(trip),
            onFailure: error => error.ToProblemDetails());
    }
}
