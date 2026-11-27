using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Operations.Features.GetBackOfficeTrip;

/// <summary>
/// HTTP endpoint for <c>GET /api/operations/backoffice/trips/{tripId}</c>.
/// </summary>
public static class GetBackOfficeTripEndpoint
{
    /// <summary>
    /// Maps the GetBackOfficeTrip endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapGetBackOfficeTrip(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/operations/backoffice/trips/{tripId:guid}", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.BackOfficeOrAdmin)
            .WithName("GetBackOfficeTrip")
            .WithTags("Operations.BackOffice")
            .Produces<GetBackOfficeTripResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [FromRoute] Guid tripId,
        [FromServices] GetBackOfficeTripHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(tripId, cancellationToken);

        return result.Match(
            onSuccess: response => Results.Ok(response),
            onFailure: error => error.ToProblemDetails());
    }
}
