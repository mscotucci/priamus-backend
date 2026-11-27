using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Priamus.Trips.IntegrationTesting;

/// <summary>
/// The caller of an endpoint test: the claims the real JWT would carry (<c>sub</c>, <c>role</c>),
/// without seeding a user and logging in. Chosen per client with <see cref="ApplyTo"/>; a client
/// that applies none is anonymous.
/// </summary>
/// <param name="UserId">Value of the <c>sub</c> claim.</param>
/// <param name="Role">Value of the <c>role</c> claim: <c>Admin</c>, <c>BackOffice</c> or <c>Driver</c>.</param>
/// <param name="DriverId">Value of the <c>driver_id</c> claim; <c>null</c> for a session that names no driver.</param>
public sealed record TestCaller(Guid UserId, string Role, Guid? DriverId)
{
    internal const string Scheme = "Test";
    internal const string UserIdHeader = "X-Test-User-Id";
    internal const string RoleHeader = "X-Test-Role";
    internal const string DriverIdHeader = "X-Test-Driver-Id";

    /// <summary>A caller with the given role, no driver and the fixed actor id of <see cref="FakeCurrentUser"/>.</summary>
    /// <param name="role">The role claim.</param>
    public static TestCaller WithRole(string role) => new(FakeCurrentUser.ActorId.Value, role, null);

    /// <summary>A Driver whose session names the driver <paramref name="driverId"/>.</summary>
    /// <param name="driverId">The <c>driver_id</c> claim.</param>
    public static TestCaller DriverWith(Guid driverId) => new(FakeCurrentUser.ActorId.Value, "Driver", driverId);

    /// <summary>Makes every request of <paramref name="client"/> carry this caller's claims.</summary>
    /// <param name="client">The client of a host built with <c>ApiFactory.Create(..., useTestAuthentication: true)</c>.</param>
    public HttpClient ApplyTo(HttpClient client)
    {
        client.DefaultRequestHeaders.Add(UserIdHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(RoleHeader, Role);
        if (DriverId is { } driverId)
        {
            client.DefaultRequestHeaders.Add(DriverIdHeader, driverId.ToString());
        }

        return client;
    }
}

/// <summary>
/// Authentication handler of the test scheme: reads the caller from the headers set by
/// <see cref="TestCaller.ApplyTo"/>. With no headers the request is anonymous, so the policies
/// answer 401 exactly as with a missing token; with a role they answer 403 or 200 as with a real one.
/// </summary>
internal sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(TestCaller.UserIdHeader, out var userId)
            || !Request.Headers.TryGetValue(TestCaller.RoleHeader, out var role))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // Short claim names, as the JWT bearer setup of the Identity module reads them.
        var claims = new List<Claim> { new("sub", userId.ToString()), new("role", role.ToString()) };
        if (Request.Headers.TryGetValue(TestCaller.DriverIdHeader, out var driverId))
        {
            claims.Add(new Claim("driver_id", driverId.ToString()));
        }

        var identity = new ClaimsIdentity(
            claims,
            TestCaller.Scheme,
            nameType: "sub",
            roleType: "role");

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), TestCaller.Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
