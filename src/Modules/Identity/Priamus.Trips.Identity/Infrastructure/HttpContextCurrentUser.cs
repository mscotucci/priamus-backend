using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Priamus.Trips.SharedKernel.Security;

namespace Priamus.Trips.Identity.Infrastructure;

/// <summary>
/// ASP.NET Core implementation of <see cref="ICurrentUser"/>.
/// Reads identity from the HTTP context's <see cref="ClaimsPrincipal"/>.
/// Uses short claim names (<c>sub</c>, <c>role</c>) as configured in
/// <see cref="DependencyInjection.IdentityModuleExtensions"/> via
/// <c>TokenValidationParameters.NameClaimType</c> and <c>RoleClaimType</c>.
/// </summary>
internal sealed class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    /// <inheritdoc />
    public UserId UserId
    {
        get
        {
            if (!IsAuthenticated)
            {
                throw new InvalidOperationException(
                    "Cannot access UserId for an unauthenticated request.");
            }

            var sub = _httpContextAccessor.HttpContext!.User.FindFirstValue("sub")
                ?? throw new InvalidOperationException(
                    "Authenticated JWT is missing the 'sub' claim.");

            if (!Guid.TryParse(sub, out var id))
            {
                throw new InvalidOperationException(
                    $"The 'sub' claim value '{sub}' is not a valid Guid.");
            }

            return new UserId(id);
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> Roles =>
        _httpContextAccessor.HttpContext?.User
            .FindAll("role")
            .Select(c => c.Value)
            .ToList()
            ?? [];
}
