using TokenObservability.Domain.Authorization;
using TokenObservability.Domain.Ingestion;
using TokenObservability.Domain.Pricing;
using TokenObservability.Domain.Tenancy;
using TokenObservability.Infrastructure.Persistence;

namespace TokenObservability.Runtime.Tests;

public sealed class PricingBasisTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PricingBasisCandidateApprovalKeepsVersionedReviewHistory()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var admin = await CreateProductUserAsync(store, seed, "admin-subject");
        var firstCandidate = await CreateCandidateAsync(store, seed, "audit-price-seed-001", "openai-20260615", 1.25m);
        var secondCandidate = await CreateCandidateAsync(store, seed, "audit-price-seed-002", "openai-20260616", 1.50m);

        var approvedFirst = await store.ApprovePricingBasisAsync(
            new PricingBasisReviewRequest(
                seed.Organization.CustomerOrganizationId,
                firstCandidate.PricingBasisId,
                "audit-price-approve-001",
                "pricing-approve-001",
                "provider_update_reviewed"),
            admin.ProductUserId,
            ProductRole.PlatformAdmin);
        var approvedSecond = await store.ApprovePricingBasisAsync(
            new PricingBasisReviewRequest(
                seed.Organization.CustomerOrganizationId,
                secondCandidate.PricingBasisId,
                "audit-price-approve-002",
                "pricing-approve-002",
                "provider_update_reviewed"),
            admin.ProductUserId,
            ProductRole.PlatformAdmin);

        var records = await store.ListPricingBasisRecordsAsync(seed.Organization.CustomerOrganizationId);
        var supersededFirst = Assert.Single(records, record => record.PricingBasisId == approvedFirst.PricingBasisId);
        Assert.Equal(PricingReviewState.Superseded, supersededFirst.ReviewState);
        Assert.Equal(PricingReviewState.Approved, approvedSecond.ReviewState);
        Assert.Equal("openai-20260616", approvedSecond.PricingVersion);
        Assert.Equal("https://developers.openai.com/api/docs/pricing", approvedSecond.SourceMetadata["source_url"]);

        var auditEvents = await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId);
        Assert.Contains(auditEvents, auditEvent =>
            auditEvent.Action == ProductAuthorizationAction.PricingManage &&
            auditEvent.CorrelationId == "pricing-approve-002" &&
            auditEvent.EvidenceMetadata["review_state"] == "approved");
    }

    [Fact]
    public async Task CustomerPricingOverrideIsTenantScopedAuditedAndDoesNotReplaceProviderMetadata()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var contoso = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var fabrikam = await CreateTenantAsync(store, "fabrikam", "fabrikam-tenant");
        var admin = await CreateProductUserAsync(store, contoso, "admin-subject");

        var record = await store.CreateCustomerPricingOverrideAsync(
            new CreatePricingBasisRecordRequest(
                contoso.Organization.CustomerOrganizationId,
                "codex-cli",
                "openai",
                "gpt-5",
                PricingTokenType.Input,
                "enterprise_contract",
                "USD",
                0.75m,
                "contoso-contract-2026",
                PricingSourceKind.AdminOverride,
                PricingReviewState.Approved,
                Now,
                EffectiveToUtc: null,
                "audit-price-override-001",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["source_url"] = "https://contoso.example/pricing-contract",
                    ["source_retrieved_at_utc"] = Now.ToString("O"),
                    ["provider_document_version"] = "contoso-contract",
                    ["provider_sku_name"] = "gpt-5",
                    ["billing_route"] = "enterprise_contract"
                }),
            admin.ProductUserId,
            ProductRole.PlatformAdmin,
            "pricing-override-001");

        Assert.Equal(PricingSourceKind.AdminOverride, record.SourceKind);
        Assert.Equal("contoso-contract-2026", record.PricingVersion);
        Assert.Equal("https://contoso.example/pricing-contract", record.SourceMetadata["source_url"]);
        Assert.Empty(await store.ListPricingBasisRecordsAsync(fabrikam.Organization.CustomerOrganizationId));

        var auditEvent = Assert.Single(
            await store.ListGovernanceAuditEventsAsync(contoso.Organization.CustomerOrganizationId),
            auditEvent => auditEvent.CorrelationId == "pricing-override-001");
        Assert.Equal(ProductAuthorizationAction.PricingManage, auditEvent.Action);
        Assert.Equal("pricing_override_create", auditEvent.EvidenceMetadata["operation"]);
        Assert.Equal("admin_override", auditEvent.EvidenceMetadata["source_kind"]);
    }

    [Fact]
    public async Task CostEstimatesUseApprovedPricingAndReturnUnavailableForUnmatchedPricing()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var user = await CreateProductUserAsync(store, seed, "developer-subject");
        var approvedInputPrice = await CreateCandidateAsync(store, seed, "audit-price-seed-003", "openai-20260615", 2.00m);
        await store.ApprovePricingBasisAsync(
            new PricingBasisReviewRequest(
                seed.Organization.CustomerOrganizationId,
                approvedInputPrice.PricingBasisId,
                "audit-price-approve-003",
                "pricing-approve-003",
                "provider_update_reviewed"),
            user.ProductUserId,
            ProductRole.PlatformAdmin);
        var session = await CreateSessionAsync(store, seed, user);

        var matched = await store.EstimateAndRecordTokenObservationCostAsync(
            new EstimateTokenObservationCostRequest(
                seed.Organization.CustomerOrganizationId,
                session.AgentSessionId,
                ModelInvocationId: null,
                TokenMetricName.InputTokens,
                TokenCount: 1_000_000,
                TokenMetricStatus.Observed,
                TokenMetricConfidence.Observed,
                "openai",
                "gpt-5",
                "standard",
                Now));
        var unmatched = await store.EstimateAndRecordTokenObservationCostAsync(
            new EstimateTokenObservationCostRequest(
                seed.Organization.CustomerOrganizationId,
                session.AgentSessionId,
                ModelInvocationId: null,
                TokenMetricName.OutputTokens,
                TokenCount: 500_000,
                TokenMetricStatus.Estimated,
                TokenMetricConfidence.Estimated,
                "openai",
                "gpt-5",
                "standard",
                Now));

        Assert.Equal(2.00m, matched.EstimatedCost);
        Assert.Equal(CostEstimateStatus.Estimated, matched.CostStatus);
        Assert.Equal(TokenMetricStatus.Observed, matched.TokenMetricStatus);
        Assert.Null(unmatched.EstimatedCost);
        Assert.Null(unmatched.PricingBasisId);
        Assert.Equal(CostEstimateStatus.Unavailable, unmatched.CostStatus);

        var mix = await store.ListCostMixAsync(seed.Organization.CustomerOrganizationId);
        Assert.Contains(mix, bucket =>
            bucket.TokenType == PricingTokenType.Input &&
            bucket.CostStatus == CostEstimateStatus.Estimated &&
            bucket.EstimatedCost == 2.00m);
        Assert.Contains(mix, bucket =>
            bucket.TokenType == PricingTokenType.Output &&
            bucket.CostStatus == CostEstimateStatus.Unavailable &&
            bucket.EstimatedCost is null);
    }

    private static async Task<PricingBasisRecord> CreateCandidateAsync(
        InMemoryTenantMetadataStore store,
        TenantSeed seed,
        string auditEventId,
        string pricingVersion,
        decimal inputPrice)
    {
        await store.RecordGovernanceAuditEventAsync(
            seed.Organization.CustomerOrganizationId,
            new CreateGovernanceAuditEventRequest(
                auditEventId,
                ActorProductUserId: null,
                EffectiveRole: null,
                ProductAuthorizationAction.PricingManage,
                new ProductScope(ProductScopeKind.Pricing, ScopeId: "pricing"),
                Decision: "created",
                DenialReason: null,
                CorrelationId: $"{auditEventId}-correlation",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["evidence_kind"] = "admin_operation",
                    ["operation"] = "pricing_seed_refresh",
                    ["result"] = "candidate",
                    ["pricing_basis_id"] = "pending",
                    ["pricing_version"] = pricingVersion,
                    ["provider_name"] = "openai",
                    ["model_name"] = "gpt-5",
                    ["token_type"] = "input",
                    ["billing_route"] = "standard",
                    ["source_kind"] = "automated_seed",
                    ["review_state"] = "candidate"
                }));

        return await store.CreatePricingBasisRecordAsync(
            new CreatePricingBasisRecordRequest(
                seed.Organization.CustomerOrganizationId,
                "codex-cli",
                "openai",
                "gpt-5",
                PricingTokenType.Input,
                "standard",
                "USD",
                inputPrice,
                pricingVersion,
                PricingSourceKind.AutomatedSeed,
                PricingReviewState.Candidate,
                Now,
                EffectiveToUtc: null,
                auditEventId,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["source_url"] = "https://developers.openai.com/api/docs/pricing",
                    ["source_retrieved_at_utc"] = Now.ToString("O"),
                    ["provider_document_version"] = "openai-api-pricing",
                    ["provider_sku_name"] = "gpt-5",
                    ["billing_route"] = "standard"
                }));
    }

    private static async Task<AgentSessionRecord> CreateSessionAsync(
        InMemoryTenantMetadataStore store,
        TenantSeed seed,
        ProductUser user)
    {
        return await store.UpsertAgentSessionAsync(
            new CreateAgentSessionRecordRequest(
                seed.Organization.CustomerOrganizationId,
                user.ProductUserId,
                "profile-contoso-codex",
                CodingAgentHarness.CodexCli,
                ProviderSessionIdHash: null,
                StartedAtUtc: Now,
                EndedAtUtc: null,
                AgentSessionStatus.Active,
                RepositoryEvidenceState.Unavailable,
                ContentCaptureSummary.MetadataOnly,
                RecommendationStatus.NotStarted,
                TokenMetricStatus.Observed,
                TokenMetricConfidence.Observed));
    }

    private static async Task<ProductUser> CreateProductUserAsync(
        InMemoryTenantMetadataStore store,
        TenantSeed seed,
        string subject)
    {
        return await store.CreateProductUserAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            new CreateProductUserRequest(subject, subject, Email: null));
    }

    private static async Task<TenantSeed> CreateTenantAsync(
        InMemoryTenantMetadataStore store,
        string slug,
        string externalTenantId)
    {
        var organization = await store.CreateCustomerOrganizationAsync(new CreateCustomerOrganizationRequest(
            slug,
            DisplayName: slug,
            DataResidencyRegion: "eastus2",
            CustomerOrganizationIsolationTier.Shared));
        var identityTenant = await store.CreateIdentityTenantAsync(
            organization.CustomerOrganizationId,
            new CreateIdentityTenantRequest(
                IdentityTenantProvider.MicrosoftEntra,
                $"https://login.microsoftonline.com/{externalTenantId}/v2.0",
                externalTenantId,
                ["api://token-observability"],
                JwksUri: null,
                DisplayName: $"{slug} Entra ID"));

        return new TenantSeed(organization, identityTenant);
    }

    private sealed record TenantSeed(CustomerOrganization Organization, IdentityTenant IdentityTenant);
}
