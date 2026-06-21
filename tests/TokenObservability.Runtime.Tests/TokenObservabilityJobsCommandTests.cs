using System.Net;
using System.Security.Cryptography;
using System.Text;
using TokenObservability.Domain.Authorization;
using TokenObservability.Domain.Ingestion;
using TokenObservability.Jobs;
using TokenObservability.Domain.Pricing;
using TokenObservability.Domain.Tenancy;
using TokenObservability.Infrastructure.Ingestion;
using TokenObservability.Infrastructure.Persistence;

namespace TokenObservability.Runtime.Tests;

public sealed class TokenObservabilityJobsCommandTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] ExpectedCommands =
    [
        "normalize-telemetry",
        "detect-hotspots",
        "generate-recommendations",
        "redact-content",
        "refresh-pricing",
        "retention-cleanup",
        "reprocess-session",
        "tenant-maintenance"
    ];

    [Fact]
    public void TokenObservabilityJobsExposeDocumentedCommandCatalog()
    {
        var actualCommands = JobCommandCatalog.Commands.Select(command => command.Name).ToArray();

        Assert.Equal(ExpectedCommands, actualCommands);
    }

    [Fact]
    public async Task TokenObservabilityJobsListCommandsEntryPointReturnsSuccess()
    {
        using var writer = new StringWriter();

        var exitCode = await TokenObservabilityJobsCommandLine.RunAsync(["--list-commands"], writer);

        Assert.Equal(0, exitCode);
        foreach (var expectedCommand in ExpectedCommands)
        {
            Assert.Contains(expectedCommand, writer.ToString());
        }
    }

    [Fact]
    public void OpenAiPricingParserCreatesTokenTypeCandidatesFromOfficialPricingShape()
    {
        const string pricingPage = """
            Model Input Cached input Output
            gpt-5 $1.25 $0.13 $10.00
            gpt-5-mini $0.25 $0.025 $2.00
            """;

        var candidates = OpenAiPricingPageParser.Parse(
            pricingPage,
            new Uri("https://developers.openai.com/api/docs/pricing"),
            new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero));

        Assert.Contains(candidates, candidate =>
            candidate.ProviderName == "openai" &&
            candidate.ModelName == "gpt-5" &&
            candidate.TokenType == PricingTokenType.Input &&
            candidate.PricePerMillionTokens == 1.25m);
        Assert.Contains(candidates, candidate =>
            candidate.ModelName == "gpt-5" &&
            candidate.TokenType == PricingTokenType.CachedInput &&
            candidate.PricePerMillionTokens == 0.13m);
        Assert.Contains(candidates, candidate =>
            candidate.ModelName == "gpt-5-mini" &&
            candidate.TokenType == PricingTokenType.Output &&
            candidate.PricePerMillionTokens == 2.00m);
        Assert.All(candidates, candidate =>
        {
            Assert.Equal("codex-cli", candidate.Harness);
            Assert.Equal("standard", candidate.BillingRoute);
            Assert.Equal("https://developers.openai.com/api/docs/pricing", candidate.SourceMetadata["source_url"]);
        });
    }

    [Fact]
    public void AzureRetailPricingParserCreatesConsumptionCandidatesWithoutInferringMissingRates()
    {
        const string json = """
            {
              "Items": [
                {
                  "productName": "Azure OpenAI",
                  "skuName": "gpt 4o 1120 Inp global",
                  "meterName": "Input Tokens",
                  "unitOfMeasure": "1K Tokens",
                  "retailPrice": 0.0025,
                  "currencyCode": "USD",
                  "armRegionName": "eastus2",
                  "meterId": "meter-input"
                },
                {
                  "productName": "Azure OpenAI",
                  "skuName": "gpt 4o 1120 Out Data Zone",
                  "meterName": "Output Tokens",
                  "unitOfMeasure": "1K Tokens",
                  "retailPrice": 0.0100,
                  "currencyCode": "USD",
                  "armRegionName": "eastus2",
                  "meterId": "meter-output"
                },
                {
                  "productName": "Azure OpenAI",
                  "skuName": "gpt 4o 1120 Requests",
                  "meterName": "Requests",
                  "unitOfMeasure": "1K Requests",
                  "retailPrice": 1.0,
                  "currencyCode": "USD",
                  "armRegionName": "eastus2"
                }
              ],
              "NextPageLink": null
            }
            """;

        var result = AzureOpenAiRetailPricingParser.Parse(
            json,
            new Uri("https://prices.azure.com/api/retail/prices"),
            new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(2, result.Candidates.Count);
        Assert.Contains(result.Candidates, candidate =>
            candidate.ProviderName == "azure_openai" &&
            candidate.ModelName == "gpt-4o-1120" &&
            candidate.TokenType == PricingTokenType.Input &&
            candidate.BillingRoute == "global_standard" &&
            candidate.PricePerMillionTokens == 2.5m);
        Assert.Contains(result.Candidates, candidate =>
            candidate.TokenType == PricingTokenType.Output &&
            candidate.BillingRoute == "data_zone" &&
            candidate.PricePerMillionTokens == 10.0m);
    }

    [Fact]
    public async Task RefreshPricingCommandCreatesTenantScopedCandidateRecords()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var organization = await store.CreateCustomerOrganizationAsync(
            new CreateCustomerOrganizationRequest(
                "contoso",
                "Contoso",
                "uksouth",
                CustomerOrganizationIsolationTier.Shared));
        using var httpClient = new HttpClient(new StubHttpMessageHandler("""
            Model Input Cached input Output
            gpt-5 $1.25 $0.13 $10.00
            """));
        var service = new ProviderPricingRefreshService(httpClient);
        using var writer = new StringWriter();

        var exitCode = await TokenObservabilityJobsCommandLine.RunAsync(
            [
                "refresh-pricing",
                "--provider",
                "openai",
                "--customer-organization-id",
                organization.CustomerOrganizationId.ToString(),
                "--correlation-id",
                "pricing-refresh-test"
            ],
            writer,
            store,
            service);

        Assert.Equal(0, exitCode);
        Assert.Contains("Created 3 tenant-scoped pricing basis candidate record(s).", writer.ToString());
        var records = await store.ListPricingBasisRecordsAsync(organization.CustomerOrganizationId);
        Assert.Equal(3, records.Count);
        Assert.All(records, record =>
        {
            Assert.Equal(PricingSourceKind.AutomatedSeed, record.SourceKind);
            Assert.Equal(PricingReviewState.Candidate, record.ReviewState);
        });
        Assert.DoesNotContain(records, record => record.ReviewState == PricingReviewState.Approved);
        var auditEvents = await store.ListGovernanceAuditEventsAsync(organization.CustomerOrganizationId);
        Assert.Equal(3, auditEvents.Count(audit => audit.EvidenceMetadata["operation"] == "pricing_seed_refresh"));
    }

    [Fact]
    public async Task RefreshPricingCommandLoadsCustomerOrganizationBeforeSeedingCandidates()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var customerOrganizationId = CustomerOrganizationId.NewId();
        using var httpClient = new HttpClient(new StubHttpMessageHandler("""
            Model Input Cached input Output
            gpt-5 $1.25 $0.13 $10.00
            """));
        var service = new ProviderPricingRefreshService(httpClient);
        using var writer = new StringWriter();

        var exitCode = await TokenObservabilityJobsCommandLine.RunAsync(
            [
                "refresh-pricing",
                "--provider",
                "openai",
                "--customer-organization-id",
                customerOrganizationId.ToString(),
                "--customer-organization-slug",
                "contoso",
                "--customer-organization-display-name",
                "Contoso",
                "--data-residency-region",
                "uksouth"
            ],
            writer,
            store,
            service);

        Assert.Equal(0, exitCode);
        var organization = await store.FindCustomerOrganizationAsync(customerOrganizationId);
        Assert.NotNull(organization);
        Assert.Equal("contoso", organization.Slug);
        Assert.Equal(3, (await store.ListPricingBasisRecordsAsync(customerOrganizationId)).Count);
    }

    [Fact]
    public async Task RefreshPricingCommandRejectsUnloadedCustomerWithoutSlug()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        using var writer = new StringWriter();

        var exitCode = await TokenObservabilityJobsCommandLine.RunAsync(
            [
                "refresh-pricing",
                "--provider",
                "openai",
                "--customer-organization-id",
                CustomerOrganizationId.NewId().ToString()
            ],
            writer,
            store,
            new ProviderPricingRefreshService(new HttpClient(new StubHttpMessageHandler(string.Empty))));

        Assert.Equal(2, exitCode);
        Assert.Contains("requires --customer-organization-slug", writer.ToString());
    }

    [Fact]
    public async Task RefreshPricingCommandReportsProviderFetchFailureWithoutActiveChanges()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var organization = await store.CreateCustomerOrganizationAsync(
            new CreateCustomerOrganizationRequest(
                "contoso",
                "Contoso",
                "uksouth",
                CustomerOrganizationIsolationTier.Shared));
        using var writer = new StringWriter();

        var exitCode = await TokenObservabilityJobsCommandLine.RunAsync(
            [
                "refresh-pricing",
                "--provider",
                "unsupported",
                "--customer-organization-id",
                organization.CustomerOrganizationId.ToString()
            ],
            writer,
            store);

        Assert.Equal(2, exitCode);
        Assert.Contains("No active pricing basis or cost estimate was changed.", writer.ToString());
        Assert.Empty(await store.ListPricingBasisRecordsAsync(organization.CustomerOrganizationId));
    }

    [Fact]
    public async Task RedactContentCommandUsesMetadataOnlyReviewWorkflow()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateContentJobSeedAsync(store, captured: false);
        using var writer = new StringWriter();

        var exitCode = await TokenObservabilityJobsCommandLine.RunAsync(
            [
                "redact-content",
                "--customer-organization-id",
                seed.Organization.CustomerOrganizationId.ToString()
            ],
            writer,
            store);

        Assert.Equal(0, exitCode);
        Assert.Contains("metadata-only content review work", writer.ToString());
        Assert.Contains("No raw failed content was read or emitted.", writer.ToString());
        Assert.DoesNotContain("placeholder", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RetentionCleanupCommandExpiresCapturedBlobPointersAndKeepsMetadata()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateContentJobSeedAsync(store, captured: true);
        Assert.NotNull(seed.ContentReference.BlobPointer);
        using var writer = new StringWriter();

        var exitCode = await TokenObservabilityJobsCommandLine.RunAsync(
            [
                "retention-cleanup",
                "--customer-organization-id",
                seed.Organization.CustomerOrganizationId.ToString(),
                "--as-of-utc",
                Now.AddDays(31).ToString("O"),
                "--correlation-id",
                "retention-cleanup-test"
            ],
            writer,
            store);

        Assert.Equal(0, exitCode);
        Assert.Contains("Expired 1 captured content reference blob pointer(s).", writer.ToString());
        var cleaned = await store.FindContentReferenceAsync(
            seed.Organization.CustomerOrganizationId,
            seed.ContentReference.ContentReferenceId);
        Assert.NotNull(cleaned);
        Assert.Null(cleaned.BlobPointer);
        Assert.Equal(ContentReferenceCaptureState.MetadataOnly, cleaned.CaptureState);
        Assert.False(cleaned.RecommendationEligible);
        Assert.Equal(seed.ContentReference.ContentReferenceId, cleaned.ContentReferenceId);
        var auditEvents = await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId);
        Assert.Contains(auditEvents, audit => audit.EvidenceMetadata["operation"] == "content_retention_cleanup");
    }

    [Fact]
    public async Task GenerateRecommendationsCommandCreatesDeterministicRecommendationRecords()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var organization = await store.CreateCustomerOrganizationAsync(
            new CreateCustomerOrganizationRequest(
                "contoso",
                "Contoso",
                "uksouth",
                CustomerOrganizationIsolationTier.Shared));
        var identityTenant = await store.CreateIdentityTenantAsync(
            organization.CustomerOrganizationId,
            new CreateIdentityTenantRequest(
                IdentityTenantProvider.MicrosoftEntra,
                "https://sts.windows.net/contoso/",
                "contoso",
                ["api://token-observability"],
                JwksUri: null,
                "Contoso Entra ID"));
        var admin = await store.CreateProductUserAsync(
            organization.CustomerOrganizationId,
            identityTenant.IdentityTenantId,
            new CreateProductUserRequest("admin", "admin", "admin@example.test"));
        var developer = await store.CreateProductUserAsync(
            organization.CustomerOrganizationId,
            identityTenant.IdentityTenantId,
            new CreateProductUserRequest("developer", "developer", "developer@example.test"));
        var lifecycle = new ScopedIngestionCredentialLifecycleService(store, new StaticTenantMetadataClock(Now));
        var issued = await lifecycle.CreateAsync(
            organization.CustomerOrganizationId,
            new IssueScopedIngestionCredentialRequest(
                "profile-contoso-codex",
                developer.ProductUserId,
                CodingAgentHarness.CodexCli,
                [new ProductScope(ProductScopeKind.Organization, ScopeId: null)],
                Now.AddDays(30),
                admin.ProductUserId,
                ProductRole.PlatformAdmin,
                "job-credential",
                "audit-job-credential"));
        var session = await SeedTokenSessionAsync(store, issued.Credential, "job-recommendation-session");
        await store.RecordTokenHotspotAsync(new CreateTokenHotspotRecordRequest(
            organization.CustomerOrganizationId,
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
            TokenBurnScore: null,
            EstimatedCostImpact: null,
            DetectionKey: "job-large-context"));
        using var writer = new StringWriter();

        var exitCode = await TokenObservabilityJobsCommandLine.RunAsync(
            [
                "generate-recommendations",
                "--customer-organization-id",
                organization.CustomerOrganizationId.ToString(),
                "--agent-session-id",
                session.AgentSessionId,
                "--correlation-id",
                "job-generate-recommendations"
            ],
            writer,
            store);

        Assert.Equal(0, exitCode);
        Assert.Contains("Created 1 recommendation record(s).", writer.ToString());
        Assert.Contains("deterministic fallback", writer.ToString());
        Assert.DoesNotContain("raw_prompt", writer.ToString(), StringComparison.OrdinalIgnoreCase);
        var recommendations = await store.ListRecommendationsForSessionAsync(
            organization.CustomerOrganizationId,
            session.AgentSessionId);
        Assert.Single(recommendations);
        var auditEvents = await store.ListGovernanceAuditEventsAsync(organization.CustomerOrganizationId);
        Assert.Single(auditEvents, audit => audit.EvidenceMetadata["operation"] == "recommendation_generation");
    }

    [Fact]
    public async Task GenerateRecommendationsCommandRequiresLoadedTenantAndSessionInputs()
    {
        using var writer = new StringWriter();

        var exitCode = await TokenObservabilityJobsCommandLine.RunAsync(
            ["generate-recommendations"],
            writer,
            new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now)));

        Assert.Equal(2, exitCode);
        Assert.Contains("--customer-organization-id", writer.ToString());
        Assert.Contains("--agent-session-id", writer.ToString());
    }

    private static async Task<ContentJobSeed> CreateContentJobSeedAsync(
        InMemoryTenantMetadataStore store,
        bool captured)
    {
        var organization = await store.CreateCustomerOrganizationAsync(
            new CreateCustomerOrganizationRequest(
                captured ? "contoso-captured" : "contoso-review",
                "Contoso",
                "uksouth",
                CustomerOrganizationIsolationTier.Shared));
        var identityTenant = await store.CreateIdentityTenantAsync(
            organization.CustomerOrganizationId,
            new CreateIdentityTenantRequest(
                IdentityTenantProvider.MicrosoftEntra,
                $"https://sts.windows.net/{organization.Slug}/",
                organization.Slug,
                ["api://token-observability"],
                JwksUri: null,
                "Contoso Entra ID"));
        var admin = await store.CreateProductUserAsync(
            organization.CustomerOrganizationId,
            identityTenant.IdentityTenantId,
            new CreateProductUserRequest($"admin-{organization.Slug}", "admin", "admin@example.test"));
        var developer = await store.CreateProductUserAsync(
            organization.CustomerOrganizationId,
            identityTenant.IdentityTenantId,
            new CreateProductUserRequest($"developer-{organization.Slug}", "developer", "developer@example.test"));
        var lifecycle = new ScopedIngestionCredentialLifecycleService(store, new StaticTenantMetadataClock(Now));
        var issued = await lifecycle.CreateAsync(
            organization.CustomerOrganizationId,
            new IssueScopedIngestionCredentialRequest(
                $"profile-{organization.Slug}",
                developer.ProductUserId,
                CodingAgentHarness.CodexCli,
                [new ProductScope(ProductScopeKind.Organization, ScopeId: null)],
                Now.AddDays(30),
                admin.ProductUserId,
                ProductRole.PlatformAdmin,
                "job-content-credential",
                $"audit-job-content-credential-{organization.Slug}"));
        var session = await SeedTokenSessionAsync(store, issued.Credential, $"content-job-{organization.Slug}");
        var telemetryEnvelopeId = Assert.Single(session.SourceTelemetryEnvelopeIds);

        await store.RecordContentCandidateMetadataAsync(new CreateContentCandidateMetadataRequest(
            organization.CustomerOrganizationId,
            PolicyVersionId: "policy-content-v1",
            issued.Credential.ScopedIngestionCredentialId,
            issued.Credential.HarnessSetupProfileId,
            session.AgentSessionId,
            $"{telemetryEnvelopeId}:otlp.log.body",
            ContentClass.PromptSnippet,
            OriginalLength: 32,
            ContentCapturePolicyDecision.CaptureAllowed,
            captured ? ContentCandidateEvidenceState.Candidate : ContentCandidateEvidenceState.ReviewRequired,
            captured ? ContentRedactionStatus.Passed : ContentRedactionStatus.ReviewRequired,
            ContentRetentionClass.Short,
            RecommendationUse.Disabled,
            captured ? ContentRedactionOutcome.Captured : ContentRedactionOutcome.ReviewRequired,
            RedactionDecisionReason: captured ? "redaction_passed" : "pii_low_confidence",
            RedactionPipelineVersion: "content-redaction-pipeline-v1",
            ProductRuleVersion: "product-redaction-rules-v1",
            RedactedContentHash: captured ? ComputeSha256Hex("redacted content") : null,
            RedactedContentStored: captured));

        var contentReference = Assert.Single(await store.ListContentReferencesForSessionAsync(
            organization.CustomerOrganizationId,
            session.AgentSessionId));
        return new ContentJobSeed(organization, contentReference);
    }

    private sealed class StubHttpMessageHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }
    }

    private static async Task<AgentSessionRecord> SeedTokenSessionAsync(
        InMemoryTenantMetadataStore store,
        ScopedIngestionCredential credential,
        string providerSessionId)
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
            MetricState: "observed",
            MetricStatus: TokenMetricStatus.Observed,
            MetricConfidence: TokenMetricConfidence.Observed,
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
        await store.RecordTokenObservationAsync(new CreateTokenObservationRecordRequest(
            credential.CustomerOrganizationId,
            session.AgentSessionId,
            ModelInvocationId: null,
            TokenMetricName.InputTokens,
            Value: 120_000,
            TokenMetricStatus.Observed,
            TokenMetricConfidence.Observed,
            TokenObservationSourceKind.CodexEvent,
            envelope.TelemetryEnvelopeId));
        return session;
    }

    private static string ComputeSha256Hex(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private sealed record ContentJobSeed(
        CustomerOrganization Organization,
        ContentReferenceRecord ContentReference);
}
