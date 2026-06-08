namespace AiAgentTokenObservability.Contracts.Insights;

public sealed record DashboardInsightsResponse(
    IReadOnlyList<ContextSourceResponse> ContextSources,
    IReadOnlyList<HotspotResponse> Hotspots,
    DateTimeOffset GeneratedAtUtc);

public sealed record ContextSourceResponse(
    Guid ContextSourceId,
    Guid WorkspaceRepoId,
    string SourceType,
    string PathHash,
    string? DisplayPath,
    string FileCategory,
    string? SpecArtifactStatus,
    bool EligibleForInferredHotspot,
    long? SizeBytes,
    int? LineCount);

public sealed record HotspotResponse(
    Guid HotspotId,
    Guid? AgentSessionId,
    Guid? WorkspaceRepoId,
    string SourceType,
    string? SourceRef,
    string AttributionType,
    string Confidence,
    string SuspectedCause,
    string EvidenceRefsJson,
    decimal? TokenBurnScore,
    IReadOnlyList<RecommendationResponse> Recommendations);

public sealed record RecommendationResponse(
    Guid RecommendationId,
    Guid HotspotId,
    string RecommendationType,
    string RuleId,
    string TriggerCondition,
    string RecommendedAction,
    string ExpectedBenefit,
    string Confidence,
    string EvidenceRefsJson);
