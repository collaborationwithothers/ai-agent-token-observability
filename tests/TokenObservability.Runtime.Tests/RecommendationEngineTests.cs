using System.Security.Cryptography;
using System.Text;
using TokenObservability.Domain.Authorization;
using TokenObservability.Domain.Ingestion;
using TokenObservability.Domain.Recommendations;
using TokenObservability.Domain.Tenancy;
using TokenObservability.Infrastructure.Ingestion;
using TokenObservability.Infrastructure.Persistence;
using TokenObservability.Infrastructure.Recommendations;

namespace TokenObservability.Runtime.Tests;

public sealed class RecommendationEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DeterministicGeneratorCreatesAuditBackedRecommendationsFromSupportedHotspots()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var credential = await IssueCredentialAsync(store, seed);
        var session = await SeedTokenSessionAsync(
            store,
            credential.Credential,
            "recommendation-session",
            [
                (TokenMetricName.InputTokens, 120_000, TokenMetricStatus.Observed, TokenMetricConfidence.Observed),
                (TokenMetricName.CachedInputTokens, null, TokenMetricStatus.Unavailable, TokenMetricConfidence.Unavailable)
            ]);
        await store.RecordTokenHotspotAsync(new CreateTokenHotspotRecordRequest(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            TokenHotspotType.LargeContext,
            TokenHotspotFindingState.Confirmed,
            TokenHotspotAttributionType.Direct,
            TokenHotspotConfidence.High,
            TokenMetricStatus.Observed,
            TokenMetricConfidence.Observed,
            PromptCacheEvidenceState.NotApplicable,
            ModelName: "gpt-5",
            EvidenceSummary: "Input tokens exceeded configured threshold using accepted token evidence.",
            EvidenceReferenceIds: [session.AgentSessionId],
            TokenBurnScore: 0.91,
            EstimatedCostImpact: null,
            DetectionKey: "large-context:test"));
        await store.RecordTokenHotspotAsync(new CreateTokenHotspotRecordRequest(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            TokenHotspotType.PromptCacheBreakage,
            TokenHotspotFindingState.CandidateCorrelated,
            TokenHotspotAttributionType.Correlated,
            TokenHotspotConfidence.Medium,
            TokenMetricStatus.Observed,
            TokenMetricConfidence.Observed,
            PromptCacheEvidenceState.Unavailable,
            ModelName: "gpt-5",
            EvidenceSummary: "Cache cause is unknown because cache evidence was unavailable.",
            EvidenceReferenceIds: [session.AgentSessionId],
            TokenBurnScore: null,
            EstimatedCostImpact: null,
            DetectionKey: "cache-unavailable:test"));
        var generator = new DeterministicRecommendationGenerator(store);

        var generated = await generator.GenerateForSessionAsync(new GenerateRecommendationsRequest(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            CorrelationId: "recommendation-generation-test"));

        Assert.Equal(2, generated.Count);
        Assert.Contains(generated, recommendation => recommendation.RuleId == "rec.high_input_tokens.narrow_context");
        Assert.Contains(generated, recommendation => recommendation.RuleId == "rec.cache_unavailable.instrumentation_gap");
        Assert.All(generated, recommendation =>
        {
            Assert.Equal(RecommendationKind.Deterministic, recommendation.Kind);
            Assert.Equal(RecommendationState.Accepted, recommendation.State);
            Assert.Equal(RecommendationAuthorityState.Deterministic, recommendation.AuthorityState);
            Assert.Equal(RecommendationValidationState.Validated, recommendation.ValidationState);
            Assert.Equal("recommendation.evidence.v1", recommendation.EvidencePacketVersion);
            Assert.Matches("^[A-F0-9]{64}$", recommendation.EvidencePacketHash);
            Assert.NotEmpty(recommendation.EvidenceReferenceIds);
            Assert.NotEmpty(recommendation.ExpectedBenefit);
            Assert.NotEmpty(recommendation.PolicyMetadata);
            Assert.DoesNotContain("blame", recommendation.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("developer rank", recommendation.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("raw_prompt", recommendation.EvidencePacketJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("command_output", recommendation.EvidencePacketJson, StringComparison.OrdinalIgnoreCase);
        });
        var auditEvents = await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId);
        Assert.Equal(2, auditEvents.Count(audit => audit.EvidenceMetadata["operation"] == "recommendation_generation"));
    }

    [Fact]
    public async Task DeterministicGeneratorIsIdempotentForSameRuleAndHotspotEvidence()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var credential = await IssueCredentialAsync(store, seed);
        var session = await SeedTokenSessionAsync(
            store,
            credential.Credential,
            "idempotent-recommendation-session",
            [(TokenMetricName.OutputTokens, 45_000, TokenMetricStatus.Observed, TokenMetricConfidence.Observed)]);
        await store.RecordTokenHotspotAsync(new CreateTokenHotspotRecordRequest(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            TokenHotspotType.GeneratedArtifactBloat,
            TokenHotspotFindingState.Confirmed,
            TokenHotspotAttributionType.Direct,
            TokenHotspotConfidence.High,
            TokenMetricStatus.Observed,
            TokenMetricConfidence.Observed,
            PromptCacheEvidenceState.NotApplicable,
            ModelName: "gpt-5",
            EvidenceSummary: "Output tokens exceeded configured threshold using accepted token evidence.",
            EvidenceReferenceIds: [session.AgentSessionId],
            TokenBurnScore: null,
            EstimatedCostImpact: null,
            DetectionKey: "generated-artifact:test"));
        var generator = new DeterministicRecommendationGenerator(store);
        var request = new GenerateRecommendationsRequest(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            CorrelationId: "idempotent-recommendation-test");

        var firstRun = await generator.GenerateForSessionAsync(request);
        var secondRun = await generator.GenerateForSessionAsync(request);
        var stored = await store.ListRecommendationsForSessionAsync(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId);

        var recommendation = Assert.Single(firstRun);
        Assert.Equal(recommendation.RecommendationId, Assert.Single(secondRun).RecommendationId);
        Assert.Equal(recommendation.RecommendationId, Assert.Single(stored).RecommendationId);
    }

    [Fact]
    public async Task RecommendationStoreRejectsCrossTenantEvidenceAndUnsafeLanguage()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var contoso = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var fabrikam = await CreateTenantAsync(store, "fabrikam", "fabrikam-tenant");
        var contosoCredential = await IssueCredentialAsync(store, contoso);
        var fabrikamCredential = await IssueCredentialAsync(store, fabrikam);
        var contosoSession = await SeedTokenSessionAsync(
            store,
            contosoCredential.Credential,
            "contoso-session",
            [(TokenMetricName.InputTokens, 120_000, TokenMetricStatus.Observed, TokenMetricConfidence.Observed)]);
        var fabrikamSession = await SeedTokenSessionAsync(
            store,
            fabrikamCredential.Credential,
            "fabrikam-session",
            [(TokenMetricName.InputTokens, 120_000, TokenMetricStatus.Observed, TokenMetricConfidence.Observed)]);
        var fabrikamHotspot = await store.RecordTokenHotspotAsync(new CreateTokenHotspotRecordRequest(
            fabrikam.Organization.CustomerOrganizationId,
            fabrikamSession.AgentSessionId,
            TokenHotspotType.LargeContext,
            TokenHotspotFindingState.Confirmed,
            TokenHotspotAttributionType.Direct,
            TokenHotspotConfidence.High,
            TokenMetricStatus.Observed,
            TokenMetricConfidence.Observed,
            PromptCacheEvidenceState.NotApplicable,
            ModelName: "gpt-5",
            EvidenceSummary: "Input tokens exceeded configured threshold using accepted token evidence.",
            EvidenceReferenceIds: [fabrikamSession.AgentSessionId],
            TokenBurnScore: null,
            EstimatedCostImpact: null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateRecommendationAsync(new CreateRecommendationRecordRequest(
            contoso.Organization.CustomerOrganizationId,
            AgentSessionId: contosoSession.AgentSessionId,
            TokenHotspotId: fabrikamHotspot.TokenHotspotId,
            RuleId: "rec.high_input_tokens.narrow_context",
            RecommendationKind.Deterministic,
            RecommendationState.Accepted,
            RecommendationAuthorityState.Deterministic,
            RecommendationConfidence.High,
            RecommendationValidationState.Validated,
            RecommendationVisibilityScope.TeamScoped,
            EvidencePacketVersion: "recommendation.evidence.v1",
            EvidencePacketJson: "{}",
            EvidencePacketHash: new string('A', 64),
            Summary: "Reduce unnecessary context.",
            Rationale: "Evidence supports context reduction.",
            RecommendedAction: "Use targeted files.",
            ExpectedBenefit: "Lower input token use.",
            ModelPolicyVersionId: null,
            PromptTemplateVersion: null,
            EvidenceReferenceIds: [fabrikamHotspot.TokenHotspotId.ToString()],
            PolicyMetadata: new Dictionary<string, string> { ["content_capture_policy_version"] = "metadata_only" },
            AuditEventId: "audit-cross-tenant-recommendation",
            CorrelationId: "cross-tenant-recommendation")));

        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateRecommendationAsync(new CreateRecommendationRecordRequest(
            contoso.Organization.CustomerOrganizationId,
            AgentSessionId: contosoSession.AgentSessionId,
            TokenHotspotId: null,
            RuleId: "rec.high_input_tokens.narrow_context",
            RecommendationKind.Deterministic,
            RecommendationState.Accepted,
            RecommendationAuthorityState.Deterministic,
            RecommendationConfidence.Medium,
            RecommendationValidationState.Validated,
            RecommendationVisibilityScope.TeamScoped,
            EvidencePacketVersion: "recommendation.evidence.v1",
            EvidencePacketJson: "{}",
            EvidencePacketHash: new string('B', 64),
            Summary: "Developer ranking shows this person caused waste.",
            Rationale: "The user made an obvious error.",
            RecommendedAction: "Blame the developer.",
            ExpectedBenefit: "N/A",
            ModelPolicyVersionId: null,
            PromptTemplateVersion: null,
            EvidenceReferenceIds: [contosoSession.AgentSessionId],
            PolicyMetadata: new Dictionary<string, string> { ["content_capture_policy_version"] = "metadata_only" },
            AuditEventId: "audit-unsafe-recommendation",
            CorrelationId: "unsafe-recommendation")));
    }

    [Fact]
    public async Task RecommendationRegenerationRejectsUnsafeReasonWithoutPersistingRequestOrAudit()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var credential = await IssueCredentialAsync(store, seed);
        var session = await SeedTokenSessionAsync(
            store,
            credential.Credential,
            "unsafe-regeneration-reason-session",
            [(TokenMetricName.InputTokens, 120_000, TokenMetricStatus.Observed, TokenMetricConfidence.Observed)]);

        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateRecommendationRegenerationRequestAsync(
            new CreateRecommendationRegenerationRequest(
                seed.Organization.CustomerOrganizationId,
                session.AgentSessionId,
                TokenHotspotId: null,
                Reason: "policy changed after raw prompt review",
                AuditEventId: "audit-regeneration-unsafe-reason",
                CorrelationId: "regeneration-unsafe-reason"),
            credential.Credential.ProductUserId,
            ProductRole.Developer));

        Assert.Empty(await store.ListRecommendationRegenerationRequestsAsync(seed.Organization.CustomerOrganizationId));
        var auditEvents = await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId);
        Assert.DoesNotContain(auditEvents, audit => audit.AuditEventId == "audit-regeneration-unsafe-reason");
    }

    [Fact]
    public void StructuredOutputValidatorRejectsUnsupportedLlmOutput()
    {
        var packet = new RecommendationEvidencePacket(
            "recommendation.evidence.v1",
            CustomerOrganizationId.NewId(),
            "session-1",
            "codex-cli",
            DateTimeOffset.UtcNow,
            EvidenceReferenceIds: ["hotspot-1"],
            HiddenEvidenceReasons: ["policy_hidden"],
            Json: "{\"hiddenEvidence\":[{\"reason\":\"policy_hidden\"}]}",
            Hash: new string('C', 64));

        var valid = RecommendationStructuredOutputValidator.Validate(
            new StructuredRecommendationOutput(
                "recommendation.llm_output.v1",
                "recommendation_explanation",
                "reduce_context",
                new StructuredCandidateHotspot(Proposed: false, Type: null, Label: null, PromotionEligible: false),
                "Large context appears to be increasing input token use.",
                "Use targeted files and summaries.",
                "Lower input tokens.",
                EvidenceReferenceIds: ["hotspot-1"],
                UnsupportedEvidenceGaps: ["cache evidence unavailable"],
                "medium",
                "llm_assisted",
                SafetyFlags: [],
                PolicyLimitations: ["policy_hidden"],
                "This is workflow coaching, not a person failure.",
                "No raw content was used."),
            packet);

        Assert.True(valid.IsValid);
        Assert.False(RecommendationStructuredOutputValidator.Validate(
            valid.Output! with { EvidenceReferenceIds = ["missing-ref"] },
            packet).IsValid);
        Assert.False(RecommendationStructuredOutputValidator.Validate(
            valid.Output! with { EvidenceReferenceIds = [] },
            packet).IsValid);
        Assert.False(RecommendationStructuredOutputValidator.Validate(
            valid.Output! with { GenerationType = "" },
            packet).IsValid);
        Assert.False(RecommendationStructuredOutputValidator.Validate(
            valid.Output! with { CandidateHotspot = null! },
            packet).IsValid);
        Assert.False(RecommendationStructuredOutputValidator.Validate(
            valid.Output! with { SafetyFlags = null! },
            packet).IsValid);
        Assert.False(RecommendationStructuredOutputValidator.Validate(
            valid.Output! with { UserFacingWording = "Rank the developer for this obvious user error." },
            packet).IsValid);
        Assert.False(RecommendationStructuredOutputValidator.Validate(
            valid.Output! with { PolicyLimitations = [] },
            packet).IsValid);
        Assert.False(RecommendationStructuredOutputValidator.Validate(
            valid.Output! with { RecommendedAction = "Inspect raw_prompt and command_output." },
            packet).IsValid);
        Assert.False(RecommendationStructuredOutputValidator.Validate(
            valid.Output! with
            {
                CandidateHotspot = new StructuredCandidateHotspot(
                    Proposed: true,
                    Type: "prompt_cache_breakage",
                    Label: "confirmed cache breakage",
                    PromotionEligible: true)
            },
            packet).IsValid);
    }

    private static async Task<TenantSeed> CreateTenantAsync(
        InMemoryTenantMetadataStore store,
        string slug = "contoso",
        string externalTenantId = "contoso-tenant")
    {
        var organization = await store.CreateCustomerOrganizationAsync(new CreateCustomerOrganizationRequest(
            Slug: slug,
            DisplayName: $"{slug} organization",
            DataResidencyRegion: "eastus2",
            IsolationTier: CustomerOrganizationIsolationTier.Shared));

        var identityTenant = await store.CreateIdentityTenantAsync(
            organization.CustomerOrganizationId,
            new CreateIdentityTenantRequest(
                Provider: IdentityTenantProvider.MicrosoftEntra,
                Issuer: $"https://sts.windows.net/{externalTenantId}/",
                ExternalTenantId: externalTenantId,
                AllowedAudiences: ["api://token-observability"],
                JwksUri: new Uri($"https://login.microsoftonline.com/{externalTenantId}/discovery/v2.0/keys"),
                DisplayName: $"{slug} Entra ID"));

        return new TenantSeed(organization, identityTenant);
    }

    private static async Task<IssuedScopedIngestionCredential> IssueCredentialAsync(
        InMemoryTenantMetadataStore store,
        TenantSeed seed)
    {
        var lifecycle = new ScopedIngestionCredentialLifecycleService(store, new StaticTenantMetadataClock(Now));
        var admin = await store.CreateProductUserAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            new CreateProductUserRequest(
                ExternalSubjectId: $"admin-{Guid.NewGuid():N}",
                DisplayLabel: "admin",
                Email: "admin@example.test"));
        var developer = await store.CreateProductUserAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            new CreateProductUserRequest(
                ExternalSubjectId: $"developer-{Guid.NewGuid():N}",
                DisplayLabel: "developer",
                Email: "developer@example.test"));

        return await lifecycle.CreateAsync(
            seed.Organization.CustomerOrganizationId,
            new IssueScopedIngestionCredentialRequest(
                HarnessSetupProfileId: $"profile-{seed.Organization.Slug}-codex",
                ProductUserId: developer.ProductUserId,
                AllowedHarness: CodingAgentHarness.CodexCli,
                AllowedScopes: [new ProductScope(ProductScopeKind.Organization, ScopeId: null)],
                ExpiresAtUtc: Now.AddDays(30),
                CreatedByProductUserId: admin.ProductUserId,
                ActorEffectiveRole: ProductRole.PlatformAdmin,
                CorrelationId: $"credential-create-{Guid.NewGuid():N}",
                AuditEventId: $"audit-credential-create-{Guid.NewGuid():N}"));
    }

    private static async Task<AgentSessionRecord> SeedTokenSessionAsync(
        InMemoryTenantMetadataStore store,
        ScopedIngestionCredential credential,
        string providerSessionId,
        IReadOnlyList<(TokenMetricName MetricName, long? Value, TokenMetricStatus Status, TokenMetricConfidence Confidence)> observations)
    {
        var providerSessionIdHash = ComputeSha256Hex(providerSessionId);
        var envelope = await store.RecordTelemetryEnvelopeAsync(new CreateTelemetryEnvelopeRecordRequest(
            CustomerOrganizationId: credential.CustomerOrganizationId,
            HarnessSetupProfileId: credential.HarnessSetupProfileId,
            ScopedIngestionCredentialId: credential.ScopedIngestionCredentialId,
            ProductUserId: credential.ProductUserId,
            Harness: "codex-cli",
            SchemaVersion: "2026-06-01",
            SignalType: "metric",
            SourceEventName: "codex.api_request",
            SourceEventTimestampUtc: Now,
            ConversationIdHash: providerSessionIdHash,
            TurnIdHash: ComputeSha256Hex($"{providerSessionId}-turn"),
            SourceEventId: null,
            TraceIdHash: null,
            SpanIdHash: null,
            ModelName: "gpt-5",
            HarnessVersion: null,
            SandboxSetting: null,
            ApprovalSetting: null,
            RepositoryEvidenceState: "unavailable",
            ContentPolicyDecision: "metadata_only",
            ContentCaptureState: "metadata_only",
            RedactionState: "not_required",
            RoutingDecision: new Dictionary<string, string>
            {
                ["result"] = "accepted",
                ["metadata_store"] = "postgresql",
                ["diagnostic_store"] = "not_applicable",
                ["metrics_store"] = "azure_monitor_workspace",
                ["content_capture"] = "metadata_only"
            },
            EvidenceState: "observed",
            MetricState: observations.Any(observation => observation.Value is null) ? "unavailable" : "observed",
            MetricStatus: observations.Any(observation => observation.Status != TokenMetricStatus.Observed)
                ? TokenMetricStatus.Mixed
                : TokenMetricStatus.Observed,
            MetricConfidence: observations.Any(observation => observation.Confidence != TokenMetricConfidence.Observed)
                ? TokenMetricConfidence.Estimated
                : TokenMetricConfidence.Observed,
            SourceEvidenceKind: "harness_emitted",
            CorrelationId: $"correlation-{providerSessionId}",
            DedupeKeyHash: ComputeSha256Hex($"dedupe-{providerSessionId}"),
            IngestionVersionMetadata: new Dictionary<string, string>
            {
                ["schema_version"] = "2026-06-01",
                ["harness_version"] = "unavailable",
                ["contract_version"] = "2026-06-01"
            }));
        var session = Assert.Single(await store.ListAgentSessionsAsync(credential.CustomerOrganizationId));

        foreach (var observation in observations)
        {
            await store.RecordTokenObservationAsync(new CreateTokenObservationRecordRequest(
                credential.CustomerOrganizationId,
                session.AgentSessionId,
                ModelInvocationId: null,
                observation.MetricName,
                observation.Value,
                observation.Status,
                observation.Confidence,
                observation.Value.HasValue ? TokenObservationSourceKind.CodexEvent : TokenObservationSourceKind.Missing,
                envelope.TelemetryEnvelopeId));
        }

        return session;
    }

    private static string ComputeSha256Hex(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private sealed record TenantSeed(CustomerOrganization Organization, IdentityTenant IdentityTenant);
}
