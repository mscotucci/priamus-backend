using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Priamus.Trips.SharedKernel.Results;
using Shouldly;

namespace Priamus.Trips.IntegrationTesting;

/// <summary>
/// Assertion helper for the problem responses produced by <c>ResultHttpExtensions.ToProblemDetails</c>,
/// so an endpoint test states "409 with error X" in one line instead of re-parsing the body each time.
/// </summary>
public static class ProblemDetailsAssertions
{
    /// <summary>
    /// Asserts the response has <paramref name="expectedStatus"/> and that its <c>errorCode</c> and
    /// <c>errorType</c> match <paramref name="expectedError"/>. The error comes from the aggregate's
    /// <c>{Aggregate}Errors</c> class, never a string literal. The <c>detail</c> is the Italian text
    /// of the module's <c>.resx</c>, asserted by the module's message tests.
    /// </summary>
    public static async Task ShouldBeProblemAsync(
        this HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        Error expectedError,
        CancellationToken cancellationToken = default)
    {
        response.StatusCode.ShouldBe(expectedStatus);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        body.GetProperty("errorCode").GetString().ShouldBe(expectedError.Code);
        body.GetProperty("errorType").GetString().ShouldBe(expectedError.Type.ToString());
    }
}
