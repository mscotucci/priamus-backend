using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Operations.Features.ListMyTrips;

/// <summary>
/// HTTP endpoint for <c>GET /api/operations/driver/my-trips</c>.
/// </summary>
public static class ListMyTripsEndpoint
{
    /// <summary>
    /// Maps the ListMyTrips endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapListMyTrips(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/operations/driver/my-trips", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.Driver)
            .WithName("ListMyTrips")
            .WithTags("Operations.Driver")
            .Produces<MyTripsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [FromServices] ListMyTripsHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(cancellationToken);

        return result.Match(
            onSuccess: response => Results.Ok(response),
            onFailure: error => error.ToProblemDetails());
    }
}
