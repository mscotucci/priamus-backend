using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Registry.Features.SimulatePrice;

/// <summary>
/// HTTP endpoint for <c>POST /api/registry/contracts/simulation</c>.
/// </summary>
public static class SimulatePriceEndpoint
{
    /// <summary>
    /// Maps the SimulatePrice endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapSimulatePrice(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/registry/contracts/simulation", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.BackOfficeOrAdmin)
            .WithName("SimulatePrice")
            .WithTags("Registry.Contracts")
            .Produces<PriceSimulationDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [FromBody] SimulatePriceRequest request,
        [FromServices] SimulatePriceHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.Match(
            onSuccess: response => Results.Ok(response),
            onFailure: error => error.ToProblemDetails());
    }
}
