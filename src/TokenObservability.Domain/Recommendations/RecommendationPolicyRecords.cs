using TokenObservability.Domain.Authorization;
using TokenObservability.Domain.Ingestion;
using TokenObservability.Domain.Tenancy;

namespace TokenObservability.Domain.Recommendations;

public sealed record RecommendationModelPolicyRecord(
    CustomerOrganizationId CustomerOrganizationId,
    string PolicyVersionId,
    RecommendationModelPolicyState State,
    RecommendationModelProvider Provider,
    string PrimaryDeploymentAlias,
    string? FallbackDeploymentAlias,
    string Region,
    string ModelFamilyOrSku,
    string? ModelVersion,
    string PromptTemplateVersion,
    string StructuredOutputSchemaVersion,
    IReadOnlyList<RecommendationEvidenceClass> AllowedEvidenceClasses,
    RecommendationModelFallbackBehavior FallbackBehavior,
    bool LlmAssistedEnabled,
    string AuditEventId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ActivatedAtUtc);

public sealed record CreateRecommendationModelPolicyRequest(
    CustomerOrganizationId CustomerOrganizationId,
    string PolicyVersionId,
    RecommendationModelPolicyState State,
    RecommendationModelProvider Provider,
    string PrimaryDeploymentAlias,
    string? FallbackDeploymentAlias,
    string Region,
    string ModelFamilyOrSku,
    string? ModelVersion,
    string PromptTemplateVersion,
    string StructuredOutputSchemaVersion,
    IReadOnlyList<RecommendationEvidenceClass> AllowedEvidenceClasses,
    RecommendationModelFallbackBehavior FallbackBehavior,
    bool LlmAssistedEnabled,
    ProductUserId ActorProductUserId,
    ProductRole EffectiveRole,
    string AuditEventId,
    string CorrelationId);

public sealed record RecommendationPromptTemplateRecord(
    CustomerOrganizationId CustomerOrganizationId,
    string PromptTemplateVersion,
    RecommendationPromptTemplatePurpose Purpose,
    RecommendationPromptTemplateState State,
    string PromptTextHash,
    string StructuredOutputSchemaVersion,
    string PolicyConstraintsJson,
    string AuditEventId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ActivatedAtUtc);

public sealed record CreateRecommendationPromptTemplateRequest(
    CustomerOrganizationId CustomerOrganizationId,
    string PromptTemplateVersion,
    RecommendationPromptTemplatePurpose Purpose,
    RecommendationPromptTemplateState State,
    string PromptTextHash,
    string StructuredOutputSchemaVersion,
    string PolicyConstraintsJson,
    ProductUserId ActorProductUserId,
    ProductRole EffectiveRole,
    string AuditEventId,
    string CorrelationId);

public sealed record RecommendationLlmGenerationFailureRecord(
    RecommendationLlmGenerationFailureId RecommendationLlmGenerationFailureId,
    CustomerOrganizationId CustomerOrganizationId,
    string AgentSessionId,
    TokenHotspotId? TokenHotspotId,
    string FailureCode,
    string Provider,
    string DeploymentAlias,
    string PolicyVersionId,
    string PromptTemplateVersion,
    string EvidencePacketHash,
    string StructuredOutputSchemaVersion,
    string AuditEventId,
    DateTimeOffset CreatedAtUtc);

public sealed record CreateRecommendationLlmGenerationFailureRequest(
    CustomerOrganizationId CustomerOrganizationId,
    string AgentSessionId,
    TokenHotspotId? TokenHotspotId,
    string FailureCode,
    string Provider,
    string DeploymentAlias,
    string PolicyVersionId,
    string PromptTemplateVersion,
    string EvidencePacketHash,
    string StructuredOutputSchemaVersion,
    string AuditEventId,
    string CorrelationId);

public readonly record struct RecommendationLlmGenerationFailureId(Guid Value)
{
    public static RecommendationLlmGenerationFailureId NewId()
    {
        return new RecommendationLlmGenerationFailureId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}

public enum RecommendationModelPolicyState
{
    Draft,
    Active,
    Superseded,
    Disabled
}

public enum RecommendationModelProvider
{
    AzureOpenAi
}

public enum RecommendationModelFallbackBehavior
{
    DeterministicOnly,
    FallbackDeploymentThenDeterministic
}

public enum RecommendationEvidenceClass
{
    CustomerOrganization,
    SessionMetadata,
    HarnessMetadata,
    ModelInvocationSummary,
    TokenObservation,
    TokenHotspot,
    CostStatus,
    CacheEvidence,
    ContentReferenceMetadata,
    HiddenEvidenceMarker,
    PolicyMetadata
}

public enum RecommendationPromptTemplatePurpose
{
    RecommendationDrafter,
    CandidateHotspotGenerator,
    CacheBreakageExplainer
}

public enum RecommendationPromptTemplateState
{
    Draft,
    Active,
    Superseded,
    Disabled
}
