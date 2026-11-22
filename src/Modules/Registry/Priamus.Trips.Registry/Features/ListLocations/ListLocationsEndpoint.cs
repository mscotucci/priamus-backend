using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;
using Priamus.Trips.SharedKernel.Paging;

namespace Priamus.Trips.Registry.Features.ListLocations;

/// <summary>
/// HTTP endpoint for <c>GET /api/registry/locations</c>.
/// </summary>
public static class ListLocationsEndpoint
{
    /// <summary>
    /// Maps the ListLocations endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapListLocations(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/registry/locations", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.BackOfficeOrAdmin)
            .WithName("ListLocations")
            .WithTags("Registry.Locations")
            .Produces<PagedResponse<LocationListItemDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [AsParameters] ListLocationsRequest request,
        [FromServices] ListLocationsHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.Match(
            onSuccess: response => Results.Ok(response),
            onFailure: error => error.ToProblemDetails());
    }
}
