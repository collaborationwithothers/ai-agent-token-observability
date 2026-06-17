using TokenObservability.Domain.Authorization;
using TokenObservability.Domain.Ingestion;
using TokenObservability.Domain.Tenancy;
using TokenObservability.Infrastructure.Persistence;

namespace TokenObservability.Runtime.Tests;

public sealed class ContentCapturePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ContentCapturePolicyIsDisabledByDefaultWithoutAnAllowingRule()
    {
        var organizationId = CustomerOrganizationId.NewId();
        var productUserId = ProductUserId.NewId();
        var policy = ContentCapturePolicy.Disabled(organizationId, "policy-content-v1");
        var context = new ContentCapturePolicyContext(
            organizationId,
            CodingAgentHarness.CodexCli,
            "profile-codex-cli-eastus2",
            SetupProfileContentCaptureEnabled: false,
            productUserId,
            ProductRole.Developer,
            TeamId: "team-platform",
            RepositoryId: "repo-token-observability",
            ContentRetentionClass.Short,
            RecommendationUse.CandidateEvidence);

        var evaluation = ContentCapturePolicyEvaluator.Evaluate(
            policy,
            context,
            ScopedIngestionCredentialId.NewId(),
            "session-001",
            new EmittedContentCandidate(
                ContentClass.ToolOutputExcerpt,
                "telemetry-envelope-001:x-aito-tool-result",
                "raw tool output that must not be stored"));

        Assert.Equal(ContentCapturePolicyDecision.PolicyDenied, evaluation.Decision);
        Assert.Equal(ContentCandidateEvidenceState.PolicyHidden, evaluation.EvidenceState);
        Assert.NotNull(evaluation.Metadata);
        Assert.Equal("policy-content-v1", evaluation.Metadata.PolicyVersionId);
        Assert.Equal(ContentRedactionStatus.NotRequired, evaluation.Metadata.RedactionStatus);
        Assert.Equal("raw tool output that must not be stored".Length, evaluation.Metadata.OriginalLength);
        Assert.DoesNotContain("raw tool output", evaluation.Metadata.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScopedAllowRuleRequiresHarnessEmittedCandidateBeforeRedactionCanRun()
    {
        var organizationId = CustomerOrganizationId.NewId();
        var productUserId = ProductUserId.NewId();
        var policy = new ContentCapturePolicy(
            organizationId,
            "policy-content-v2",
            CaptureEnabledByDefault: false,
            Rules:
            [
                new ContentCapturePolicyRule(
                    AllowCapture: true,
                    Harness: CodingAgentHarness.CodexCli,
                    HarnessSetupProfileId: "profile-codex-cli-eastus2",
                    ProductUserId: productUserId,
                    ProductRole: ProductRole.Developer,
                    TeamId: "team-platform",
                    RepositoryId: "repo-token-observability",
                    ContentClass: ContentClass.PromptSnippet,
                    RetentionClass: ContentRetentionClass.Short,
                    RecommendationUse: RecommendationUse.CandidateEvidence)
            ]);
        var context = new ContentCapturePolicyContext(
            organizationId,
            CodingAgentHarness.CodexCli,
            "profile-codex-cli-eastus2",
            SetupProfileContentCaptureEnabled: true,
            productUserId,
            ProductRole.Developer,
            TeamId: "team-platform",
            RepositoryId: "repo-token-observability",
            ContentRetentionClass.Short,
            RecommendationUse.CandidateEvidence);

        var noCandidate = ContentCapturePolicyEvaluator.Evaluate(
            policy,
            context,
            ScopedIngestionCredentialId.NewId(),
            "session-001",
            candidate: null);

        Assert.Equal(ContentCapturePolicyDecision.MetadataOnly, noCandidate.Decision);
        Assert.Null(noCandidate.Metadata);

        var candidate = ContentCapturePolicyEvaluator.Evaluate(
            policy,
            context,
            ScopedIngestionCredentialId.NewId(),
            "session-001",
            new EmittedContentCandidate(
                ContentClass.PromptSnippet,
                "telemetry-envelope-001:x-aito-raw-prompt",
                "raw prompt that still must not be persisted"));

        Assert.Equal(ContentCapturePolicyDecision.CaptureAllowed, candidate.Decision);
        Assert.Equal(ContentCandidateEvidenceState.RedactionRequired, candidate.EvidenceState);
        Assert.NotNull(candidate.Metadata);
        Assert.Equal(ContentRedactionStatus.Pending, candidate.Metadata.RedactionStatus);
        Assert.DoesNotContain("raw prompt", candidate.Metadata.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ContentCandidateMetadataStoresPolicyHiddenStateWithoutRawContent()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var admin = await CreateProductUserAsync(store, seed, "admin-subject");
        var developer = await CreateProductUserAsync(store, seed, "developer-subject");
        var credential = await CreateCredentialAsync(store, seed, admin, developer);

        var metadata = await store.RecordContentCandidateMetadataAsync(new CreateContentCandidateMetadataRequest(
            seed.Organization.CustomerOrganizationId,
            PolicyVersionId: "policy-content-v1",
            credential.ScopedIngestionCredentialId,
            HarnessSetupProfileId: "profile-codex-cli-eastus2",
            SessionId: "agent-session-001",
            TelemetryReference: "telemetry-envelope-001:x-aito-tool-result",
            ContentClass.ToolOutputExcerpt,
            OriginalLength: "raw tool result containing a secret-looking value".Length,
            ContentCapturePolicyDecision.PolicyDenied,
            ContentCandidateEvidenceState.PolicyHidden,
            ContentRedactionStatus.NotRequired,
            ContentRetentionClass.MetadataOnly,
            RecommendationUse.Disabled));

        var stored = Assert.Single(await store.ListContentCandidateMetadataAsync(seed.Organization.CustomerOrganizationId));
        Assert.Same(metadata, stored);
        Assert.Equal(ContentCandidateEvidenceState.PolicyHidden, stored.EvidenceState);
        Assert.Equal(ContentCapturePolicyDecision.PolicyDenied, stored.PolicyDecision);
        Assert.Equal(ContentRedactionStatus.NotRequired, stored.RedactionStatus);
        Assert.DoesNotContain("raw tool result", stored.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-looking", stored.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ContentCapturePolicyChangesCreateGovernanceAuditEvents()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var admin = await CreateProductUserAsync(store, seed, "admin-subject");

        var auditEvent = await store.RecordContentCapturePolicyChangeAsync(new CreateContentCapturePolicyChangeRequest(
            seed.Organization.CustomerOrganizationId,
            admin.ProductUserId,
            ProductRole.PlatformAdmin,
            PolicyVersionId: "policy-content-v1",
            CorrelationId: "content-policy-change-001",
            ContentCapturePolicyChangeKind.Activated));

        Assert.Equal("content_capture_policy", auditEvent.TargetResourceKind);
        Assert.Equal("policy-content-v1", auditEvent.TargetResourceId);
        Assert.Equal("updated", auditEvent.Decision);
        Assert.Equal("content_capture", auditEvent.EvidenceMetadata["policy_kind"]);
        Assert.Equal("policy-content-v1", auditEvent.EvidenceMetadata["policy_version_id"]);
        Assert.Equal("activated", auditEvent.EvidenceMetadata["change_kind"]);
        Assert.Same(auditEvent, Assert.Single(await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId)));
    }

    [Fact]
    public async Task ActiveContentCapturePolicyChangesAreAudited()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var admin = await CreateProductUserAsync(store, seed, "admin-subject");
        var policy = new ContentCapturePolicy(
            seed.Organization.CustomerOrganizationId,
            "policy-content-v2",
            CaptureEnabledByDefault: false,
            Rules: []);

        var activePolicy = await store.SetActiveContentCapturePolicyAsync(new SetActiveContentCapturePolicyRequest(
            policy,
            admin.ProductUserId,
            ProductRole.PlatformAdmin,
            CorrelationId: "content-policy-activation-001",
            ContentCapturePolicyChangeKind.Activated));

        var storedPolicy = await store.GetActiveContentCapturePolicyAsync(
            seed.Organization.CustomerOrganizationId,
            "profile-codex-cli-eastus2");
        var auditEvent = Assert.Single(await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId));
        Assert.Equal(activePolicy, storedPolicy);
        Assert.Equal("content_capture_policy", auditEvent.TargetResourceKind);
        Assert.Equal("policy-content-v2", auditEvent.TargetResourceId);
        Assert.Equal("activated", auditEvent.EvidenceMetadata["change_kind"]);
    }

    private static Task<ScopedIngestionCredential> CreateCredentialAsync(
        InMemoryTenantMetadataStore store,
        TenantSeed seed,
        ProductUser admin,
        ProductUser developer)
    {
        return store.CreateScopedIngestionCredentialAsync(
            seed.Organization.CustomerOrganizationId,
            new CreateScopedIngestionCredentialRequest(
                HarnessSetupProfileId: "profile-codex-cli-eastus2",
                ProductUserId: developer.ProductUserId,
                CredentialHash: "sha256:credential-verifier-001",
                CredentialPrefix: "aito_live_1234",
                AllowedHarness: CodingAgentHarness.CodexCli,
                AllowedScopes: [new ProductScope(ProductScopeKind.Repository, "repo-001")],
                ExpiresAtUtc: Now.AddDays(30),
                CreatedByProductUserId: admin.ProductUserId,
                ActorEffectiveRole: ProductRole.PlatformAdmin,
                CorrelationId: "credential-create-001",
                AuditEventId: "audit-credential-create-001"));
    }

    private static Task<ProductUser> CreateProductUserAsync(
        InMemoryTenantMetadataStore store,
        TenantSeed seed,
        string externalSubjectId)
    {
        return store.CreateProductUserAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            new CreateProductUserRequest(
                ExternalSubjectId: externalSubjectId,
                DisplayLabel: externalSubjectId,
                Email: $"{externalSubjectId}@example.test"));
    }

    private static async Task<TenantSeed> CreateTenantAsync(InMemoryTenantMetadataStore store)
    {
        var organization = await store.CreateCustomerOrganizationAsync(new CreateCustomerOrganizationRequest(
            Slug: "contoso",
            DisplayName: "Contoso Engineering",
            DataResidencyRegion: "eastus2",
            IsolationTier: CustomerOrganizationIsolationTier.Shared));

        var identityTenant = await store.CreateIdentityTenantAsync(
            organization.CustomerOrganizationId,
            new CreateIdentityTenantRequest(
                Provider: IdentityTenantProvider.MicrosoftEntra,
                Issuer: "https://login.microsoftonline.com/contoso-tenant/v2.0",
                ExternalTenantId: "contoso-tenant",
                AllowedAudiences: ["api://token-observability"],
                JwksUri: null,
                DisplayName: "Contoso Entra ID"));

        return new TenantSeed(organization, identityTenant);
    }

    private sealed record TenantSeed(
        CustomerOrganization Organization,
        IdentityTenant IdentityTenant);
}
