using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Identity.Features.Revoke;

/// <summary>
/// HTTP endpoint for <c>POST /api/auth/revoke</c> (self-service logout).
/// </summary>
public static class RevokeEndpoint
{
    /// <summary>
    /// Maps the self-service Revoke endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapRevoke(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/revoke", HandleAsync)
            .RequireAuthorization()
            .WithName("Revoke")
            .WithTags("Auth")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        RevokeRequest request,
        RevokeHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.Match(
            onSuccess: () => Results.NoContent(),
            onFailure: error => error.ToProblemDetails());
    }
}
