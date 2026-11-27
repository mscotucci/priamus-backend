using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Operations.Features.GetNewTrip;

/// <summary>
/// HTTP endpoint for <c>GET /api/operations/driver/new-trip</c>.
/// </summary>
public static class GetNewTripEndpoint
{
    /// <summary>
    /// Maps the GetNewTrip endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapGetNewTrip(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/operations/driver/new-trip", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.Driver)
            .WithName("GetNewTrip")
            .WithTags("Operations.Driver")
            .Produces<GetNewTripResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [FromServices] GetNewTripHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(cancellationToken);

        return result.Match(
            onSuccess: response => Results.Ok(response),
            onFailure: error => error.ToProblemDetails());
    }
}
