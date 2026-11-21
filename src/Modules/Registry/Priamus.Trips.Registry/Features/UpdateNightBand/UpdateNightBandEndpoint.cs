using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Registry.Features.UpdateNightBand;

/// <summary>
/// HTTP endpoint for <c>PUT /api/registry/night-band</c>.
/// </summary>
public static class UpdateNightBandEndpoint
{
    /// <summary>
    /// Maps the UpdateNightBand endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapUpdateNightBand(this IEndpointRouteBuilder app)
    {
        app.MapPut("/api/registry/night-band", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.Admin)
            .WithName("UpdateNightBand")
            .WithTags("Registry.NightBand")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [FromBody] UpdateNightBandRequest request,
        [FromServices] UpdateNightBandHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.Match(
            onSuccess: () => Results.NoContent(),
            onFailure: error => error.ToProblemDetails());
    }
}
