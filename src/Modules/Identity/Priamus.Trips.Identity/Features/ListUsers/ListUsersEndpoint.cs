using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.SharedKernel.Http;
using Priamus.Trips.SharedKernel.Paging;

namespace Priamus.Trips.Identity.Features.ListUsers;

/// <summary>
/// HTTP endpoint for <c>GET /api/users</c>.
/// </summary>
public static class ListUsersEndpoint
{
    /// <summary>
    /// Maps the ListUsers endpoint onto the route builder.
    /// </summary>
    public static IEndpointRouteBuilder MapListUsers(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/users", HandleAsync)
            .RequireAuthorization("AdminOnly")
            .WithName("ListUsers")
            .WithTags("Users")
            .Produces<PagedResponse<UserListItemDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [AsParameters] ListUsersRequest request,
        ListUsersHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.Match(
            onSuccess: response => Results.Ok(response),
            onFailure: error => error.ToProblemDetails());
    }
}
