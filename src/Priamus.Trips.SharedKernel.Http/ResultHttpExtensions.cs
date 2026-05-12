using Microsoft.AspNetCore.Http;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.SharedKernel.Http;

/// <summary>
/// Maps <see cref="Error"/> values to ASP.NET Core <see cref="IResult"/> ProblemDetails responses.
/// Mapping is driven exclusively by <see cref="Error.Type"/>: never branch on <see cref="Error.Code"/>.
/// Adding a new error in a module therefore does not require modifying this class.
/// </summary>
/// <remarks>
/// SharedKernel core is intentionally framework-agnostic; this project adds the ASP.NET Core bridge.
/// </remarks>
public static class ResultHttpExtensions
{
    /// <summary>
    /// Converts an <see cref="Error"/> to a ProblemDetails <see cref="IResult"/>.
    /// </summary>
    /// <param name="error">The error to convert. Must not be <see cref="Error.None"/>.</param>
    /// <returns>An <see cref="IResult"/> representing a ProblemDetails response.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="error"/> is <see cref="Error.None"/>.</exception>
    public static IResult ToProblemDetails(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var (status, title) = error.Type switch
        {
            ErrorType.Validation => (StatusCodes.Status400BadRequest, "Bad Request"),
            ErrorType.Unauthorized => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            ErrorType.Forbidden => (StatusCodes.Status403Forbidden, "Forbidden"),
            ErrorType.NotFound => (StatusCodes.Status404NotFound, "Not Found"),
            ErrorType.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
            ErrorType.Domain => (StatusCodes.Status422UnprocessableEntity, "Unprocessable Entity"),
            ErrorType.Unexpected => (StatusCodes.Status500InternalServerError, "Internal Server Error"),
            ErrorType.None => throw new InvalidOperationException("Cannot map Error.None to a ProblemDetails response."),
            _ => throw new InvalidOperationException($"Unhandled ErrorType: {error.Type}."),
        };

        return Microsoft.AspNetCore.Http.Results.Problem(
            statusCode: status,
            title: title,
            detail: error.Description,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = error.Code,
                ["errorType"] = error.Type.ToString(),
            });
    }
}
