using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Registry.Features.CreateLocation;

/// <summary>
/// HTTP endpoint for <c>POST /api/registry/locations</c>.
/// </summary>
public static class CreateLocationEndpoint
{
    /// <summary>
    /// Maps the CreateLocation endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapCreateLocation(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/registry/locations", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.BackOfficeOrAdmin)
            .WithName("CreateLocation")
            .WithTags("Registry.Locations")
            .Produces<CreateLocationResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [FromBody] CreateLocationRequest request,
        [FromServices] CreateLocationHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.Match(
            onSuccess: response => Results.Created($"/api/registry/locations/{response.Id}", response),
            onFailure: error => error.ToProblemDetails());
    }
}
