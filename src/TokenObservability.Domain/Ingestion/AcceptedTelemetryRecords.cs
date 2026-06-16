using TokenObservability.Domain.Tenancy;

namespace TokenObservability.Domain.Ingestion;

public sealed record CreateAgentSessionRecordRequest(
    CustomerOrganizationId CustomerOrganizationId,
    ProductUserId ProductUserId,
    string HarnessSetupProfileId,
    CodingAgentHarness Harness,
    string? ProviderSessionIdHash,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    AgentSessionStatus SessionStatus,
    RepositoryEvidenceState RepositoryEvidenceState,
    ContentCaptureSummary ContentCaptureSummary,
    RecommendationStatus RecommendationStatus,
    TokenMetricStatus TokenMetricStatus,
    TokenMetricConfidence TokenMetricConfidence);

public sealed record TokenObservationRecord(
    TokenObservationId TokenObservationId,
    CustomerOrganizationId CustomerOrganizationId,
    string AgentSessionId,
    string? ModelInvocationId,
    TokenMetricName MetricName,
    long? Value,
    TokenMetricStatus MetricStatus,
    TokenMetricConfidence MetricConfidence,
    TokenObservationSourceKind SourceKind,
    string? SourceTelemetryEnvelopeId,
    DateTimeOffset CreatedAtUtc);

public readonly record struct TokenObservationId(Guid Value)
{
    public static TokenObservationId Empty { get; } = new(Guid.Empty);

    public static TokenObservationId NewId()
    {
        return new TokenObservationId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}

public sealed record CreateTokenObservationRecordRequest(
    CustomerOrganizationId CustomerOrganizationId,
    string AgentSessionId,
    string? ModelInvocationId,
    TokenMetricName MetricName,
    long? Value,
    TokenMetricStatus MetricStatus,
    TokenMetricConfidence MetricConfidence,
    TokenObservationSourceKind SourceKind,
    string? SourceTelemetryEnvelopeId);

public enum AgentSessionStatus
{
    Active,
    Completed,
    Failed,
    Partial,
    Expired
}

public enum TelemetryEvidenceState
{
    Observed,
    Derived,
    Estimated,
    Unavailable,
    NotApplicable,
    Mixed
}

public enum RepositoryEvidenceState
{
    Observed,
    Correlated,
    Inferred,
    Unavailable,
    Mixed
}

public enum ContentCaptureSummary
{
    None,
    MetadataOnly,
    Captured,
    ReviewRequired,
    RedactionFailed,
    Mixed
}

public enum RecommendationStatus
{
    NotStarted,
    Queued,
    Generated,
    Failed,
    Disabled
}

public enum TokenMetricName
{
    InputTokens,
    OutputTokens,
    CachedInputTokens,
    ReasoningOutputTokens,
    TotalTokens
}

public enum TokenMetricStatus
{
    Observed,
    Derived,
    Estimated,
    Unavailable,
    NotApplicable,
    Mixed
}

public enum TokenMetricConfidence
{
    Observed,
    Deterministic,
    Estimated,
    LlmInferred,
    Unavailable
}

public enum TokenObservationSourceKind
{
    CodexEvent,
    OtlpMetric,
    DerivedSummary,
    Estimator,
    Missing
}
