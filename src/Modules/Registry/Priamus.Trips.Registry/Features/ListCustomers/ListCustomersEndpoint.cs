using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;
using Priamus.Trips.SharedKernel.Paging;

namespace Priamus.Trips.Registry.Features.ListCustomers;

/// <summary>
/// HTTP endpoint for <c>GET /api/registry/customers</c>.
/// </summary>
public static class ListCustomersEndpoint
{
    /// <summary>
    /// Maps the ListCustomers endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapListCustomers(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/registry/customers", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.BackOfficeOrAdmin)
            .WithName("ListCustomers")
            .WithTags("Registry.Customers")
            .Produces<PagedResponse<CustomerListItemDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [AsParameters] ListCustomersRequest request,
        [FromServices] ListCustomersHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.Match(
            onSuccess: response => Results.Ok(response),
            onFailure: error => error.ToProblemDetails());
    }
}
