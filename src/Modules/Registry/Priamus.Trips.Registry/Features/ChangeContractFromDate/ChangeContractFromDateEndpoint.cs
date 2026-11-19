using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.Registry.Features.ListContracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Registry.Features.ChangeContractFromDate;

/// <summary>
/// HTTP endpoint for <c>POST /api/registry/customers/{customerId:guid}/contracts/{contractId:guid}/change-from-date</c>.
/// </summary>
public static class ChangeContractFromDateEndpoint
{
    /// <summary>
    /// Maps the ChangeContractFromDate endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapChangeContractFromDate(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/registry/customers/{customerId:guid}/contracts/{contractId:guid}/change-from-date", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.BackOfficeOrAdmin)
            .WithName("ChangeContractFromDate")
            .WithTags("Registry.Contracts")
            .Produces<ContractDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        Guid customerId,
        Guid contractId,
        [FromBody] ChangeContractFromDateRequest request,
        [FromServices] ChangeContractFromDateHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(customerId, contractId, request, cancellationToken);

        return result.Match(
            onSuccess: response => Results.Created(
                $"/api/registry/customers/{customerId}/contracts/{response.Id}", response),
            onFailure: error => error.ToProblemDetails());
    }
}
