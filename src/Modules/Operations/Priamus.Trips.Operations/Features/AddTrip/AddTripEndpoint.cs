using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Operations.Features.AddTrip;

/// <summary>
/// HTTP endpoint for <c>PUT /api/operations/driver/trips/{tripId}</c>.
/// </summary>
public static class AddTripEndpoint
{
    /// <summary>
    /// Maps the AddTrip endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapAddTrip(this IEndpointRouteBuilder app)
    {
        app.MapPut("/api/operations/driver/trips/{tripId:guid}", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.Driver)
            .WithName("AddTrip")
            .WithTags("Operations.Driver")
            .Produces<TripDto>(StatusCodes.Status201Created)
            .Produces<TripDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [FromRoute] Guid tripId,
        [FromBody] AddTripRequest request,
        [FromServices] AddTripHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(tripId, request, cancellationToken);

        return result.Match(
            onSuccess: outcome => outcome.Created
                ? Results.Created($"/api/operations/driver/trips/{outcome.Trip.Id}", outcome.Trip)
                : Results.Ok(outcome.Trip),
            onFailure: error => error.ToProblemDetails());
    }
}
