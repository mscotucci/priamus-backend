using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Registry.Features.ListContracts;

/// <summary>
/// HTTP endpoint for <c>GET /api/registry/customers/{customerId:guid}/contracts</c>.
/// </summary>
public static class ListContractsEndpoint
{
    /// <summary>
    /// Maps the ListContracts endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapListContracts(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/registry/customers/{customerId:guid}/contracts", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.BackOfficeOrAdmin)
            .WithName("ListContracts")
            .WithTags("Registry.Contracts")
            .Produces<ContractDto[]>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        Guid customerId,
        [FromServices] ListContractsHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(customerId, cancellationToken);

        return result.Match(
            onSuccess: response => Results.Ok(response),
            onFailure: error => error.ToProblemDetails());
    }
}
