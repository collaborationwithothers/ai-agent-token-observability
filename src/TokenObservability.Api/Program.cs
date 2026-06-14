using TokenObservability.Infrastructure.Runtime;

var builder = WebApplication.CreateBuilder(args);

builder.AddRuntimeProbeServices();

var app = builder.Build();

app.MapRuntimeProbeEndpoints();
app.MapGet("/api/v1", () => Results.Ok(new
{
    service = "token-observability-api",
    status = "placeholder"
}));

app.Run();
