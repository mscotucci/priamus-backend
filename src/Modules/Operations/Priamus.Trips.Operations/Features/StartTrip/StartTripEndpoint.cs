using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Operations.Features.StartTrip;

/// <summary>
/// HTTP endpoint for <c>POST /api/operations/driver/trips/{tripId}/start</c>.
/// </summary>
public static class StartTripEndpoint
{
    /// <summary>
    /// Maps the StartTrip endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapStartTrip(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/operations/driver/trips/{tripId:guid}/start", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.Driver)
            .WithName("StartTrip")
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
        [FromBody] StartTripRequest request,
        [FromServices] StartTripHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(tripId, request, cancellationToken);

        return result.Match(
            onSuccess: trip => Results.Ok(trip),
            onFailure: error => error.ToProblemDetails());
    }
}
