using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Priamus.Trips.SharedKernel.Http;

/// <summary>
/// Maps a <see cref="BadHttpRequestException"/> escaping minimal-API parameter binding (for
/// example a JSON body value that does not parse into its target type, such as an enum sent as
/// an integer or an unknown name) to a 400 <c>ProblemDetails</c> response.
/// </summary>
/// <remarks>
/// In the Development environment minimal APIs set <c>RouteHandlerOptions.ThrowOnBadRequest</c>
/// to <see langword="true"/>, so a binding failure throws instead of writing a 400, and the
/// default exception pipeline answers 500. This handler makes the mapping explicit and
/// independent of the environment, for every module's endpoints alike.
/// </remarks>
public sealed class BadHttpRequestExceptionHandler : IExceptionHandler
{
    private readonly ILogger<BadHttpRequestExceptionHandler> _logger;

    public BadHttpRequestExceptionHandler(ILogger<BadHttpRequestExceptionHandler> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest)
        {
            return false;
        }

        // Warning, not Error: a client input problem. The exception, which may include the
        // malformed value, goes to the log only and never to the response.
        _logger.LogWarning(
            badRequest, "Rejected a malformed request body: {Message}", badRequest.Message);

        httpContext.Response.StatusCode = badRequest.StatusCode;

        await Microsoft.AspNetCore.Http.Results.Problem(
            statusCode: badRequest.StatusCode,
            title: "Bad Request",
            detail: "The request body could not be read.")
            .ExecuteAsync(httpContext);

        return true;
    }
}
