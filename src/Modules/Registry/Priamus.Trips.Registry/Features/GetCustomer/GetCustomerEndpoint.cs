using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.Registry.Features.ListCustomers;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Registry.Features.GetCustomer;

/// <summary>
/// HTTP endpoint for <c>GET /api/registry/customers/{id:guid}</c>.
/// </summary>
public static class GetCustomerEndpoint
{
    /// <summary>
    /// Maps the GetCustomer endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapGetCustomer(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/registry/customers/{id:guid}", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.BackOfficeOrAdmin)
            .WithName("GetCustomer")
            .WithTags("Registry.Customers")
            .Produces<CustomerListItemDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        [FromServices] GetCustomerHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken);

        return result.Match(
            onSuccess: response => Results.Ok(response),
            onFailure: error => error.ToProblemDetails());
    }
}
