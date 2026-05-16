using Microsoft.AspNetCore.Routing;
using Priamus.Trips.Identity.Features.AdminRevoke;
using Priamus.Trips.Identity.Features.CreateUser;
using Priamus.Trips.Identity.Features.Login;
using Priamus.Trips.Identity.Features.Refresh;
using Priamus.Trips.Identity.Features.Revoke;

namespace Priamus.Trips.Identity.DependencyInjection;

/// <summary>
/// Enumerates all Identity module HTTP endpoints.
/// </summary>
public static class IdentityModuleEndpoints
{
    /// <summary>
    /// Maps all Identity module endpoints onto the given route builder.
    /// </summary>
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapLogin();
        app.MapRefresh();
        app.MapRevoke();
        app.MapAdminRevoke();
        app.MapCreateUser();
        return app;
    }
}
