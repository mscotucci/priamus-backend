using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Identity.Features.Refresh;

/// <summary>
/// HTTP endpoint for <c>POST /api/auth/refresh</c>.
/// </summary>
public static class RefreshEndpoint
{
    /// <summary>
    /// Maps the Refresh endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapRefresh(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/refresh", HandleAsync)
            .AllowAnonymous()
            .WithName("Refresh")
            .WithTags("Auth")
            .Produces<RefreshResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        RefreshRequest request,
        RefreshHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.Match(
            onSuccess: response => Results.Ok(response),
            onFailure: error => error.ToProblemDetails());
    }
}
