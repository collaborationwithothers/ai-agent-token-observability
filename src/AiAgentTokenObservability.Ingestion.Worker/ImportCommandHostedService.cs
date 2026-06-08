using AiAgentTokenObservability.Storage.Import;

namespace AiAgentTokenObservability.Ingestion.Worker;

public sealed class ImportCommandHostedService(
    ImportCommandOptions options,
    CopilotJsonlImportService importService,
    IHostApplicationLifetime applicationLifetime,
    ILogger<ImportCommandHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var result = await importService.ImportAsync(
                new CopilotJsonlImportRequest(
                    options.SourceFilePath,
                    options.RepoPath,
                    options.RepoFriendlyName,
                    options.DeveloperIdentity),
                stoppingToken);

            logger.LogInformation(
                "Imported Copilot JSONL. ImportId={ImportId} SourceFileHash={SourceFileHash} Status={Status} Records={Records} Skipped={Skipped} Warnings={Warnings} Errors={Errors}",
                result.TelemetryImportId,
                result.SourceFileHash,
                result.ImportStatus,
                result.RecordCount,
                result.SkippedRecordCount,
                result.WarningCount,
                result.ErrorCount);
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            logger.LogError("Copilot JSONL import failed with {ErrorType}.", ex.GetType().Name);
        }
        finally
        {
            applicationLifetime.StopApplication();
        }
    }
}
