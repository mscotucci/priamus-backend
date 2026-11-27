using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.SharedKernel.Http;

namespace Priamus.Trips.Operations.Features.GetTripQuestions;

/// <summary>
/// HTTP endpoint for <c>GET /api/operations/driver/trip-questions</c>.
/// </summary>
public static class GetTripQuestionsEndpoint
{
    /// <summary>
    /// Maps the GetTripQuestions endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapGetTripQuestions(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/operations/driver/trip-questions", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.Driver)
            .WithName("GetTripQuestions")
            .WithTags("Operations.Driver")
            .Produces<TripQuestionsDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        [AsParameters] GetTripQuestionsRequest request,
        [FromServices] GetTripQuestionsHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.Match(
            onSuccess: response => Results.Ok(response),
            onFailure: error => error.ToProblemDetails());
    }
}
