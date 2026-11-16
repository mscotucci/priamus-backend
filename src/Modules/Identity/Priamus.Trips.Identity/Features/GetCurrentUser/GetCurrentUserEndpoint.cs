using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Identity.Features.GetCurrentUser;

/// <summary>
/// HTTP endpoint for <c>GET /api/identity/me</c>.
/// </summary>
public static class GetCurrentUserEndpoint
{
    /// <summary>
    /// Maps the GetCurrentUser endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapGetCurrentUser(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/identity/me", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.Authenticated)
            .WithName("GetCurrentUser")
            .WithTags("Identity.Auth")
            .Produces<GetCurrentUserResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [FromServices] GetCurrentUserHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(cancellationToken);

        return result.Match(
            onSuccess: response => Results.Ok(response),
            onFailure: error => error.ToProblemDetails());
    }
}
