using TokenObservability.Domain.Tenancy;

namespace TokenObservability.Domain.Ingestion;

public sealed record TokenHotspotRecord(
    TokenHotspotId TokenHotspotId,
    CustomerOrganizationId CustomerOrganizationId,
    string AgentSessionId,
    string Harness,
    string? ModelName,
    TokenHotspotType HotspotType,
    TokenHotspotFindingState FindingState,
    TokenHotspotAttributionType AttributionType,
    TokenHotspotConfidence Confidence,
    TokenMetricStatus MetricStatus,
    TokenMetricConfidence MetricConfidence,
    PromptCacheEvidenceState PromptCacheEvidenceState,
    string EvidenceSummary,
    IReadOnlyList<string> EvidenceReferenceIds,
    string? DetectionKey,
    double? TokenBurnScore,
    decimal? EstimatedCostImpact,
    DateTimeOffset CreatedAtUtc);

public sealed record CreateTokenHotspotRecordRequest(
    CustomerOrganizationId CustomerOrganizationId,
    string AgentSessionId,
    TokenHotspotType HotspotType,
    TokenHotspotFindingState FindingState,
    TokenHotspotAttributionType AttributionType,
    TokenHotspotConfidence Confidence,
    TokenMetricStatus MetricStatus,
    TokenMetricConfidence MetricConfidence,
    PromptCacheEvidenceState PromptCacheEvidenceState,
    string? ModelName,
    string EvidenceSummary,
    IReadOnlyList<string> EvidenceReferenceIds,
    double? TokenBurnScore,
    decimal? EstimatedCostImpact,
    string? DetectionKey = null);

public readonly record struct TokenHotspotId(Guid Value)
{
    public static TokenHotspotId Empty { get; } = new(Guid.Empty);

    public static TokenHotspotId NewId()
    {
        return new TokenHotspotId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}

public enum TokenHotspotType
{
    PromptCacheBreakage,
    LargeContext,
    ToolLoop,
    ModelRetry,
    RepoContextBloat,
    GeneratedArtifactBloat,
    ExpensiveModelChoice,
    ErrorRework,
    Unknown
}

public enum TokenHotspotFindingState
{
    Confirmed,
    CandidateLlmInferred,
    CandidateCorrelated,
    Rejected,
    Superseded
}

public enum TokenHotspotAttributionType
{
    Direct,
    Correlated,
    LlmInferred,
    Unavailable
}

public enum TokenHotspotConfidence
{
    High,
    Medium,
    Low,
    Unavailable
}

public enum PromptCacheEvidenceState
{
    KnownReason,
    InferredCandidate,
    Unknown,
    Unavailable,
    NotApplicable
}
