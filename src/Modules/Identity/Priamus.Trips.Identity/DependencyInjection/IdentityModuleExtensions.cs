using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Validation;
using Microsoft.IdentityModel.Tokens;
using Priamus.Trips.Identity.Application.Abstractions;
using Priamus.Trips.Identity.Application.Services;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Infrastructure;
using Priamus.Trips.Identity.Persistence.DependencyInjection;
using Priamus.Trips.Identity.Resources;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Identity.DependencyInjection;

/// <summary>
/// Extension methods for registering Identity module services.
/// </summary>
public static class IdentityModuleExtensions
{
    /// <summary>
    /// Registers all Identity module services: persistence, JWT infrastructure,
    /// application abstractions, authorization policies, and handlers (via Scrutor).
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">Application configuration (provides the "Jwt" section).</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        // ── DTO validation (Microsoft.Extensions.Validation source generator) ────
        // Called per-module so that the source generator runs in THIS assembly and
        // discovers every [ValidatableType] declared here. The generator emits a
        // resolver scoped to the assembly that hosts the AddValidation() call site,
        // so a single host-level invocation would only see types in the Host project.
        services.AddValidation();

        // Italian messages: the localizer reads Resources/IdentityMessages.resx, keyed by Error.Code.
        services.AddLocalization();
        services.Configure<ProblemDetailsOptions>(options =>
            options.CustomizeProblemDetails += LocalizeProblemDetail);

        // ── Persistence ──────────────────────────────────────────────────────────
        var connectionString = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'Database' is required for the Identity module.");
        }

        services.AddIdentityPersistence(connectionString);

        // ── JWT options ──────────────────────────────────────────────────────────
        services
            .AddOptions<JwtOptions>()
            .BindConfiguration("Jwt")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ── Bootstrap administrator ──────────────────────────────────────────────
        // No validation: absent or incomplete values only mean that no administrator is created.
        services
            .AddOptions<BootstrapAdminOptions>()
            .BindConfiguration("Identity:BootstrapAdmin");
        services.AddScoped<AdminBootstrapService>();
        services.AddScoped<IUserLookup, UserLookup>();

        // ── Infrastructure ───────────────────────────────────────────────────────
        services.AddSingleton<IJwtTokenIssuer, JwtTokenIssuer>();
        services.AddSingleton<IRefreshTokenHasher, Sha256RefreshTokenHasher>();
        services.AddScoped<
            Microsoft.AspNetCore.Identity.IPasswordHasher<User>,
            Microsoft.AspNetCore.Identity.PasswordHasher<User>>();

        // ── IClock ───────────────────────────────────────────────────────────────
        // Registered as singleton so all handlers share the same production clock.
        services.AddSingleton<IClock, SystemClock>();

        // ── ICurrentUser ─────────────────────────────────────────────────────────
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        // ── JWT Bearer authentication ─────────────────────────────────────────────
        // Read raw config values for JwtBearer middleware setup. The options object
        // is separately validated at startup via ValidateOnStart(); here we only need
        // the values to configure the TokenValidationParameters.
        var jwtSection = configuration.GetSection("Jwt");
        var jwtIssuer = jwtSection["Issuer"] ?? string.Empty;
        var jwtAudience = jwtSection["Audience"] ?? string.Empty;
        // Use a placeholder key if the signing key is not configured; ValidateOnStart()
        // will reject the application before it can serve requests with an invalid key.
        var jwtSigningKey = jwtSection["SigningKey"] ?? new string('x', 32);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false; // preserve original claim types (e.g. "sub", not ClaimTypes.NameIdentifier)
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = jwtAudience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtSigningKey)),
                    // JsonWebTokenHandler does not auto-remap to legacy ClaimTypes URIs;
                    // configure short-name claim types so ICurrentUser can read them directly.
                    NameClaimType = "sub",
                    RoleClaimType = "role",
                };
            });

        // ── Authorization policies ───────────────────────────────────────────────
        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                AuthorizationPolicies.Authenticated,
                policy => policy.RequireAuthenticatedUser());

            options.AddPolicy(
                AuthorizationPolicies.Admin,
                policy => policy.RequireRole(UserRole.Admin.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.BackOfficeOrAdmin,
                policy => policy.RequireRole(
                    UserRole.BackOffice.ToString(),
                    UserRole.Admin.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.Driver,
                policy => policy
                    .RequireRole(UserRole.Driver.ToString())
                    .RequireClaim("driver_id"));
        });

        // ── Handlers (Scrutor scan) ──────────────────────────────────────────────
        services.Scan(scan => scan
            .FromAssemblyOf<IdentityModuleMarker>()
            .AddClasses(c => c.Where(t => t.Name.EndsWith("Handler")), publicOnly: false)
            .AsSelf()
            .WithScopedLifetime());

        return services;
    }

    /// <summary>
    /// Replaces the English <c>detail</c> of a problem with the Italian text keyed by its
    /// <c>errorCode</c>. A code with no entry keeps the error description, so errors of other
    /// modules answer unchanged.
    /// </summary>
    private static void LocalizeProblemDetail(ProblemDetailsContext context)
    {
        if (!context.ProblemDetails.Extensions.TryGetValue("errorCode", out var code) || code is not string errorCode)
        {
            return;
        }

        var localizer = context.HttpContext.RequestServices.GetRequiredService<IStringLocalizer<IdentityMessages>>();
        var message = localizer[errorCode];
        if (!message.ResourceNotFound)
        {
            context.ProblemDetails.Detail = message.Value;
        }
    }
}

/// <summary>Marker class for assembly scanning.</summary>
internal sealed class IdentityModuleMarker;
