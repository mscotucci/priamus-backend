using Priamus.Trips.Host.Infrastructure.Stubs;
using Priamus.Trips.Identity.DependencyInjection;
using Priamus.Trips.Registry.Contracts;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ── Logging ──────────────────────────────────────────────────────────────────
builder.Host.UseSerilog((ctx, lc) =>
    lc.ReadFrom.Configuration(ctx.Configuration));

// ── OpenAPI ───────────────────────────────────────────────────────────────────
builder.Services.AddOpenApi();

// DTO validation is registered per-module (e.g. AddIdentityModule). The
// Microsoft.Extensions.Validation source generator only sees [ValidatableType]
// declarations in the assembly that hosts the AddValidation() call site,
// so each module owns its own call to keep its DTOs discoverable.

// ── Temporary stubs (replaced when respective modules land) ──────────────────
// TODO: replace when Registry module lands (ADR 0008)
builder.Services.AddSingleton<IDriverLookup, NullDriverLookup>();

// ── Identity module ───────────────────────────────────────────────────────────
builder.Services.AddIdentityModule(builder.Configuration);

var app = builder.Build();

// ── Middleware ────────────────────────────────────────────────────────────────
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi();

// ── Identity endpoints ────────────────────────────────────────────────────────
app.MapIdentityEndpoints();

app.Run();

// Expose Program for integration test WebApplicationFactory
public partial class Program;
