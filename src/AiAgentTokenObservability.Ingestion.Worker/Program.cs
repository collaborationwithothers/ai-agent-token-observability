using AiAgentTokenObservability.Ingestion.Worker;
using AiAgentTokenObservability.Storage;
using AiAgentTokenObservability.Storage.Import;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddTelemetryStorage(builder.Configuration);
builder.Services.AddSingleton<CopilotJsonlImportService>();

var importOptions = ImportCommandOptions.FromArgs(args)
    ?? ImportCommandOptions.FromConfiguration(builder.Configuration);
var enrichmentOptions = RepoContextEnrichmentCommandOptions.FromArgs(args)
    ?? RepoContextEnrichmentCommandOptions.FromConfiguration(builder.Configuration);
if (importOptions is not null)
{
    builder.Services.AddSingleton(importOptions);
    builder.Services.AddHostedService<ImportCommandHostedService>();
}
else if (enrichmentOptions is not null)
{
    builder.Services.AddSingleton(enrichmentOptions);
    builder.Services.AddHostedService<RepoContextEnrichmentCommandHostedService>();
}
else
{
    builder.Services.AddHostedService<Worker>();
}

var host = builder.Build();
host.Run();
