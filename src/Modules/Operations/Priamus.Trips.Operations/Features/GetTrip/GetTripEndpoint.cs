using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Operations.Features.GetTrip;

/// <summary>
/// HTTP endpoint for <c>GET /api/operations/driver/trips/{tripId}</c>.
/// </summary>
public static class GetTripEndpoint
{
    /// <summary>
    /// Maps the GetTrip endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapGetTrip(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/operations/driver/trips/{tripId:guid}", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.Driver)
            .WithName("GetTrip")
            .WithTags("Operations.Driver")
            .Produces<GetTripResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [FromRoute] Guid tripId,
        [FromServices] GetTripHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(tripId, cancellationToken);

        return result.Match(
            onSuccess: response => Results.Ok(response),
            onFailure: error => error.ToProblemDetails());
    }
}
