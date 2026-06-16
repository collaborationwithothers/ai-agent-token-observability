using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry.Trace;
using TokenObservability.Infrastructure.Ingestion;
using TokenObservability.Infrastructure.Runtime;
using TokenObservability.Ingestion;

var builder = WebApplication.CreateBuilder(args);

var openTelemetry = builder.Services
    .AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(IngestionDiagnosticLoggerSink.ActivitySourceName));

var azureMonitorConnectionString =
    builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"] ??
    builder.Configuration["AzureMonitor:ConnectionString"];

if (!string.IsNullOrWhiteSpace(azureMonitorConnectionString))
{
    openTelemetry.UseAzureMonitor(options =>
    {
        options.ConnectionString = azureMonitorConnectionString;
    });
}

builder.AddRuntimeProbeServices();
builder.AddTokenObservabilityIngestionServices();

var app = builder.Build();

app.MapRuntimeProbeEndpoints();
app.MapTokenObservabilityIngestionEndpoints();

app.Run();
