using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Registry.Features.ListRateableLocations;

/// <summary>
/// HTTP endpoint for <c>GET /api/registry/locations/rateable</c>.
/// </summary>
public static class ListRateableLocationsEndpoint
{
    /// <summary>
    /// Maps the ListRateableLocations endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapListRateableLocations(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/registry/locations/rateable", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.BackOfficeOrAdmin)
            .WithName("ListRateableLocations")
            .WithTags("Registry.Locations")
            .Produces<IReadOnlyList<RateableLocationDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [FromServices] ListRateableLocationsHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(cancellationToken);

        return result.Match(
            onSuccess: response => Results.Ok(response),
            onFailure: error => error.ToProblemDetails());
    }
}
