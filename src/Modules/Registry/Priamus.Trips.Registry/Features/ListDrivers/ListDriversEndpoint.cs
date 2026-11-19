using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;
using Priamus.Trips.SharedKernel.Paging;

namespace Priamus.Trips.Registry.Features.ListDrivers;

/// <summary>
/// HTTP endpoint for <c>GET /api/registry/drivers</c>.
/// </summary>
public static class ListDriversEndpoint
{
    /// <summary>
    /// Maps the ListDrivers endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapListDrivers(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/registry/drivers", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.BackOfficeOrAdmin)
            .WithName("ListDrivers")
            .WithTags("Registry.Drivers")
            .Produces<PagedResponse<DriverListItemDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [AsParameters] ListDriversRequest request,
        [FromServices] ListDriversHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.Match(
            onSuccess: response => Results.Ok(response),
            onFailure: error => error.ToProblemDetails());
    }
}
