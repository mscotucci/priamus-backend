using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Operations.Features.GetDriverHome;

/// <summary>
/// HTTP endpoint for <c>GET /api/operations/driver/home</c>.
/// </summary>
public static class GetDriverHomeEndpoint
{
    /// <summary>
    /// Maps the GetDriverHome endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapGetDriverHome(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/operations/driver/home", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.Driver)
            .WithName("GetDriverHome")
            .WithTags("Operations.Driver")
            .Produces<GetDriverHomeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [FromServices] GetDriverHomeHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(cancellationToken);

        return result.Match(
            onSuccess: response => Results.Ok(response),
            onFailure: error => error.ToProblemDetails());
    }
}
