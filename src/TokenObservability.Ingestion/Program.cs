using TokenObservability.Infrastructure.Runtime;

var builder = WebApplication.CreateBuilder(args);

builder.AddRuntimeProbeServices();

var app = builder.Build();

app.MapRuntimeProbeEndpoints();
app.MapGet("/ingestion", () => Results.Ok(new
{
    service = "token-observability-ingestion",
    status = "placeholder"
}));

app.Run();
