using AiAgentTokenObservability.Contracts.Sessions;
using AiAgentTokenObservability.Contracts.Insights;

namespace AiAgentTokenObservability.Storage;

public sealed class TelemetryImportModel
{
    public Guid TelemetryImportId { get; init; } = Guid.NewGuid();
    public required string Harness { get; init; }
    public required string SourceKind { get; init; }
    public required string SourceFileHash { get; init; }
    public string? EnvironmentName { get; set; }
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public required string ImportStatus { get; set; }
    public int RecordCount { get; set; }
    public int SkippedRecordCount { get; set; }
    public int WarningCount { get; set; }
    public int ErrorCount { get; set; }
    public required AgentSessionModel Session { get; init; }
    public List<TelemetryRecordModel> TelemetryRecords { get; } = [];
    public List<AgentTurnModel> AgentTurns { get; } = [];
    public List<ModelInvocationModel> ModelInvocations { get; } = [];
    public List<TokenMetricModel> TokenMetrics { get; } = [];
    public List<ToolCallModel> ToolCalls { get; } = [];
    public List<WorkspaceRepoModel> WorkspaceRepos { get; } = [];
    public List<MetricObservationModel> MetricObservations { get; } = [];
}

public sealed class AgentSessionModel
{
    public Guid AgentSessionId { get; init; } = Guid.NewGuid();
    public required Guid TelemetryImportId { get; init; }
    public required string Harness { get; init; }
    public string? HarnessSource { get; set; }
    public string? HarnessVersion { get; set; }
    public string? AgentName { get; set; }
    public string? ProviderSessionIdHash { get; set; }
    public string? TeamHash { get; set; }
    public string? UserHash { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public required string TokenTotalType { get; set; }
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public bool ContentCaptured { get; init; }
}

public sealed class WorkspaceRepoModel
{
    public Guid WorkspaceRepoId { get; init; } = Guid.NewGuid();
    public required Guid AgentSessionId { get; init; }
    public string? RepoFriendlyName { get; init; }
    public required string RepoPathHash { get; init; }
    public string? RepoPath { get; init; }
}

public sealed class TelemetryRecordModel
{
    public Guid TelemetryRecordId { get; init; } = Guid.NewGuid();
    public required Guid TelemetryImportId { get; init; }
    public Guid? AgentSessionId { get; init; }
    public required int RecordIndex { get; init; }
    public required string RecordKind { get; init; }
    public string? BodyRedactedSummary { get; init; }
    public string? EventName { get; init; }
    public string? TraceIdHash { get; init; }
    public string? SpanIdHash { get; init; }
    public int? TraceFlags { get; init; }
    public DateTimeOffset? ObservedAtUtc { get; init; }
    public DateTimeOffset? ReceivedAtUtc { get; init; }
    public string? InstrumentationScopeName { get; init; }
    public string? InstrumentationScopeVersion { get; init; }
    public int? AttributeCount { get; init; }
}

public sealed class AgentTurnModel
{
    public Guid AgentTurnId { get; init; } = Guid.NewGuid();
    public required Guid AgentSessionId { get; init; }
    public Guid? TelemetryRecordId { get; init; }
    public int? TurnIndex { get; init; }
    public int? ToolCallCount { get; init; }
    public bool? Success { get; init; }
    public DateTimeOffset? StartedAtUtc { get; init; }
}

public sealed class ModelInvocationModel
{
    public Guid ModelInvocationId { get; init; } = Guid.NewGuid();
    public required Guid AgentSessionId { get; init; }
    public Guid? AgentTurnId { get; init; }
    public required Guid TelemetryRecordId { get; init; }
    public string? OperationName { get; init; }
    public string? ProviderName { get; init; }
    public string? RequestModel { get; init; }
    public string? ResponseModel { get; init; }
    public string? ProviderResponseIdHash { get; init; }
    public string? FinishReasonsJson { get; init; }
    public int? RequestMaxTokens { get; init; }
    public decimal? RequestTemperature { get; init; }
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public required string TokenTotalType { get; init; }
}

public sealed class TokenMetricModel
{
    public Guid TokenMetricId { get; init; } = Guid.NewGuid();
    public Guid? ModelInvocationId { get; init; }
    public Guid? AgentSessionId { get; init; }
    public required string MetricName { get; init; }
    public required string MetricStatus { get; init; }
    public required string MetricConfidence { get; init; }
    public long? Value { get; init; }
    public required string Source { get; init; }
}

public sealed class ToolCallModel
{
    public Guid ToolCallId { get; init; } = Guid.NewGuid();
    public required Guid AgentSessionId { get; init; }
    public Guid? AgentTurnId { get; init; }
    public Guid? TelemetryRecordId { get; init; }
    public string? ToolName { get; init; }
    public long? DurationMs { get; init; }
    public bool? Success { get; init; }
    public bool ArgumentsCaptured { get; init; }
    public bool ResultCaptured { get; init; }
}

public sealed class MetricObservationModel
{
    public Guid MetricObservationId { get; init; } = Guid.NewGuid();
    public required Guid TelemetryImportId { get; init; }
    public Guid? AgentSessionId { get; init; }
    public string? ScopeName { get; init; }
    public string? ScopeVersion { get; init; }
    public required string MetricName { get; init; }
    public required string MetricType { get; init; }
    public int? ValueType { get; init; }
    public string? Unit { get; init; }
    public string? Description { get; init; }
    public int? AggregationTemporality { get; init; }
    public bool? IsMonotonic { get; init; }
    public int? DataPointType { get; init; }
    public DateTimeOffset? StartTimeUtc { get; init; }
    public DateTimeOffset? EndTimeUtc { get; init; }
    public string? AttributesJson { get; init; }
    public string? ValueJson { get; init; }
    public string? BucketBoundariesJson { get; init; }
}

public sealed class ContextSourceModel
{
    public Guid ContextSourceId { get; init; } = Guid.NewGuid();
    public required Guid WorkspaceRepoId { get; init; }
    public required string SourceType { get; init; }
    public required string PathHash { get; init; }
    public string? DisplayPath { get; init; }
    public required string FileCategory { get; init; }
    public string? SpecArtifactStatus { get; init; }
    public bool EligibleForInferredHotspot { get; init; }
    public long? SizeBytes { get; init; }
    public int? LineCount { get; init; }
}

public sealed class HotspotModel
{
    public Guid HotspotId { get; init; } = Guid.NewGuid();
    public Guid? AgentSessionId { get; init; }
    public Guid? WorkspaceRepoId { get; init; }
    public required string SourceType { get; init; }
    public string? SourceRef { get; init; }
    public required string AttributionType { get; init; }
    public required string Confidence { get; init; }
    public required string SuspectedCause { get; init; }
    public required string EvidenceRefsJson { get; init; }
    public decimal? TokenBurnScore { get; init; }
}

public sealed class RecommendationModel
{
    public Guid RecommendationId { get; init; } = Guid.NewGuid();
    public required Guid HotspotId { get; init; }
    public required string RecommendationType { get; init; }
    public required string RuleId { get; init; }
    public required string TriggerCondition { get; init; }
    public required string RecommendedAction { get; init; }
    public required string ExpectedBenefit { get; init; }
    public required string Confidence { get; init; }
    public required string EvidenceRefsJson { get; init; }
}

public interface ITelemetryStore
{
    Task ReplaceImportAsync(TelemetryImportModel import, CancellationToken cancellationToken);
    Task<DashboardSessionsResponse> ListSessionSummariesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkspaceRepoModel>> ListWorkspaceReposAsync(CancellationToken cancellationToken);
    Task ReplaceRepoContextAsync(
        Guid workspaceRepoId,
        IReadOnlyList<ContextSourceModel> contextSources,
        IReadOnlyList<HotspotModel> hotspots,
        IReadOnlyList<RecommendationModel> recommendations,
        CancellationToken cancellationToken);
    Task<DashboardInsightsResponse> ListInsightsAsync(CancellationToken cancellationToken);
}
