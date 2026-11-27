using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Operations.Features.ListTripLocations;

/// <summary>
/// HTTP endpoint for <c>GET /api/operations/driver/locations</c>.
/// </summary>
public static class ListTripLocationsEndpoint
{
    /// <summary>
    /// Maps the ListTripLocations endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapListTripLocations(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/operations/driver/locations", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.Driver)
            .WithName("ListTripLocations")
            .WithTags("Operations.Driver")
            .Produces<IReadOnlyList<TripLocationDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [AsParameters] ListTripLocationsRequest request,
        [FromServices] ListTripLocationsHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.Match(
            onSuccess: response => Results.Ok(response),
            onFailure: error => error.ToProblemDetails());
    }
}
