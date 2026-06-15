using TokenObservability.Infrastructure.Runtime;
using TokenObservability.Ingestion;

var builder = WebApplication.CreateBuilder(args);

builder.AddRuntimeProbeServices();
builder.AddTokenObservabilityIngestionServices();

var app = builder.Build();

app.MapRuntimeProbeEndpoints();
app.MapTokenObservabilityIngestionEndpoints();

app.Run();
