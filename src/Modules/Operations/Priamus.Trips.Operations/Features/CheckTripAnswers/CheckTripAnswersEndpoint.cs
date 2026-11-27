using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Operations.Features.CheckTripAnswers;

/// <summary>
/// HTTP endpoint for <c>GET /api/operations/driver/answers-check</c>.
/// </summary>
public static class CheckTripAnswersEndpoint
{
    /// <summary>
    /// Maps the CheckTripAnswers endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapCheckTripAnswers(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/operations/driver/answers-check", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.Driver)
            .WithName("CheckTripAnswers")
            .WithTags("Operations.Driver")
            .Produces<CheckTripAnswersResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [AsParameters] CheckTripAnswersRequest request,
        [FromServices] CheckTripAnswersHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.Match(
            onSuccess: response => Results.Ok(response),
            onFailure: error => error.ToProblemDetails());
    }
}
