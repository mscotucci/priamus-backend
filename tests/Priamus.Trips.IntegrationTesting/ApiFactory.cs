using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Priamus.Trips.IntegrationTesting;

/// <summary>
/// Builds the API host for end-to-end tests: the real <c>Program</c> pointed at a test's isolated
/// database, with deterministic JWT options so tokens issued by <c>POST /api/auth/login</c> validate.
/// </summary>
public static class ApiFactory
{
    /// <summary>
    /// Creates a host bound to <paramref name="connectionString"/>. Settings go through
    /// <c>UseSetting</c> because the modules read their configuration while <c>Program</c> runs,
    /// before any <c>ConfigureAppConfiguration</c> callback would apply.
    /// </summary>
    /// <param name="connectionString">Connection string of the test's isolated database.</param>
    /// <param name="configureServices">Optional DI overrides (a fixed clock, a stubbed lookup).</param>
    public static WebApplicationFactory<Program> Create(
        string connectionString,
        Action<IServiceCollection>? configureServices = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:Database", connectionString);
                // appsettings.Development.json carries development bootstrap credentials: an
                // empty value keeps every test host free of an administrator unless a test sets one.
                builder.UseSetting("Identity:BootstrapAdmin:Username", string.Empty);
                builder.UseSetting("Identity:BootstrapAdmin:Password", string.Empty);
                builder.UseSetting("Jwt:Issuer", "test-issuer");
                builder.UseSetting("Jwt:Audience", "test-audience");
                builder.UseSetting("Jwt:SigningKey", "test-signing-key-endpoint-32chars!!");
                builder.UseSetting("Jwt:AccessTokenLifetime", "01:00:00");
                builder.UseSetting("Jwt:RefreshTokenLifetime", "12:00:00");

                if (configureServices is not null)
                {
                    builder.ConfigureServices(configureServices);
                }
            });
    }
}
