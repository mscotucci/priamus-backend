using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Identity.Features.AdminRevoke;

/// <summary>
/// HTTP endpoint for <c>POST /api/identity/auth/admin/tokens/{tokenId}/revoke</c>.
/// </summary>
public static class AdminRevokeEndpoint
{
    /// <summary>
    /// Maps the admin token revocation endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapAdminRevoke(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/identity/auth/admin/tokens/{tokenId:guid}/revoke", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.BackOfficeOrAdmin)
            .WithName("AdminRevokeToken")
            .WithTags("Identity.Auth")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        Guid tokenId,
        [FromServices] AdminRevokeHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(tokenId, cancellationToken);

        return result.Match(
            onSuccess: () => Results.NoContent(),
            onFailure: error => error.ToProblemDetails());
    }
}
