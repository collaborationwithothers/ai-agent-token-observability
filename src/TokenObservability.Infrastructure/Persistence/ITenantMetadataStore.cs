using TokenObservability.Domain.Authorization;
using TokenObservability.Domain.Ingestion;
using TokenObservability.Domain.Pricing;
using TokenObservability.Domain.Recommendations;
using TokenObservability.Domain.Tenancy;

namespace TokenObservability.Infrastructure.Persistence;

public interface ITenantMetadataStore : IProductApiIdempotencyStore
{
    bool CanLoadMissingCustomerOrganizations { get; }

    Task<CustomerOrganization> CreateCustomerOrganizationAsync(CreateCustomerOrganizationRequest request);

    Task<CustomerOrganization> EnsureCustomerOrganizationLoadedAsync(EnsureCustomerOrganizationLoadedRequest request);

    Task<CustomerOrganization?> FindCustomerOrganizationBySlugAsync(string slug);

    Task<CustomerOrganization?> FindCustomerOrganizationAsync(CustomerOrganizationId customerOrganizationId);

    Task<IdentityTenant?> FindIdentityTenantForClaimsAsync(
        CustomerOrganizationId customerOrganizationId,
        AuthenticatedTokenClaims claims);

    Task<GovernanceAuditEvent?> FindGovernanceAuditEventAsync(
        CustomerOrganizationId customerOrganizationId,
        string auditEventId);

    Task<IReadOnlyList<GovernanceAuditEvent>> ListGovernanceAuditEventsAsync(
        CustomerOrganizationId customerOrganizationId);

    Task<GovernanceAuditEvent> RecordGovernanceAuditEventAsync(
        CustomerOrganizationId customerOrganizationId,
        CreateGovernanceAuditEventRequest request);

    Task<ProductAuthorizationDecision> AuthorizeProductActionAsync(
        CustomerOrganizationId customerOrganizationId,
        IdentityTenantId identityTenantId,
        AuthenticatedTokenClaims claims,
        ProductAuthorizationAction action,
        ProductScope requestedScope,
        string? correlationId = null);

    Task RecordAuthorizationDenialAsync(
        CustomerOrganizationId customerOrganizationId,
        ProductAuthorizationAction action,
        ProductScope requestedScope,
        ProductAuthorizationDenialReason denialReason,
        string correlationId);

    Task<IReadOnlyList<IngestionRejectionRecord>> ListIngestionRejectionsAsync(
        CustomerOrganizationId customerOrganizationId);

    Task<IReadOnlyList<AgentSessionRecord>> ListAgentSessionsAsync(
        CustomerOrganizationId customerOrganizationId);

    Task<IReadOnlyList<TokenObservationRecord>> ListTokenObservationsAsync(
        CustomerOrganizationId customerOrganizationId,
        string agentSessionId);

    Task<IReadOnlyList<TokenHotspotRecord>> ListTokenHotspotsAsync(
        CustomerOrganizationId customerOrganizationId,
        string agentSessionId);

    Task<IReadOnlyList<ContentReferenceRecord>> ListContentReferencesForSessionAsync(
        CustomerOrganizationId customerOrganizationId,
        string agentSessionId);

    Task<IReadOnlyList<ContentReferenceRecord>> ListContentReviewItemsAsync(
        CustomerOrganizationId customerOrganizationId,
        ContentReferenceCaptureState? state = null);

    Task<ContentReferenceRecord?> FindContentReferenceAsync(
        CustomerOrganizationId customerOrganizationId,
        ContentReferenceId contentReferenceId);

    Task<RedactionReviewRecord> ReviewContentReferenceAsync(ReviewContentReferenceRequest request);

    Task<PricingBasisRecord> CreatePricingBasisRecordAsync(CreatePricingBasisRecordRequest request);

    Task<PricingBasisRecord> CreatePricingSeedCandidateRecordAsync(
        CreatePricingBasisRecordRequest request,
        string correlationId);

    Task<PricingBasisRecord> CreateCustomerPricingOverrideAsync(
        CreatePricingBasisRecordRequest request,
        ProductUserId actorProductUserId,
        ProductRole actorEffectiveRole,
        string correlationId);

    Task<IReadOnlyList<PricingBasisRecord>> ListPricingBasisRecordsAsync(
        CustomerOrganizationId customerOrganizationId);

    Task<PricingBasisRecord> ApprovePricingBasisAsync(
        PricingBasisReviewRequest request,
        ProductUserId actorProductUserId,
        ProductRole actorEffectiveRole);

    Task<PricingBasisRecord> RejectPricingBasisAsync(
        PricingBasisReviewRequest request,
        ProductUserId actorProductUserId,
        ProductRole actorEffectiveRole);

    Task<PricingBasisRecord> SupersedePricingBasisAsync(
        PricingBasisReviewRequest request,
        ProductUserId actorProductUserId,
        ProductRole actorEffectiveRole);

    Task<CostEstimateRecord> RecordCostEstimateAsync(CreateCostEstimateRecordRequest request);

    Task<IReadOnlyList<CostEstimateRecord>> ListCostEstimatesAsync(
        CustomerOrganizationId customerOrganizationId);

    Task<IReadOnlyList<CostMixBucket>> ListCostMixAsync(
        CustomerOrganizationId customerOrganizationId);

    Task<CostEstimateRecord> EstimateAndRecordTokenObservationCostAsync(
        EstimateTokenObservationCostRequest request);

    Task<IReadOnlyList<AggregateMetricPointRecord>> ListAggregateMetricPointsAsync(
        CustomerOrganizationId customerOrganizationId);

    Task<BudgetPolicyRecord> CreateBudgetPolicyAsync(
        CreateBudgetPolicyRequest request,
        ProductUserId actorProductUserId,
        ProductRole actorEffectiveRole);

    Task<IReadOnlyList<BudgetPolicyRecord>> ListBudgetPoliciesAsync(
        CustomerOrganizationId customerOrganizationId);

    Task<BudgetPolicyRecord> UpdateBudgetPolicyAsync(
        UpdateBudgetPolicyRequest request,
        ProductUserId actorProductUserId,
        ProductRole actorEffectiveRole);

    Task<IReadOnlyList<RecommendationRecord>> ListRecommendationsForSessionAsync(
        CustomerOrganizationId customerOrganizationId,
        string agentSessionId);

    Task<RecommendationRecord?> FindRecommendationAsync(
        CustomerOrganizationId customerOrganizationId,
        RecommendationId recommendationId);

    Task<RecommendationRegenerationRequest> CreateRecommendationRegenerationRequestAsync(
        CreateRecommendationRegenerationRequest request,
        ProductUserId actorProductUserId,
        ProductRole actorEffectiveRole);
}
