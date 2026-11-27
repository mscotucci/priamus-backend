using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Operations.Features.ListBackOfficeTrips;

/// <summary>
/// HTTP endpoint for <c>GET /api/operations/backoffice/trips</c>.
/// </summary>
public static class ListBackOfficeTripsEndpoint
{
    /// <summary>
    /// Maps the ListBackOfficeTrips endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapListBackOfficeTrips(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/operations/backoffice/trips", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.BackOfficeOrAdmin)
            .WithName("ListBackOfficeTrips")
            .WithTags("Operations.BackOffice")
            .Produces<BackOfficeTripListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [AsParameters] ListBackOfficeTripsRequest request,
        [FromServices] ListBackOfficeTripsHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.Match(
            onSuccess: response => Results.Ok(response),
            onFailure: error => error.ToProblemDetails());
    }
}
