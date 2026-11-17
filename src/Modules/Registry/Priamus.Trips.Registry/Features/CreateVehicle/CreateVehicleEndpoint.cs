using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Registry.Features.CreateVehicle;

/// <summary>
/// HTTP endpoint for <c>POST /api/registry/vehicles</c>.
/// </summary>
public static class CreateVehicleEndpoint
{
    /// <summary>
    /// Maps the CreateVehicle endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapCreateVehicle(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/registry/vehicles", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.BackOfficeOrAdmin)
            .WithName("CreateVehicle")
            .WithTags("Registry.Vehicles")
            .Produces<CreateVehicleResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [FromBody] CreateVehicleRequest request,
        [FromServices] CreateVehicleHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.Match(
            onSuccess: response => Results.Created($"/api/registry/vehicles/{response.Id}", response),
            onFailure: error => error.ToProblemDetails());
    }
}
