using TokenObservability.Api;
using TokenObservability.Infrastructure.Runtime;

var builder = WebApplication.CreateBuilder(args);

builder.AddRuntimeProbeServices();
builder.AddTokenObservabilityApiServices();

var app = builder.Build();

app.MapRuntimeProbeEndpoints();
app.MapTokenObservabilityApiEndpoints();

app.Run();
