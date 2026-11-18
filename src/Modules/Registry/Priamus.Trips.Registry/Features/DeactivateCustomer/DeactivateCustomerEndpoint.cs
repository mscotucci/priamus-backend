using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Registry.Features.DeactivateCustomer;

/// <summary>
/// HTTP endpoint for <c>POST /api/registry/customers/{id:guid}/deactivate</c>.
/// </summary>
public static class DeactivateCustomerEndpoint
{
    /// <summary>
    /// Maps the DeactivateCustomer endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapDeactivateCustomer(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/registry/customers/{id:guid}/deactivate", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.BackOfficeOrAdmin)
            .WithName("DeactivateCustomer")
            .WithTags("Registry.Customers")
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
        [FromBody] DeactivateCustomerRequest request,
        [FromServices] DeactivateCustomerHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, request, cancellationToken);

        return result.Match(
            onSuccess: () => Results.NoContent(),
            onFailure: error => error.ToProblemDetails());
    }
}
