using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Identity.Features.CreateUser;

/// <summary>
/// HTTP endpoint for <c>POST /api/users</c>.
/// </summary>
public static class CreateUserEndpoint
{
    /// <summary>
    /// Maps the CreateUser endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapCreateUser(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/users", HandleAsync)
            .RequireAuthorization("AdminOnly")
            .WithName("CreateUser")
            .WithTags("Users")
            .Produces<CreateUserResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        CreateUserRequest request,
        CreateUserHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.Match(
            onSuccess: response => Results.Created($"/api/users/{response.Id}", response),
            onFailure: error => error.ToProblemDetails());
    }
}
