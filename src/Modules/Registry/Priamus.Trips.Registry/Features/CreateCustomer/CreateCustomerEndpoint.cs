using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Registry.Features.CreateCustomer;

/// <summary>
/// HTTP endpoint for <c>POST /api/registry/customers</c>.
/// </summary>
public static class CreateCustomerEndpoint
{
    /// <summary>
    /// Maps the CreateCustomer endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapCreateCustomer(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/registry/customers", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.BackOfficeOrAdmin)
            .WithName("CreateCustomer")
            .WithTags("Registry.Customers")
            .Produces<CreateCustomerResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [FromBody] CreateCustomerRequest request,
        [FromServices] CreateCustomerHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.Match(
            onSuccess: response => Results.Created($"/api/registry/customers/{response.Id}", response),
            onFailure: error => error.ToProblemDetails());
    }
}
