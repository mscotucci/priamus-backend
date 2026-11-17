using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.OpenApi;
using Priamus.Trips.Host.Infrastructure.Stubs;
using Priamus.Trips.Identity.DependencyInjection;
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.Registry.DependencyInjection;
using Priamus.Trips.SharedKernel.Http;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ── Logging ──────────────────────────────────────────────────────────────────
builder.Host.UseSerilog((ctx, lc) =>
    lc.ReadFrom.Configuration(ctx.Configuration));

// ── JSON: enums as names, one convention for every module ───────────────────
// namingPolicy: null keeps the C# member name verbatim; allowIntegerValues: false rejects a raw
// integer instead of accepting it as an undocumented alias for the name. Also drives OpenAPI,
// which then documents the enum as its member names instead of an untyped integer.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false)));

// ── OpenAPI ───────────────────────────────────────────────────────────────────
// Query parameters bound with [AsParameters] from SharedKernel types (PagedRequest) have no
// [FromQuery(Name = ...)] available there and would be documented in PascalCase, while JSON
// bodies are camelCase. The framework has no switch for it, so camel-case the query names here.
// Runtime binding is case-insensitive and unaffected.
builder.Services.AddOpenApi(options => options.AddOperationTransformer((operation, _, _) =>
{
    foreach (var parameter in operation.Parameters?.OfType<OpenApiParameter>() ?? [])
    {
        if (parameter is { In: ParameterLocation.Query, Name: { } name })
        {
            parameter.Name = JsonNamingPolicy.CamelCase.ConvertName(name);
        }
    }

    return Task.CompletedTask;
}));

// BadHttpRequestExceptionHandler must be registered before AddProblemDetails(): exception
// handlers run in registration order, and this one has to claim BadHttpRequestException (a
// malformed request body) before the default ProblemDetails fallback maps it to 500.
builder.Services.AddExceptionHandler<BadHttpRequestExceptionHandler>();
builder.Services.AddProblemDetails();

// DTO validation is registered per-module (e.g. AddIdentityModule). The
// Microsoft.Extensions.Validation source generator only sees [ValidatableType]
// declarations in the assembly that hosts the AddValidation() call site,
// so each module owns its own call to keep its DTOs discoverable.

// ── Temporary stubs (replaced when respective modules land) ──────────────────
// TODO: replace when Registry module lands (ADR 0008)
builder.Services.AddSingleton<IDriverLookup, NullDriverLookup>();

// ── Identity module ───────────────────────────────────────────────────────────
builder.Services.AddIdentityModule(builder.Configuration);

// ── Registry module ───────────────────────────────────────────────────────────
builder.Services.AddRegistryModule(builder.Configuration);

var app = builder.Build();

// ── Startup ───────────────────────────────────────────────────────────────────
// Development applies pending migrations; elsewhere a dedicated pre-deploy job does.
if (app.Environment.IsDevelopment())
{
    await app.Services.MigrateIdentityDatabaseAsync();
    await app.Services.MigrateRegistryDatabaseAsync();
}

// The first administrator is created from configuration in every environment, only when no user exists.
await app.Services.BootstrapAdministratorAsync();

// ── Middleware ────────────────────────────────────────────────────────────────
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi();

// ── Identity endpoints ────────────────────────────────────────────────────────
app.MapIdentityEndpoints();

// ── Registry endpoints ────────────────────────────────────────────────────────
app.MapRegistryEndpoints();

app.Run();

// Expose Program for integration test WebApplicationFactory
public partial class Program;
