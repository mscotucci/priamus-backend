using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.Registry.Features.ListContracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Registry.Features.GetContract;

/// <summary>
/// HTTP endpoint for <c>GET /api/registry/customers/{customerId:guid}/contracts/{contractId:guid}</c>.
/// </summary>
public static class GetContractEndpoint
{
    /// <summary>
    /// Maps the GetContract endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapGetContract(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/registry/customers/{customerId:guid}/contracts/{contractId:guid}", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.BackOfficeOrAdmin)
            .WithName("GetContract")
            .WithTags("Registry.Contracts")
            .Produces<ContractDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        Guid customerId,
        Guid contractId,
        [FromServices] GetContractHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(customerId, contractId, cancellationToken);

        return result.Match(
            onSuccess: response => Results.Ok(response),
            onFailure: error => error.ToProblemDetails());
    }
}
