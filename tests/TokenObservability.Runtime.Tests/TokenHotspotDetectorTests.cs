using System.Security.Cryptography;
using System.Text;
using TokenObservability.Domain.Authorization;
using TokenObservability.Domain.Ingestion;
using TokenObservability.Domain.Tenancy;
using TokenObservability.Infrastructure.Ingestion;
using TokenObservability.Infrastructure.Persistence;

namespace TokenObservability.Runtime.Tests;

public sealed class TokenHotspotDetectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DetectSessionHotspotsRequiresConfiguredTokenThresholds()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var credential = await IssueCredentialAsync(store, seed);
        var session = await SeedTokenSessionAsync(
            store,
            credential.Credential,
            "threshold-required-session",
            [(TokenMetricName.InputTokens, 120_000, TokenMetricStatus.Observed, TokenMetricConfidence.Observed)]);
        var detector = new TokenHotspotDetector(store);

        await Assert.ThrowsAsync<ArgumentException>(() => detector.DetectSessionHotspotsAsync(new DetectTokenHotspotsRequest(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            new TokenHotspotDetectionThresholds(HighInputTokenThreshold: null, HighOutputTokenThreshold: null))));
    }

    [Fact]
    public async Task DetectSessionHotspotsCreatesConfirmedLargeContextFromConfiguredInputThreshold()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var credential = await IssueCredentialAsync(store, seed);
        var session = await SeedTokenSessionAsync(
            store,
            credential.Credential,
            "large-context-session",
            [(TokenMetricName.InputTokens, 120_000, TokenMetricStatus.Observed, TokenMetricConfidence.Observed)]);
        var detector = new TokenHotspotDetector(store);

        var hotspots = await detector.DetectSessionHotspotsAsync(new DetectTokenHotspotsRequest(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            new TokenHotspotDetectionThresholds(HighInputTokenThreshold: 100_000, HighOutputTokenThreshold: null)));

        var hotspot = Assert.Single(hotspots);
        Assert.Equal(TokenHotspotType.LargeContext, hotspot.HotspotType);
        Assert.Equal(TokenHotspotFindingState.Confirmed, hotspot.FindingState);
        Assert.Equal(TokenHotspotAttributionType.Direct, hotspot.AttributionType);
        Assert.Equal(TokenHotspotConfidence.High, hotspot.Confidence);
        Assert.Equal(TokenMetricStatus.Observed, hotspot.MetricStatus);
        Assert.Equal(TokenMetricConfidence.Observed, hotspot.MetricConfidence);
        Assert.Equal(PromptCacheEvidenceState.NotApplicable, hotspot.PromptCacheEvidenceState);
        Assert.Equal("codex-cli", hotspot.Harness);
        Assert.Equal("gpt-5", hotspot.ModelName);
        Assert.Contains("configured threshold", hotspot.EvidenceSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("blame", hotspot.EvidenceSummary, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(hotspot.EvidenceReferenceIds);
    }

    [Fact]
    public async Task DetectSessionHotspotsCreatesConfirmedGeneratedArtifactHotspotFromConfiguredOutputThreshold()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var credential = await IssueCredentialAsync(store, seed);
        var session = await SeedTokenSessionAsync(
            store,
            credential.Credential,
            "large-output-session",
            [(TokenMetricName.OutputTokens, 45_000, TokenMetricStatus.Observed, TokenMetricConfidence.Observed)]);
        var detector = new TokenHotspotDetector(store);

        var hotspots = await detector.DetectSessionHotspotsAsync(new DetectTokenHotspotsRequest(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            new TokenHotspotDetectionThresholds(HighInputTokenThreshold: null, HighOutputTokenThreshold: 40_000)));

        var hotspot = Assert.Single(hotspots);
        Assert.Equal(TokenHotspotType.GeneratedArtifactBloat, hotspot.HotspotType);
        Assert.Equal(TokenHotspotFindingState.Confirmed, hotspot.FindingState);
        Assert.Equal(TokenHotspotAttributionType.Direct, hotspot.AttributionType);
        Assert.Equal(TokenHotspotConfidence.High, hotspot.Confidence);
        Assert.Equal(TokenMetricStatus.Observed, hotspot.MetricStatus);
        Assert.Equal(TokenMetricConfidence.Observed, hotspot.MetricConfidence);
        Assert.Equal(PromptCacheEvidenceState.NotApplicable, hotspot.PromptCacheEvidenceState);
        Assert.Contains("configured threshold", hotspot.EvidenceSummary, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(hotspot.EvidenceReferenceIds);
    }

    [Fact]
    public async Task DetectSessionHotspotsDoesNotConfirmInputThresholdFromLlmInferredMetricConfidence()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var credential = await IssueCredentialAsync(store, seed);
        var session = await SeedTokenSessionAsync(
            store,
            credential.Credential,
            "llm-input-session",
            [(TokenMetricName.InputTokens, 120_000, TokenMetricStatus.Estimated, TokenMetricConfidence.LlmInferred)]);
        var detector = new TokenHotspotDetector(store);

        var hotspots = await detector.DetectSessionHotspotsAsync(new DetectTokenHotspotsRequest(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            new TokenHotspotDetectionThresholds(HighInputTokenThreshold: 100_000, HighOutputTokenThreshold: null)));

        Assert.Empty(hotspots);
    }

    [Fact]
    public async Task DetectSessionHotspotsDoesNotConfirmOutputThresholdFromLlmInferredMetricConfidence()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var credential = await IssueCredentialAsync(store, seed);
        var session = await SeedTokenSessionAsync(
            store,
            credential.Credential,
            "llm-output-session",
            [(TokenMetricName.OutputTokens, 45_000, TokenMetricStatus.Estimated, TokenMetricConfidence.LlmInferred)]);
        var detector = new TokenHotspotDetector(store);

        var hotspots = await detector.DetectSessionHotspotsAsync(new DetectTokenHotspotsRequest(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            new TokenHotspotDetectionThresholds(HighInputTokenThreshold: null, HighOutputTokenThreshold: 40_000)));

        Assert.Empty(hotspots);
    }

    [Fact]
    public async Task DetectSessionHotspotsCreatesUnknownCacheDiagnosticWhenCacheEvidenceIsMissing()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var credential = await IssueCredentialAsync(store, seed);
        var session = await SeedTokenSessionAsync(
            store,
            credential.Credential,
            "cache-missing-session",
            [(TokenMetricName.InputTokens, 120_000, TokenMetricStatus.Observed, TokenMetricConfidence.Observed)]);
        var detector = new TokenHotspotDetector(store);

        var hotspots = await detector.DetectSessionHotspotsAsync(new DetectTokenHotspotsRequest(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            new TokenHotspotDetectionThresholds(HighInputTokenThreshold: 100_000, HighOutputTokenThreshold: null),
            DetectPromptCacheDiagnostics: true));

        var cacheHotspot = Assert.Single(hotspots, hotspot => hotspot.HotspotType == TokenHotspotType.PromptCacheBreakage);
        Assert.Equal(TokenHotspotFindingState.CandidateCorrelated, cacheHotspot.FindingState);
        Assert.Equal(TokenHotspotAttributionType.Correlated, cacheHotspot.AttributionType);
        Assert.Equal(TokenHotspotConfidence.Medium, cacheHotspot.Confidence);
        Assert.Equal(PromptCacheEvidenceState.Unknown, cacheHotspot.PromptCacheEvidenceState);
        Assert.Contains("unknown", cacheHotspot.EvidenceSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("known reason", cacheHotspot.EvidenceSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user error", cacheHotspot.EvidenceSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DetectSessionHotspotsIsIdempotentForRepeatedDetectionOverSameEvidence()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var credential = await IssueCredentialAsync(store, seed);
        var session = await SeedTokenSessionAsync(
            store,
            credential.Credential,
            "idempotent-hotspot-session",
            [
                (TokenMetricName.InputTokens, 120_000, TokenMetricStatus.Observed, TokenMetricConfidence.Observed),
                (TokenMetricName.OutputTokens, 45_000, TokenMetricStatus.Observed, TokenMetricConfidence.Observed)
            ]);
        var detector = new TokenHotspotDetector(store);
        var request = new DetectTokenHotspotsRequest(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            new TokenHotspotDetectionThresholds(HighInputTokenThreshold: 100_000, HighOutputTokenThreshold: 40_000),
            DetectPromptCacheDiagnostics: true);

        var firstRun = await detector.DetectSessionHotspotsAsync(request);
        var secondRun = await detector.DetectSessionHotspotsAsync(request);
        var storedHotspots = await store.ListTokenHotspotsAsync(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId);

        Assert.Equal(3, firstRun.Count);
        Assert.Equal(3, secondRun.Count);
        Assert.Equal(3, storedHotspots.Count);
        Assert.Equal(
            firstRun.Select(hotspot => hotspot.TokenHotspotId).OrderBy(static id => id.ToString()),
            secondRun.Select(hotspot => hotspot.TokenHotspotId).OrderBy(static id => id.ToString()));
        Assert.Equal(
            firstRun.Select(hotspot => hotspot.TokenHotspotId).OrderBy(static id => id.ToString()),
            storedHotspots.Select(hotspot => hotspot.TokenHotspotId).OrderBy(static id => id.ToString()));
    }

    private static async Task<TenantSeed> CreateTenantAsync(InMemoryTenantMetadataStore store)
    {
        var organization = await store.CreateCustomerOrganizationAsync(new CreateCustomerOrganizationRequest(
            Slug: "contoso",
            DisplayName: "contoso organization",
            DataResidencyRegion: "eastus2",
            IsolationTier: CustomerOrganizationIsolationTier.Shared));

        var identityTenant = await store.CreateIdentityTenantAsync(
            organization.CustomerOrganizationId,
            new CreateIdentityTenantRequest(
                Provider: IdentityTenantProvider.MicrosoftEntra,
                Issuer: "https://sts.windows.net/contoso-tenant/",
                ExternalTenantId: "contoso-tenant",
                AllowedAudiences: ["api://token-observability"],
                JwksUri: new Uri("https://login.microsoftonline.com/contoso-tenant/discovery/v2.0/keys"),
                DisplayName: "contoso Entra ID"));

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
                HarnessSetupProfileId: "profile-contoso-codex",
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
