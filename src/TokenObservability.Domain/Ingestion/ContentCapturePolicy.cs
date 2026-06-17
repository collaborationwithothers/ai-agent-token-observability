using TokenObservability.Domain.Authorization;
using TokenObservability.Domain.Tenancy;

namespace TokenObservability.Domain.Ingestion;

public sealed record ContentCapturePolicy(
    CustomerOrganizationId CustomerOrganizationId,
    string PolicyVersionId,
    bool CaptureEnabledByDefault,
    IReadOnlyList<ContentCapturePolicyRule> Rules)
{
    public static ContentCapturePolicy Disabled(
        CustomerOrganizationId customerOrganizationId,
        string policyVersionId)
    {
        return new ContentCapturePolicy(
            customerOrganizationId,
            policyVersionId,
            CaptureEnabledByDefault: false,
            Rules: []);
    }
}

public sealed record ContentCapturePolicyRule(
    bool AllowCapture,
    CodingAgentHarness? Harness = null,
    string? HarnessSetupProfileId = null,
    ProductUserId? ProductUserId = null,
    ProductRole? ProductRole = null,
    string? TeamId = null,
    string? RepositoryId = null,
    ContentClass? ContentClass = null,
    ContentRetentionClass? RetentionClass = null,
    RecommendationUse? RecommendationUse = null);

public sealed record ContentCapturePolicyContext(
    CustomerOrganizationId CustomerOrganizationId,
    CodingAgentHarness Harness,
    string HarnessSetupProfileId,
    bool SetupProfileContentCaptureEnabled,
    ProductUserId ProductUserId,
    ProductRole? ProductRole,
    string? TeamId,
    string? RepositoryId,
    ContentRetentionClass RetentionClass,
    RecommendationUse RecommendationUse);

public sealed record EmittedContentCandidate(
    ContentClass ContentClass,
    string SourceTelemetryReference,
    string RawValue);

public sealed record ContentCandidatePolicyEvaluation(
    ContentCapturePolicyDecision Decision,
    ContentCandidateEvidenceState EvidenceState,
    ContentCandidateMetadata? Metadata);

public sealed record ContentCandidateMetadata(
    string ContentCandidateMetadataId,
    CustomerOrganizationId CustomerOrganizationId,
    string PolicyVersionId,
    ScopedIngestionCredentialId ScopedIngestionCredentialId,
    string HarnessSetupProfileId,
    string SessionId,
    string TelemetryReference,
    ContentClass ContentClass,
    int OriginalLength,
    ContentCapturePolicyDecision PolicyDecision,
    ContentCandidateEvidenceState EvidenceState,
    ContentRedactionStatus RedactionStatus,
    ContentRetentionClass RetentionClass,
    RecommendationUse RecommendationUse,
    DateTimeOffset CreatedAtUtc);

public sealed record CreateContentCandidateMetadataRequest(
    CustomerOrganizationId CustomerOrganizationId,
    string PolicyVersionId,
    ScopedIngestionCredentialId ScopedIngestionCredentialId,
    string HarnessSetupProfileId,
    string SessionId,
    string TelemetryReference,
    ContentClass ContentClass,
    int OriginalLength,
    ContentCapturePolicyDecision PolicyDecision,
    ContentCandidateEvidenceState EvidenceState,
    ContentRedactionStatus RedactionStatus,
    ContentRetentionClass RetentionClass,
    RecommendationUse RecommendationUse);

public sealed record CreateContentCandidateExtractionFailureRequest(
    CustomerOrganizationId CustomerOrganizationId,
    string PolicyVersionId,
    ScopedIngestionCredentialId ScopedIngestionCredentialId,
    string HarnessSetupProfileId,
    string SessionId,
    string TelemetryReference,
    ContentClass ContentClass,
    ContentRetentionClass RetentionClass,
    RecommendationUse RecommendationUse);

public sealed record CreateContentCapturePolicyChangeRequest(
    CustomerOrganizationId CustomerOrganizationId,
    ProductUserId ActorProductUserId,
    ProductRole EffectiveRole,
    string PolicyVersionId,
    string CorrelationId,
    ContentCapturePolicyChangeKind ChangeKind);

public sealed record SetActiveContentCapturePolicyRequest(
    ContentCapturePolicy Policy,
    ProductUserId ActorProductUserId,
    ProductRole EffectiveRole,
    string CorrelationId,
    ContentCapturePolicyChangeKind ChangeKind);

