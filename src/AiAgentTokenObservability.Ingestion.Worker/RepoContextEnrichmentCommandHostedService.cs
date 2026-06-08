using AiAgentTokenObservability.Storage.Enrichment;

namespace AiAgentTokenObservability.Ingestion.Worker;

public sealed class RepoContextEnrichmentCommandHostedService(
    RepoContextEnrichmentCommandOptions options,
    RepoContextEnrichmentService enrichmentService,
    IHostApplicationLifetime applicationLifetime,
    ILogger<RepoContextEnrichmentCommandHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var result = await enrichmentService.EnrichAsync(
                new RepoContextEnrichmentRequest(options.RepoPath),
                stoppingToken);

            logger.LogInformation(
                "Repo Context Enrichment completed. WorkspaceRepoId={WorkspaceRepoId} ContextSources={ContextSources} Hotspots={Hotspots} Recommendations={Recommendations}",
                result.WorkspaceRepoId,
                result.ContextSourceCount,
                result.HotspotCount,
                result.RecommendationCount);
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            logger.LogError("Repo Context Enrichment failed with {ErrorType}.", ex.GetType().Name);
        }
        finally
        {
            applicationLifetime.StopApplication();
        }
    }
}
