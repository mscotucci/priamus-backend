using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Registry.Features.ReactivateCustomer;

/// <summary>
/// HTTP endpoint for <c>POST /api/registry/customers/{id:guid}/reactivate</c>.
/// </summary>
public static class ReactivateCustomerEndpoint
{
    /// <summary>
    /// Maps the ReactivateCustomer endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapReactivateCustomer(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/registry/customers/{id:guid}/reactivate", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.BackOfficeOrAdmin)
            .WithName("ReactivateCustomer")
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
        [FromBody] ReactivateCustomerRequest request,
        [FromServices] ReactivateCustomerHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, request, cancellationToken);

        return result.Match(
            onSuccess: () => Results.NoContent(),
            onFailure: error => error.ToProblemDetails());
    }
}