public enum ContentCapturePolicyDecision
{
    MetadataOnly,
    CaptureAllowed,
    PolicyDenied
}

public enum ContentCandidateEvidenceState
{
    Candidate,
    PolicyHidden,
    RedactionRequired,
    RedactionFailed,
    CapturedMetadataOnly
}

public enum ContentRedactionStatus
{
    NotRequired,
    Pending,
    ReviewRequired,
    Failed,
    Passed
}

public enum ContentClass
{
    PromptSnippet,
    ToolInputExcerpt,
    ToolOutputExcerpt,
    ModelResponseExcerpt,
    CommandSummary,
    FileContentExcerpt,
    MetadataOnly
}

public enum ContentRetentionClass
{
    MetadataOnly,
    Short,
    Review,
    Blocked
}

public enum RecommendationUse
{
    Disabled,
    CandidateEvidence,
    ApprovedEvidence
}

public enum ContentCapturePolicyChangeKind
{
    Created,
    Activated,
    Updated,
    Disabled
}

public static class ContentCapturePolicyEvaluator
{
    public static ContentCandidatePolicyEvaluation Evaluate(
        ContentCapturePolicy policy,
        ContentCapturePolicyContext context,
        ScopedIngestionCredentialId scopedIngestionCredentialId,
        string sessionId,
        EmittedContentCandidate? candidate)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(context);

        if (policy.CustomerOrganizationId != context.CustomerOrganizationId)
        {
            throw new ArgumentException("Content capture policy tenant does not match the policy context.", nameof(policy));
        }

        if (candidate is null)
        {
            return new ContentCandidatePolicyEvaluation(
                ContentCapturePolicyDecision.MetadataOnly,
                ContentCandidateEvidenceState.CapturedMetadataOnly,
                Metadata: null);
        }

        var allowed = policy.CaptureEnabledByDefault && context.SetupProfileContentCaptureEnabled;

        foreach (var rule in policy.Rules)
        {
            if (RuleMatches(rule, context, candidate.ContentClass))
            {
                allowed = rule.AllowCapture && context.SetupProfileContentCaptureEnabled;
            }
        }

        var decision = allowed
            ? ContentCapturePolicyDecision.CaptureAllowed
            : ContentCapturePolicyDecision.PolicyDenied;
        var evidenceState = allowed
            ? ContentCandidateEvidenceState.RedactionRequired
            : ContentCandidateEvidenceState.PolicyHidden;
        var redactionStatus = allowed
            ? ContentRedactionStatus.Pending
            : ContentRedactionStatus.NotRequired;

        var metadata = new ContentCandidateMetadata(
            ContentCandidateMetadataId: $"content-candidate-{Guid.NewGuid():N}",
            context.CustomerOrganizationId,
            policy.PolicyVersionId,
            scopedIngestionCredentialId,
            context.HarnessSetupProfileId,
            sessionId,
            candidate.SourceTelemetryReference,
            candidate.ContentClass,
            candidate.RawValue.Length,
            decision,
            evidenceState,
            redactionStatus,
            context.RetentionClass,
            context.RecommendationUse,
            DateTimeOffset.UnixEpoch);

        return new ContentCandidatePolicyEvaluation(decision, evidenceState, metadata);
    }

    private static bool RuleMatches(
        ContentCapturePolicyRule rule,
        ContentCapturePolicyContext context,
        ContentClass contentClass)
    {
        return Matches(rule.Harness, context.Harness) &&
            Matches(rule.HarnessSetupProfileId, context.HarnessSetupProfileId) &&
            Matches(rule.ProductUserId, context.ProductUserId) &&
            Matches(rule.ProductRole, context.ProductRole) &&
            Matches(rule.TeamId, context.TeamId) &&
            Matches(rule.RepositoryId, context.RepositoryId) &&
            Matches(rule.ContentClass, contentClass) &&
            Matches(rule.RetentionClass, context.RetentionClass) &&
            Matches(rule.RecommendationUse, context.RecommendationUse);
    }

    private static bool Matches<T>(T? expected, T? actual)
        where T : struct
    {
        return expected is null ||
            (actual is not null && EqualityComparer<T>.Default.Equals(expected.Value, actual.Value));
    }

    private static bool Matches(string? expected, string? actual)
    {
        return string.IsNullOrWhiteSpace(expected) || StringComparer.Ordinal.Equals(expected, actual);
    }
}
