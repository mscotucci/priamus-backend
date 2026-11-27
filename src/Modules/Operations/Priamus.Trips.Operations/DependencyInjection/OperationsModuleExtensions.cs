using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Operations.Application.Services;
using Priamus.Trips.Operations.Persistence.DependencyInjection;
using Priamus.Trips.Operations.Resources;

namespace Priamus.Trips.Operations.DependencyInjection;

/// <summary>
/// Extension methods for registering Operations module services.
/// </summary>
public static class OperationsModuleExtensions
{
    /// <summary>
    /// Registers all Operations module services: DTO validation, Italian messages, persistence and
    /// handlers (via Scrutor). Authentication, authorization policies, <c>IClock</c> and <c>ICurrentUser</c> are
    /// registered by the Identity module, <c>ITripRegistry</c> by the Registry module.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">Application configuration (provides the "Database" connection string).</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddOperationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Called per-module so that the validation source generator runs in THIS assembly and
        // discovers every [ValidatableType] declared here.
        services.AddValidation();

        // Italian messages: the localizer reads Resources/OperationsMessages.resx, keyed by Error.Code.
        services.AddLocalization();
        services.AddScoped<ErrorMessageArguments>();
        services.Configure<ProblemDetailsOptions>(options =>
            options.CustomizeProblemDetails += LocalizeProblemDetail);

        var connectionString = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'Database' is required for the Operations module.");
        }

        services.AddOperationsPersistence(connectionString);

        services.Scan(scan => scan
            .FromAssemblyOf<OperationsModuleMarker>()
            .AddClasses(c => c.Where(t => t.Name.EndsWith("Handler")), publicOnly: false)
            .AsSelf()
            .WithScopedLifetime());

        return services;
    }

    /// <summary>
    /// Replaces the English <c>detail</c> of a problem with the Italian text keyed by its
    /// <c>errorCode</c>, formatted with the values a handler stored in <see cref="ErrorMessageArguments"/>.
    /// A code with no entry keeps the error description, so errors of other modules answer unchanged.
    /// </summary>
    private static void LocalizeProblemDetail(ProblemDetailsContext context)
    {
        if (!context.ProblemDetails.Extensions.TryGetValue("errorCode", out var code) || code is not string errorCode)
        {
            return;
        }

        var services = context.HttpContext.RequestServices;
        var localizer = services.GetRequiredService<IStringLocalizer<OperationsMessages>>();
        var message = localizer[errorCode];
        if (message.ResourceNotFound)
        {
            return;
        }

        var arguments = services.GetRequiredService<ErrorMessageArguments>().Values;
        context.ProblemDetails.Detail = arguments.Count == 0
            ? message.Value
            : string.Format(System.Globalization.CultureInfo.InvariantCulture, message.Value, [.. arguments]);
    }
}

/// <summary>Marker class for assembly scanning.</summary>
internal sealed class OperationsModuleMarker;
