using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Registry.Features.ReactivateDriver;

/// <summary>
/// HTTP endpoint for <c>POST /api/registry/drivers/{id:guid}/reactivate</c>.
/// </summary>
public static class ReactivateDriverEndpoint
{
    /// <summary>
    /// Maps the ReactivateDriver endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapReactivateDriver(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/registry/drivers/{id:guid}/reactivate", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.BackOfficeOrAdmin)
            .WithName("ReactivateDriver")
            .WithTags("Registry.Drivers")
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
        [FromBody] ReactivateDriverRequest request,
        [FromServices] ReactivateDriverHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, request, cancellationToken);

        return result.Match(
            onSuccess: () => Results.NoContent(),
            onFailure: error => error.ToProblemDetails());
    }
}
