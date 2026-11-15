using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Identity.Features.DisableUser;

/// <summary>
/// HTTP endpoint for <c>POST /api/identity/users/{id:guid}/disable</c>.
/// </summary>
public static class DisableUserEndpoint
{
    /// <summary>
    /// Maps the DisableUser endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapDisableUser(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/identity/users/{id:guid}/disable", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.Admin)
            .WithName("DisableUser")
            .WithTags("Identity.Users")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        [FromServices] DisableUserHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken);

        return result.Match(
            onSuccess: () => Results.NoContent(),
            onFailure: error => error.ToProblemDetails());
    }
}
