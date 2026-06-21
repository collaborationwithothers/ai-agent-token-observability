using TokenObservability.Domain.Ingestion;
using TokenObservability.Domain.Tenancy;

namespace TokenObservability.Domain.Recommendations;

public sealed record RecommendationRecord(
    RecommendationId RecommendationId,
    CustomerOrganizationId CustomerOrganizationId,
    string AgentSessionId,
    TokenHotspotId? TokenHotspotId,
    string? RuleId,
    RecommendationKind Kind,
    RecommendationState State,
    RecommendationAuthorityState AuthorityState,
    RecommendationConfidence Confidence,
    RecommendationValidationState ValidationState,
    RecommendationVisibilityScope VisibilityScope,
    string EvidencePacketVersion,
    string EvidencePacketJson,
    string EvidencePacketHash,
    string Summary,
    string Rationale,
    string RecommendedAction,
    string ExpectedBenefit,
    string? ModelPolicyVersionId,
    string? PromptTemplateVersion,
    IReadOnlyList<string> EvidenceReferenceIds,
    IReadOnlyDictionary<string, string> PolicyMetadata,
    string AuditEventId,
    string? GenerationKey,
    DateTimeOffset CreatedAtUtc);

public sealed record CreateRecommendationRecordRequest(
    CustomerOrganizationId CustomerOrganizationId,
    string AgentSessionId,
    TokenHotspotId? TokenHotspotId,
    string? RuleId,
    RecommendationKind Kind,
    RecommendationState State,
    RecommendationAuthorityState AuthorityState,
    RecommendationConfidence Confidence,
    RecommendationValidationState ValidationState,
    RecommendationVisibilityScope VisibilityScope,
    string EvidencePacketVersion,
    string EvidencePacketJson,
    string EvidencePacketHash,
    string Summary,
    string Rationale,
    string RecommendedAction,
    string ExpectedBenefit,
    string? ModelPolicyVersionId,
    string? PromptTemplateVersion,
    IReadOnlyList<string> EvidenceReferenceIds,
    IReadOnlyDictionary<string, string> PolicyMetadata,
    string AuditEventId,
    string CorrelationId,
    string? GenerationKey = null);

public sealed record RecommendationEvidenceRecord(
    RecommendationEvidenceId RecommendationEvidenceId,
    CustomerOrganizationId CustomerOrganizationId,
    RecommendationId RecommendationId,
    RecommendationEvidenceKind EvidenceKind,
    string EvidenceId,
    RecommendationEvidenceState EvidenceState,
    DateTimeOffset CreatedAtUtc);

public sealed record RecommendationRegenerationRequest(
    RecommendationRegenerationRequestId RecommendationRegenerationRequestId,
    CustomerOrganizationId CustomerOrganizationId,
    string? AgentSessionId,
    TokenHotspotId? TokenHotspotId,
    string Reason,
    RecommendationRegenerationState State,
    string AuditEventId,
    string CorrelationId,
    DateTimeOffset CreatedAtUtc);

public sealed record CreateRecommendationRegenerationRequest(
    CustomerOrganizationId CustomerOrganizationId,
    string? AgentSessionId,
    TokenHotspotId? TokenHotspotId,
    string Reason,
    string AuditEventId,
    string CorrelationId);

public sealed record GenerateRecommendationsRequest(
    CustomerOrganizationId CustomerOrganizationId,
    string AgentSessionId,
    string CorrelationId);

public sealed record RecommendationEvidencePacket(
    string SchemaVersion,
    CustomerOrganizationId CustomerOrganizationId,
    string AgentSessionId,
    string Harness,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<string> EvidenceReferenceIds,
    IReadOnlyList<string> HiddenEvidenceReasons,
    string Json,
    string Hash);

public sealed record StructuredRecommendationOutput(
    string SchemaVersion,
    string GenerationType,
    string RecommendationType,
    StructuredCandidateHotspot CandidateHotspot,
    string Summary,
    string RecommendedAction,
    string ExpectedBenefit,
    IReadOnlyList<string> EvidenceReferenceIds,
    IReadOnlyList<string> UnsupportedEvidenceGaps,
    string Confidence,
    string AuthorityState,
    IReadOnlyList<string> SafetyFlags,
    IReadOnlyList<string> PolicyLimitations,
    string UserFacingWording,
    string ReviewerNotes);

public sealed record StructuredCandidateHotspot(
    bool Proposed,
    string? Type,
    string? Label,
    bool PromotionEligible);

public sealed record RecommendationStructuredOutputValidationResult(
    bool IsValid,
    StructuredRecommendationOutput? Output,
    IReadOnlyList<string> Errors);

public sealed record RecommendationStructuredOutputValidationContext(
    string Provider,
    string DeploymentAlias,
    string ModelFamilyOrSku,
    string? ModelVersion,
    string ModelPolicyVersionId,
    string PromptTemplateVersion);

public readonly record struct RecommendationId(Guid Value)
{
    public static RecommendationId NewId()
    {
        return new RecommendationId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}

public readonly record struct RecommendationEvidenceId(Guid Value)
{
    public static RecommendationEvidenceId NewId()
    {
        return new RecommendationEvidenceId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}

public readonly record struct RecommendationRegenerationRequestId(Guid Value)
{
    public static RecommendationRegenerationRequestId NewId()
    {
        return new RecommendationRegenerationRequestId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}

public enum RecommendationKind
{
    Deterministic,
    LlmAssisted,
    Mixed
}

public enum RecommendationState
{
    Candidate,
    Accepted,
    Rejected,
    Expired,
    Superseded
}

public enum RecommendationAuthorityState
{
    Deterministic,
    LlmAssisted,
    LlmInferredCandidate,
    Rejected
}

public enum RecommendationConfidence
{
    Low,
    Medium,
    High
}

public enum RecommendationValidationState
{
    Pending,
    Validated,
    Rejected
}

public enum RecommendationVisibilityScope
{
    Self,
    TeamScoped,
    SecurityReview,
    Admin,
    AggregateOnly
}

public enum RecommendationEvidenceKind
{
    TelemetryEnvelope,
    TokenObservation,
    TokenHotspot,
    ContentReference,
    RepositoryEvidence,
    AuditEvent,
    PricingBasis
}

public enum RecommendationEvidenceState
{
    Observed,
    Derived,
    Correlated,
    LlmInferred,
    Unavailable
}

public enum RecommendationRegenerationState
{
    Queued,
    Completed,
    Failed
}
