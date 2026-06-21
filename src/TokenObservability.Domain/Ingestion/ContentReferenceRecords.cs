using TokenObservability.Domain.Authorization;
using TokenObservability.Domain.Tenancy;

namespace TokenObservability.Domain.Ingestion;

public readonly record struct ContentReferenceId(Guid Value)
{
    public static ContentReferenceId Empty => new(Guid.Empty);

    public override string ToString()
    {
        return Value.ToString("D");
    }
}

public readonly record struct RedactionReviewId(Guid Value)
{
    public static RedactionReviewId Empty => new(Guid.Empty);

    public override string ToString()
    {
        return Value.ToString("D");
    }
}

public sealed record CapturedContentBlobPointer(
    string Container,
    string BlobName,
    string BlobUri,
    string? BlobVersion);

public sealed record ContentReferenceRecord(
    ContentReferenceId ContentReferenceId,
    CustomerOrganizationId CustomerOrganizationId,
    string? AgentSessionId,
    string TelemetryEnvelopeId,
    ContentClass ContentClass,
    ContentReferenceCaptureState CaptureState,
    ContentReferenceRedactionStatus RedactionStatus,
    string? ContentHash,
    CapturedContentBlobPointer? BlobPointer,
    string PolicyVersionId,
    string? RedactionPipelineVersion,
    string? ProductRuleVersion,
    ContentRetentionClass RetentionClass,
    DateTimeOffset? ExpiresAtUtc,
    bool RecommendationEligible,
    string AuditEventId,
    string? ApprovedExcerpt,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record RedactionReviewRecord(
    RedactionReviewId RedactionReviewId,
    CustomerOrganizationId CustomerOrganizationId,
    ContentReferenceId ContentReferenceId,
    ProductUserId ReviewerProductUserId,
    RedactionReviewDecision Decision,
    string? DecisionReason,
    string AuditEventId,
    string CorrelationId,
    DateTimeOffset DecidedAtUtc);

public sealed record CreateContentReferenceRequest(
    CustomerOrganizationId CustomerOrganizationId,
    string? AgentSessionId,
    string TelemetryEnvelopeId,
    ContentClass ContentClass,
    ContentReferenceCaptureState CaptureState,
    ContentReferenceRedactionStatus RedactionStatus,
    string? ContentHash,
    CapturedContentBlobPointer? BlobPointer,
    string PolicyVersionId,
    string? RedactionPipelineVersion,
    string? ProductRuleVersion,
    ContentRetentionClass RetentionClass,
    DateTimeOffset? ExpiresAtUtc,
    bool RecommendationEligible,
    string AuditEventId,
    ProductUserId? ActorProductUserId,
    ProductRole? EffectiveRole,
    string CorrelationId);

public sealed record ReviewContentReferenceRequest(
    CustomerOrganizationId CustomerOrganizationId,
    ContentReferenceId ContentReferenceId,
    ProductUserId ReviewerProductUserId,
    ProductRole EffectiveRole,
    RedactionReviewDecision Decision,
    string? DecisionReason,
    string CorrelationId,
    string AuditEventId,
    string? ApprovedExcerpt = null);

public sealed record ContentRetentionCleanupResult(
    int ExpiredContentReferenceCount,
    IReadOnlyList<ContentReferenceId> ExpiredContentReferenceIds);

public enum ContentReferenceCaptureState
{
    NotAllowed,
    MetadataOnly,
    Captured,
    RedactionFailed,
    ReviewRequired,
    Discarded,
    ApprovedExcerpt
}

public enum ContentReferenceRedactionStatus
{
    NotRequired,
    Passed,
    Failed,
    ReviewRequired,
    ManuallyApproved
}

public enum RedactionReviewDecision
{
    Retry,
    Discard,
    ApproveExcerpt,
    RejectExcerpt,
    MarkRecommendationIneligible
}
