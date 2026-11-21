using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Registry.Features.GetNightBand;

/// <summary>
/// HTTP endpoint for <c>GET /api/registry/night-band</c>.
/// </summary>
public static class GetNightBandEndpoint
{
    /// <summary>
    /// Maps the GetNightBand endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapGetNightBand(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/registry/night-band", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.Admin)
            .WithName("GetNightBand")
            .WithTags("Registry.NightBand")
            .Produces<NightBandDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [FromServices] GetNightBandHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(cancellationToken);

        return result.Match(
            onSuccess: response => Results.Ok(response),
            onFailure: error => error.ToProblemDetails());
    }
}
