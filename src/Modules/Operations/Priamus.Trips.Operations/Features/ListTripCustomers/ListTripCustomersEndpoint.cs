using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Operations.Features.ListTripCustomers;

/// <summary>
/// HTTP endpoint for <c>GET /api/operations/driver/customers</c>.
/// </summary>
public static class ListTripCustomersEndpoint
{
    /// <summary>
    /// Maps the ListTripCustomers endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapListTripCustomers(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/operations/driver/customers", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.Driver)
            .WithName("ListTripCustomers")
            .WithTags("Operations.Driver")
            .Produces<ListTripCustomersResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [AsParameters] ListTripCustomersRequest request,
        [FromServices] ListTripCustomersHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.Match(
            onSuccess: response => Results.Ok(response),
            onFailure: error => error.ToProblemDetails());
    }
}
