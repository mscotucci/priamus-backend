using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Identity.Features.Revoke;

/// <summary>
/// HTTP endpoint for <c>POST /api/identity/auth/revoke</c> (self-service logout).
/// </summary>
public static class RevokeEndpoint
{
    /// <summary>
    /// Maps the self-service Revoke endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapRevoke(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/identity/auth/revoke", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.Authenticated)
            .WithName("Revoke")
            .WithTags("Identity.Auth")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [FromBody] RevokeRequest request,
        [FromServices] RevokeHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.Match(
            onSuccess: () => Results.NoContent(),
            onFailure: error => error.ToProblemDetails());
    }
}
