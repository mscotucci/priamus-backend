using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Identity.Features.Login;

/// <summary>
/// HTTP endpoint for <c>POST /api/identity/auth/login</c>.
/// </summary>
public static class LoginEndpoint
{
    /// <summary>
    /// Maps the Login endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapLogin(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/identity/auth/login", HandleAsync)
            .AllowAnonymous()
            .WithName("Login")
            .WithTags("Identity.Auth")
            .Produces<LoginResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [FromBody] LoginRequest request,
        [FromServices] LoginHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.Match(
            onSuccess: response => Results.Ok(response),
            onFailure: error => error.ToProblemDetails());
    }
}
