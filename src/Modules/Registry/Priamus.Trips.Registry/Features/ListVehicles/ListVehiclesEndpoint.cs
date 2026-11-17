using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;
using Priamus.Trips.SharedKernel.Paging;

namespace Priamus.Trips.Registry.Features.ListVehicles;

/// <summary>
/// HTTP endpoint for <c>GET /api/registry/vehicles</c>.
/// </summary>
public static class ListVehiclesEndpoint
{
    /// <summary>
    /// Maps the ListVehicles endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapListVehicles(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/registry/vehicles", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.BackOfficeOrAdmin)
            .WithName("ListVehicles")
            .WithTags("Registry.Vehicles")
            .Produces<PagedResponse<VehicleListItemDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [AsParameters] ListVehiclesRequest request,
        [FromServices] ListVehiclesHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.Match(
            onSuccess: response => Results.Ok(response),
            onFailure: error => error.ToProblemDetails());
    }
}
