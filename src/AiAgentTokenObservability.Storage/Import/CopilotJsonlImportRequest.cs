namespace AiAgentTokenObservability.Storage.Import;

public sealed record CopilotJsonlImportRequest(
    string SourceFilePath,
    string? RepoPath = null,
    string? RepoFriendlyName = null,
    string? DeveloperIdentity = null);

public sealed record CopilotJsonlImportResult(
    Guid TelemetryImportId,
    string SourceFileHash,
    string ImportStatus,
    int RecordCount,
    int SkippedRecordCount,
    int WarningCount,
    int ErrorCount);
