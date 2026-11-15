using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Identity.Features.CreateUser;

/// <summary>
/// HTTP endpoint for <c>POST /api/identity/users</c>.
/// </summary>
public static class CreateUserEndpoint
{
    /// <summary>
    /// Maps the CreateUser endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapCreateUser(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/identity/users", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.Admin)
            .WithName("CreateUser")
            .WithTags("Identity.Users")
            .Produces<CreateUserResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [FromBody] CreateUserRequest request,
        [FromServices] CreateUserHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.Match(
            onSuccess: response => Results.Created($"/api/identity/users/{response.Id}", response),
            onFailure: error => error.ToProblemDetails());
    }
}
