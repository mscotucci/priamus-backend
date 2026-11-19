using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.Registry.Features.ListDrivers;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Registry.Features.CreateDriver;

/// <summary>
/// HTTP endpoint for <c>POST /api/registry/drivers</c>.
/// </summary>
public static class CreateDriverEndpoint
{
    /// <summary>
    /// Maps the CreateDriver endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapCreateDriver(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/registry/drivers", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.BackOfficeOrAdmin)
            .WithName("CreateDriver")
            .WithTags("Registry.Drivers")
            .Produces<DriverListItemDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [FromBody] CreateDriverRequest request,
        [FromServices] CreateDriverHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.Match(
            onSuccess: response => Results.Created($"/api/registry/drivers/{response.Id}", response),
            onFailure: error => error.ToProblemDetails());
    }
}
