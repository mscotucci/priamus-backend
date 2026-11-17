using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Identity.Features.EnableUser;

/// <summary>
/// HTTP endpoint for <c>POST /api/identity/users/{id:guid}/enable</c>.
/// </summary>
public static class EnableUserEndpoint
{
    /// <summary>
    /// Maps the EnableUser endpoint.
    /// </summary>
    /// <remarks>
    /// There is no <c>CannotEnableSelf</c> error: a disabled admin cannot authenticate
    /// (login rejects with <see cref="UserErrors.AccountDisabled"/>) and therefore cannot
    /// reach this endpoint. The preclusion is enforced upstream by design.
    /// </remarks>
    public static IEndpointRouteBuilder MapEnableUser(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/identity/users/{id:guid}/enable", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.Admin)
            .WithName("EnableUser")
            .WithTags("Identity.Users")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        [FromBody] EnableUserRequest request,
        [FromServices] EnableUserHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, request, cancellationToken);

        return result.Match(
            onSuccess: () => Results.NoContent(),
            onFailure: error => error.ToProblemDetails());
    }
}
