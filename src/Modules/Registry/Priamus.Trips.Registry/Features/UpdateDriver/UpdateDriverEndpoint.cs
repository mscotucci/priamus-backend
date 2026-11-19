using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Registry.Features.UpdateDriver;

/// <summary>
/// HTTP endpoint for <c>PUT /api/registry/drivers/{id:guid}</c>.
/// </summary>
public static class UpdateDriverEndpoint
{
    /// <summary>
    /// Maps the UpdateDriver endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapUpdateDriver(this IEndpointRouteBuilder app)
    {
        app.MapPut("/api/registry/drivers/{id:guid}", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.BackOfficeOrAdmin)
            .WithName("UpdateDriver")
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
        [FromBody] UpdateDriverRequest request,
        [FromServices] UpdateDriverHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, request, cancellationToken);

        return result.Match(
            onSuccess: () => Results.NoContent(),
            onFailure: error => error.ToProblemDetails());
    }
}
