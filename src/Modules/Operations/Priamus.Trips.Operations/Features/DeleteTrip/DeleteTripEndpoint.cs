using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Operations.Features.DeleteTrip;

/// <summary>
/// HTTP endpoint for <c>DELETE /api/operations/driver/trips/{tripId}</c>.
/// </summary>
public static class DeleteTripEndpoint
{
    /// <summary>
    /// Maps the DeleteTrip endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapDeleteTrip(this IEndpointRouteBuilder app)
    {
        app.MapDelete("/api/operations/driver/trips/{tripId:guid}", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.Driver)
            .WithName("DeleteTrip")
            .WithTags("Operations.Driver")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [FromRoute] Guid tripId,
        [FromServices] DeleteTripHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(tripId, cancellationToken);

        return result.Match(
            onSuccess: () => Results.NoContent(),
            onFailure: error => error.ToProblemDetails());
    }
}
