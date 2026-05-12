// Placeholder Host bootstrap. Real wiring is added by the feature-implementer agent.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
// Endpoints are registered by each module via {Module}ModuleEndpoints.Map{Module}Endpoints(app). No endpoints exist yet because no module has shipped its first feature.
app.Run();
