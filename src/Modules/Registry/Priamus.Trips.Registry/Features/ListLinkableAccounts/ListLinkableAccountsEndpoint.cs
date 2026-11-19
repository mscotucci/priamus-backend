using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.Registry.Features.ListDrivers;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Registry.Features.ListLinkableAccounts;

/// <summary>
/// HTTP endpoint for <c>GET /api/registry/drivers/linkable-accounts</c>.
/// </summary>
public static class ListLinkableAccountsEndpoint
{
    /// <summary>
    /// Maps the ListLinkableAccounts endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapListLinkableAccounts(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/registry/drivers/linkable-accounts", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.BackOfficeOrAdmin)
            .WithName("ListLinkableAccounts")
            .WithTags("Registry.Drivers")
            .Produces<IReadOnlyList<DriverAccountDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [AsParameters] ListLinkableAccountsRequest request,
        [FromServices] ListLinkableAccountsHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.Match(
            onSuccess: response => Results.Ok(response),
            onFailure: error => error.ToProblemDetails());
    }
}
