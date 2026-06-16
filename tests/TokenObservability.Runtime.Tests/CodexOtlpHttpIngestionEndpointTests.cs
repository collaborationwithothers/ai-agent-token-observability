using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TokenObservability.Domain.Authorization;
using TokenObservability.Domain.Ingestion;
using TokenObservability.Domain.Tenancy;
using TokenObservability.Infrastructure.Ingestion;
using TokenObservability.Infrastructure.Persistence;
using TokenObservability.Ingestion;

namespace TokenObservability.Runtime.Tests;

public sealed class CodexOtlpHttpIngestionEndpointTests
{
    private const string Harness = "codex-cli";
    private const string SetupProfileId = "profile-codex-cli-eastus2";
    private const string SchemaVersion = "2026-06-01";
    private const string OtlpContentType = "application/x-protobuf";
    private static readonly byte[] MinimalOtlpLogExportRequest =
    [
        0x0A, 0x0A,
        0x12, 0x08,
        0x12, 0x06,
        0x1A, 0x04,
        0x69, 0x6E, 0x66, 0x6F
    ];
    private static readonly byte[] ScopeOnlyOtlpLogExportRequest = [0x0A, 0x04, 0x12, 0x02, 0x0A, 0x00];
    private static readonly byte[] MinimalOtlpTraceExportRequest =
    [
        0x0A, 0x20,
        0x12, 0x1E,
        0x12, 0x1C,
        0x0A, 0x10,
        0x01, 0x02, 0x03, 0x04,
        0x05, 0x06, 0x07, 0x08,
        0x09, 0x0A, 0x0B, 0x0C,
        0x0D, 0x0E, 0x0F, 0x10,
        0x12, 0x08,
        0x11, 0x12, 0x13, 0x14,
        0x15, 0x16, 0x17, 0x18
    ];
    private static readonly byte[] MinimalOtlpMetricExportRequest =
    [
        0x0A, 0x0C,
        0x12, 0x0A,
        0x12, 0x08,
        0x0A, 0x04,
        0x63, 0x6F, 0x73, 0x74,
        0x2A, 0x00
    ];
    private static readonly DateTimeOffset Now = new(2026, 6, 17, 11, 15, 0, TimeSpan.Zero);

    public static TheoryData<string> OtlpSignalRoutes()
    {
        return new TheoryData<string>
        {
            "/v1/logs",
            "/v1/traces",
            "/v1/metrics"
        };
    }

    public static TheoryData<string, byte[]> OtlpWrongSignalPayloads()
    {
        return new TheoryData<string, byte[]>
        {
            { "/v1/logs", MinimalOtlpTraceExportRequest },
            { "/v1/logs", MinimalOtlpMetricExportRequest },
            { "/v1/traces", MinimalOtlpLogExportRequest },
            { "/v1/traces", MinimalOtlpMetricExportRequest },
            { "/v1/metrics", MinimalOtlpLogExportRequest },
            { "/v1/metrics", MinimalOtlpTraceExportRequest }
        };
    }

    [Theory]
    [MemberData(nameof(OtlpSignalRoutes))]
    public async Task CodexOtlpEndpointAcceptsValidAuthenticatedTelemetryWithOtlpResponse(string path)
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest(path, issued.Secret);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OtlpContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        Assert.False(response.Headers.Contains("X-AITO-Rejection-Code"));
    }

    [Fact]
    public async Task CodexOtlpEndpointPersistsAcceptedTelemetryWithUnavailableTokenMetrics()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest("/v1/metrics", issued.Secret);
        request.Headers.Add("X-Correlation-Id", "codex-ingest-accepted-metrics-001");

        using var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();

        var envelope = Assert.Single(await store.ListTelemetryEnvelopesAsync(seed.Organization.CustomerOrganizationId));
        Assert.Equal(seed.Organization.CustomerOrganizationId, envelope.CustomerOrganizationId);
        Assert.Equal(issued.Credential.ScopedIngestionCredentialId, envelope.ScopedIngestionCredentialId);
        Assert.Equal(issued.Credential.ProductUserId, envelope.ProductUserId);
        Assert.Equal("metrics", envelope.SignalType);
        Assert.Equal(TokenMetricStatus.Unavailable, envelope.MetricStatus);
        Assert.Equal(TokenMetricConfidence.Unavailable, envelope.MetricConfidence);
        Assert.Equal("metadata_only", envelope.ContentPolicyDecision);

        var session = Assert.Single(await store.ListAgentSessionsAsync(seed.Organization.CustomerOrganizationId));
        Assert.Equal(issued.Credential.ProductUserId, session.ProductUserId);
        Assert.Equal(TokenMetricStatus.Unavailable, session.TokenMetricStatus);
        Assert.Equal(TokenMetricConfidence.Unavailable, session.TokenMetricConfidence);

        var observations = await store.ListTokenObservationsAsync(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId);
        Assert.Equal(
            [
                TokenMetricName.InputTokens,
                TokenMetricName.OutputTokens,
                TokenMetricName.CachedInputTokens,
                TokenMetricName.ReasoningOutputTokens,
                TokenMetricName.TotalTokens
            ],
            observations.Select(observation => observation.MetricName).ToArray());
        Assert.All(observations, observation =>
        {
            Assert.Null(observation.Value);
            Assert.Equal(TokenMetricStatus.Unavailable, observation.MetricStatus);
            Assert.Equal(TokenMetricConfidence.Unavailable, observation.MetricConfidence);
            Assert.Equal(TokenObservationSourceKind.Missing, observation.SourceKind);
            Assert.Equal(envelope.TelemetryEnvelopeId, observation.SourceTelemetryEnvelopeId);
        });
    }

    [Fact]
    public async Task CodexOtlpEndpointTreatsDuplicateAcceptedTelemetryAsIdempotent()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var firstRequest = CreateOtlpRequest("/v1/metrics", issued.Secret);
        using var secondRequest = CreateOtlpRequest("/v1/metrics", issued.Secret);
        firstRequest.Headers.Add("X-Correlation-Id", "codex-ingest-accepted-duplicate-001");
        secondRequest.Headers.Add("X-Correlation-Id", "codex-ingest-accepted-duplicate-001");

        using var firstResponse = await client.SendAsync(firstRequest);
        using var secondResponse = await client.SendAsync(secondRequest);

        firstResponse.EnsureSuccessStatusCode();
        secondResponse.EnsureSuccessStatusCode();

        var envelope = Assert.Single(await store.ListTelemetryEnvelopesAsync(seed.Organization.CustomerOrganizationId));
        var session = Assert.Single(await store.ListAgentSessionsAsync(seed.Organization.CustomerOrganizationId));
        var observations = await store.ListTokenObservationsAsync(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId);
        Assert.Equal(5, observations.Count);
        Assert.All(observations, observation => Assert.Equal(envelope.TelemetryEnvelopeId, observation.SourceTelemetryEnvelopeId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CodexOtlpEndpointDedupeDoesNotDependOnCallerSuppliedCorrelationId(bool useUnsafeCorrelationId)
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var firstRequest = CreateOtlpRequest("/v1/metrics", issued.Secret);
        using var secondRequest = CreateOtlpRequest("/v1/metrics", issued.Secret);
        firstRequest.Headers.Remove("X-Correlation-Id");
        secondRequest.Headers.Remove("X-Correlation-Id");

        if (useUnsafeCorrelationId)
        {
            firstRequest.Headers.Add("X-Correlation-Id", "unsafe correlation id with spaces");
            secondRequest.Headers.Add("X-Correlation-Id", "unsafe correlation id with spaces");
        }

        using var firstResponse = await client.SendAsync(firstRequest);
        using var secondResponse = await client.SendAsync(secondRequest);

        firstResponse.EnsureSuccessStatusCode();
        secondResponse.EnsureSuccessStatusCode();

        var envelope = Assert.Single(await store.ListTelemetryEnvelopesAsync(seed.Organization.CustomerOrganizationId));
        var session = Assert.Single(await store.ListAgentSessionsAsync(seed.Organization.CustomerOrganizationId));
        var observations = await store.ListTokenObservationsAsync(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId);
        Assert.Equal(5, observations.Count);
        Assert.All(observations, observation => Assert.Equal(envelope.TelemetryEnvelopeId, observation.SourceTelemetryEnvelopeId));
    }

    [Fact]
    public async Task CodexOtlpEndpointRejectsContentBearingLogsBeforeMetadataStorage()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        var payload = CreateLogPayloadWithBody("prompt text that must not be fingerprinted");
        var forbiddenPayloadHash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest("/v1/logs", issued.Secret, payload);

        using var response = await client.SendAsync(request);

        await AssertOtlpRejectionAsync(response, HttpStatusCode.Forbidden, "policy_context_missing", forbiddenPayloadHash);

        Assert.Empty(await store.ListTelemetryEnvelopesAsync(seed.Organization.CustomerOrganizationId));
        Assert.Empty(await store.ListAgentSessionsAsync(seed.Organization.CustomerOrganizationId));
        var rejection = Assert.Single(await store.ListIngestionRejectionsAsync(seed.Organization.CustomerOrganizationId));
        Assert.Equal("policy_context_missing", rejection.ReasonCode);
        Assert.DoesNotContain(forbiddenPayloadHash, rejection.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodexOtlpEndpointDoesNotCollapseDistinctSafeMetadataPayloadsWithSameShape()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var firstRequest = CreateOtlpRequest("/v1/metrics", issued.Secret, CreateMetricPayload("cost"));
        using var secondRequest = CreateOtlpRequest("/v1/metrics", issued.Secret, CreateMetricPayload("burn"));
        firstRequest.Headers.Remove("X-Correlation-Id");
        secondRequest.Headers.Remove("X-Correlation-Id");

        using var firstResponse = await client.SendAsync(firstRequest);
        using var secondResponse = await client.SendAsync(secondRequest);

        firstResponse.EnsureSuccessStatusCode();
        secondResponse.EnsureSuccessStatusCode();

        Assert.Equal(2, (await store.ListTelemetryEnvelopesAsync(seed.Organization.CustomerOrganizationId)).Count);
        Assert.Equal(2, (await store.ListAgentSessionsAsync(seed.Organization.CustomerOrganizationId)).Count);
    }

    [Fact]
    public async Task CodexOtlpEndpointDoesNotCollapseDistinctSafeMetricDataWithSameLength()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var firstRequest = CreateOtlpRequest("/v1/metrics", issued.Secret, CreateMetricPayloadWithIntData("cost", value: 7));
        using var secondRequest = CreateOtlpRequest("/v1/metrics", issued.Secret, CreateMetricPayloadWithIntData("cost", value: 8));
        firstRequest.Headers.Remove("X-Correlation-Id");
        secondRequest.Headers.Remove("X-Correlation-Id");

        using var firstResponse = await client.SendAsync(firstRequest);
        using var secondResponse = await client.SendAsync(secondRequest);

        firstResponse.EnsureSuccessStatusCode();
        secondResponse.EnsureSuccessStatusCode();

        Assert.Equal(2, (await store.ListTelemetryEnvelopesAsync(seed.Organization.CustomerOrganizationId)).Count);
        Assert.Equal(2, (await store.ListAgentSessionsAsync(seed.Organization.CustomerOrganizationId)).Count);
    }

    [Fact]
    public async Task TokenObservationStorePreservesMetricStatesNullsAndTrueZeroes()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        var envelope = await store.RecordTelemetryEnvelopeAsync(new CreateTelemetryEnvelopeRecordRequest(
            CustomerOrganizationId: seed.Organization.CustomerOrganizationId,
            HarnessSetupProfileId: issued.Credential.HarnessSetupProfileId,
            ScopedIngestionCredentialId: issued.Credential.ScopedIngestionCredentialId,
            ProductUserId: issued.Credential.ProductUserId,
            Harness: CodingAgentHarness.CodexCli,
            SchemaVersion,
            SignalType: "metrics",
            SourceEventName: "codex.api_request",
            SourceEventTimestampUtc: Now,
            ConversationIdHash: "conversation-001",
            ModelName: "gpt-5",
            ContentPolicyDecision: "metadata_only",
            RoutingDecision: "metadata_store",
            MetricStatus: TokenMetricStatus.Observed,
            MetricConfidence: TokenMetricConfidence.Observed,
            DedupeKeyHash: "dedupe-001"));
        var session = await store.UpsertAgentSessionAsync(new CreateAgentSessionRecordRequest(
            CustomerOrganizationId: seed.Organization.CustomerOrganizationId,
            ProductUserId: issued.Credential.ProductUserId,
            HarnessSetupProfileId: issued.Credential.HarnessSetupProfileId,
            Harness: CodingAgentHarness.CodexCli,
            ProviderSessionIdHash: "conversation-001",
            StartedAtUtc: Now,
            EndedAtUtc: null,
            SessionStatus: AgentSessionStatus.Active,
            RepositoryEvidenceState: RepositoryEvidenceState.Unavailable,
            ContentCaptureSummary: ContentCaptureSummary.MetadataOnly,
            RecommendationStatus: RecommendationStatus.NotStarted,
            TokenMetricStatus: TokenMetricStatus.Mixed,
            TokenMetricConfidence: TokenMetricConfidence.Estimated));

        await store.RecordTokenObservationAsync(new CreateTokenObservationRecordRequest(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            ModelInvocationId: null,
            TokenMetricName.InputTokens,
            Value: 0,
            TokenMetricStatus.Observed,
            TokenMetricConfidence.Observed,
            TokenObservationSourceKind.OtlpMetric,
            envelope.TelemetryEnvelopeId));
        await store.RecordTokenObservationAsync(new CreateTokenObservationRecordRequest(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            ModelInvocationId: null,
            TokenMetricName.OutputTokens,
            Value: 7,
            TokenMetricStatus.Estimated,
            TokenMetricConfidence.Estimated,
            TokenObservationSourceKind.Estimator,
            envelope.TelemetryEnvelopeId));
        await store.RecordTokenObservationAsync(new CreateTokenObservationRecordRequest(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            ModelInvocationId: null,
            TokenMetricName.CachedInputTokens,
            Value: null,
            TokenMetricStatus.Unavailable,
            TokenMetricConfidence.Unavailable,
            TokenObservationSourceKind.Missing,
            envelope.TelemetryEnvelopeId));
        await store.RecordTokenObservationAsync(new CreateTokenObservationRecordRequest(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            ModelInvocationId: null,
            TokenMetricName.ReasoningOutputTokens,
            Value: null,
            TokenMetricStatus.NotApplicable,
            TokenMetricConfidence.Unavailable,
            TokenObservationSourceKind.Missing,
            envelope.TelemetryEnvelopeId));
        await store.RecordTokenObservationAsync(new CreateTokenObservationRecordRequest(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            ModelInvocationId: null,
            TokenMetricName.TotalTokens,
            Value: 7,
            TokenMetricStatus.Mixed,
            TokenMetricConfidence.Estimated,
            TokenObservationSourceKind.DerivedSummary,
            envelope.TelemetryEnvelopeId));

        var observations = await store.ListTokenObservationsAsync(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId);

        Assert.Collection(
            observations,
            observation =>
            {
                Assert.Equal(TokenMetricName.InputTokens, observation.MetricName);
                Assert.Equal(0, observation.Value);
                Assert.Equal(TokenMetricStatus.Observed, observation.MetricStatus);
            },
            observation =>
            {
                Assert.Equal(TokenMetricName.OutputTokens, observation.MetricName);
                Assert.Equal(7, observation.Value);
                Assert.Equal(TokenMetricStatus.Estimated, observation.MetricStatus);
            },
            observation =>
            {
                Assert.Equal(TokenMetricName.CachedInputTokens, observation.MetricName);
                Assert.Null(observation.Value);
                Assert.Equal(TokenMetricStatus.Unavailable, observation.MetricStatus);
            },
            observation =>
            {
                Assert.Equal(TokenMetricName.ReasoningOutputTokens, observation.MetricName);
                Assert.Null(observation.Value);
                Assert.Equal(TokenMetricStatus.NotApplicable, observation.MetricStatus);
            },
            observation =>
            {
                Assert.Equal(TokenMetricName.TotalTokens, observation.MetricName);
                Assert.Equal(7, observation.Value);
                Assert.Equal(TokenMetricStatus.Mixed, observation.MetricStatus);
            });

        await Assert.ThrowsAsync<ArgumentException>(() => store.RecordTokenObservationAsync(
            new CreateTokenObservationRecordRequest(
                seed.Organization.CustomerOrganizationId,
                session.AgentSessionId,
                ModelInvocationId: null,
                TokenMetricName.CachedInputTokens,
                Value: 1,
                TokenMetricStatus.Unavailable,
                TokenMetricConfidence.Unavailable,
                TokenObservationSourceKind.Missing,
                envelope.TelemetryEnvelopeId)));
        await Assert.ThrowsAsync<ArgumentException>(() => store.RecordTokenObservationAsync(
            new CreateTokenObservationRecordRequest(
                seed.Organization.CustomerOrganizationId,
                session.AgentSessionId,
                ModelInvocationId: null,
                TokenMetricName.CachedInputTokens,
                Value: 0,
                TokenMetricStatus.Estimated,
                TokenMetricConfidence.Estimated,
                TokenObservationSourceKind.Estimator,
                envelope.TelemetryEnvelopeId)));
    }

    [Fact]
    public async Task CodexOtlpEndpointRejectsMissingCredentialBeforeParsingPayload()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest("/v1/logs", secret: null, payload: [0xFF, 0xFF, 0xFF]);
        request.Headers.Add("X-Customer-Organization-Slug", "contoso");
        request.Headers.Add("X-Correlation-Id", "codex-ingest-missing-credential-001");

        using var response = await client.SendAsync(request);

        await AssertOtlpRejectionAsync(response, HttpStatusCode.Unauthorized, "invalid_credential");

        var rejection = Assert.Single(await store.ListIngestionRejectionsAsync(seed.Organization.CustomerOrganizationId));
        Assert.Equal(seed.Organization.CustomerOrganizationId, rejection.CustomerOrganizationId);
        Assert.Null(rejection.ScopedIngestionCredentialId);
        Assert.Null(rejection.HarnessSetupProfileId);
        Assert.Equal("codex-cli", rejection.DeclaredHarness);
        Assert.Equal("logs", rejection.SignalType);
        Assert.Equal("invalid_credential", rejection.ReasonCode);
        Assert.Equal(StatusCodes.Status401Unauthorized, rejection.HttpStatus);
        Assert.Equal("codex-ingest-missing-credential-001", rejection.CorrelationId);
        var auditEvent = Assert.Single(
            await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId),
            auditEvent => auditEvent.CorrelationId == "codex-ingest-missing-credential-001");
        Assert.Equal(ProductAuthorizationAction.TelemetryIngest, auditEvent.Action);
        Assert.Equal("ingestion_rejection", auditEvent.EvidenceMetadata["operation"]);
        Assert.Equal("invalid_credential", auditEvent.EvidenceMetadata["result"]);
        Assert.Equal("unknown", auditEvent.EvidenceMetadata["scope_id"]);
        Assert.Equal(auditEvent.AuditEventId, rejection.AuditEventId);
        AssertRejectionMetadataDoesNotContain(rejection, "FF");
    }

    [Fact]
    public async Task CodexOtlpEndpointDoesNotPersistUnsafeCallerSuppliedHarnessContextOnCredentialRejection()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest("/v1/logs", secret: null, payload: [0xFF]);
        request.Headers.Remove("X-AITO-Harness");
        request.Headers.Remove("X-AITO-Setup-Profile-Id");
        request.Headers.Add("X-AITO-Harness", "codex-cli-secret=leaked");
        request.Headers.Add("X-AITO-Setup-Profile-Id", "profile-secret=leaked");
        request.Headers.Add("X-Customer-Organization-Slug", "contoso");

        using var response = await client.SendAsync(request);

        await AssertOtlpRejectionAsync(response, HttpStatusCode.Unauthorized, "invalid_credential");

        var rejection = Assert.Single(await store.ListIngestionRejectionsAsync(seed.Organization.CustomerOrganizationId));
        Assert.Null(rejection.DeclaredHarness);
        Assert.Null(rejection.HarnessSetupProfileId);
        Assert.Equal("unknown", rejection.EvidenceMetadata["scope_id"]);
        Assert.DoesNotContain("secret=leaked", rejection.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CodexOtlpEndpointDoesNotPersistSafeLookingSetupProfileOnCredentiallessRejection()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest("/v1/logs", secret: null, payload: [0xFF]);
        request.Headers.Remove("X-AITO-Setup-Profile-Id");
        request.Headers.Add("X-AITO-Setup-Profile-Id", "profile-codex-cli-eastus2-safe-looking-value");
        request.Headers.Add("X-Customer-Organization-Slug", "contoso");

        using var response = await client.SendAsync(request);

        await AssertOtlpRejectionAsync(response, HttpStatusCode.Unauthorized, "invalid_credential");

        var rejection = Assert.Single(await store.ListIngestionRejectionsAsync(seed.Organization.CustomerOrganizationId));
        Assert.Null(rejection.HarnessSetupProfileId);
        Assert.Equal("codex-cli", rejection.DeclaredHarness);
        Assert.Equal("unknown", rejection.EvidenceMetadata["scope_id"]);
        Assert.DoesNotContain("profile-codex-cli-eastus2-safe-looking-value", rejection.ToString(), StringComparison.Ordinal);
        var auditEvent = Assert.Single(await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId));
        Assert.Equal("unknown", auditEvent.EvidenceMetadata["scope_id"]);
        Assert.DoesNotContain(
            "profile-codex-cli-eastus2-safe-looking-value",
            auditEvent.ToString(),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-a-token")]
    [InlineData("aito_live_unknownCredentialValue000000000000")]
    public async Task CodexOtlpEndpointRejectsMalformedAndUnknownCredentialsWithoutLeakingInputs(string secret)
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        await CreateTenantAsync(store);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest("/v1/logs", secret);

        using var response = await client.SendAsync(request);

        await AssertOtlpRejectionAsync(response, HttpStatusCode.Unauthorized, "invalid_credential", secret);
    }

    [Fact]
    public async Task CodexOtlpEndpointRejectsWrongHarnessContextWithAuditableMetadata()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest("/v1/logs", issued.Secret);
        request.Headers.Remove("X-AITO-Harness");
        request.Headers.Add("X-AITO-Harness", "claude-code");
        request.Headers.Add("X-Correlation-Id", "codex-ingest-wrong-harness-001");

        using var response = await client.SendAsync(request);

        await AssertOtlpRejectionAsync(response, HttpStatusCode.Forbidden, "credential_out_of_scope", issued.Secret);

        var auditEvent = Assert.Single(
            await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId),
            auditEvent => auditEvent.CorrelationId == "codex-ingest-wrong-harness-001");
        Assert.Equal(ProductAuthorizationAction.TelemetryIngest, auditEvent.Action);
        Assert.Equal("denied", auditEvent.Decision);
        Assert.Equal("scoped_ingestion_credential_failed_access", auditEvent.EvidenceMetadata["operation"]);
        Assert.Equal("wrong_harness", auditEvent.EvidenceMetadata["result"]);

        var rejection = AssertSingleRejection(
            await store.ListIngestionRejectionsAsync(seed.Organization.CustomerOrganizationId),
            "codex-ingest-wrong-harness-001",
            "credential_out_of_scope");
        Assert.Equal("logs", rejection.SignalType);
        Assert.Null(rejection.DeclaredHarness);
        Assert.Equal(StatusCodes.Status403Forbidden, rejection.HttpStatus);
        Assert.Equal(auditEvent.AuditEventId, rejection.AuditEventId);
    }

    [Fact]
    public async Task CodexOtlpEndpointRejectsWrongSetupProfileWithAuditableMetadata()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest("/v1/logs", issued.Secret);
        request.Headers.Remove("X-AITO-Setup-Profile-Id");
        request.Headers.Add("X-AITO-Setup-Profile-Id", "profile-other");
        request.Headers.Add("X-Correlation-Id", "codex-ingest-wrong-profile-001");

        using var response = await client.SendAsync(request);

        await AssertOtlpRejectionAsync(response, HttpStatusCode.Forbidden, "credential_out_of_scope", issued.Secret);

        var auditEvent = Assert.Single(
            await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId),
            auditEvent => auditEvent.CorrelationId == "codex-ingest-wrong-profile-001");
        Assert.Equal(ProductAuthorizationAction.TelemetryIngest, auditEvent.Action);
        Assert.Equal("wrong_harness_profile", auditEvent.EvidenceMetadata["result"]);
    }

    [Theory]
    [InlineData("X-AITO-Harness", "malformed_harness_context")]
    [InlineData("X-AITO-Setup-Profile-Id", "malformed_harness_context")]
    public async Task CodexOtlpEndpointAuditsMissingHarnessContextWhenCredentialIdentifiesTenant(
        string headerToRemove,
        string expectedAuditReason)
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest("/v1/logs", issued.Secret);
        request.Headers.Remove(headerToRemove);
        request.Headers.Add("X-Correlation-Id", $"codex-ingest-missing-{headerToRemove}-001");

        using var response = await client.SendAsync(request);

        await AssertOtlpRejectionAsync(response, HttpStatusCode.Forbidden, "credential_out_of_scope", issued.Secret);

        var auditEvent = Assert.Single(
            await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId),
            auditEvent => auditEvent.CorrelationId == $"codex-ingest-missing-{headerToRemove}-001");
        Assert.Equal(ProductAuthorizationAction.TelemetryIngest, auditEvent.Action);
        Assert.Equal("scoped_ingestion_credential_failed_access", auditEvent.EvidenceMetadata["operation"]);
        Assert.Equal(expectedAuditReason, auditEvent.EvidenceMetadata["result"]);
    }

    [Theory]
    [InlineData(ScopedIngestionCredentialStatus.Disabled)]
    [InlineData(ScopedIngestionCredentialStatus.Revoked)]
    [InlineData(ScopedIngestionCredentialStatus.Expired)]
    public async Task CodexOtlpEndpointRejectsInactiveCredentialLifecycleStates(ScopedIngestionCredentialStatus status)
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        await SetCredentialStatusAsync(store, seed, issued.Credential, status);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest("/v1/logs", issued.Secret);
        request.Headers.Add("X-Correlation-Id", $"codex-ingest-{status}-001");

        using var response = await client.SendAsync(request);

        await AssertOtlpRejectionAsync(response, HttpStatusCode.Unauthorized, "invalid_credential", issued.Secret);

        Assert.Contains(
            await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId),
            auditEvent => auditEvent.Action == ProductAuthorizationAction.TelemetryIngest &&
                auditEvent.EvidenceMetadata["operation"] == "scoped_ingestion_credential_failed_access");
    }

    [Fact]
    public async Task CodexOtlpEndpointClassifiesInactiveCredentialBeforeMalformedHarnessContext()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        await SetCredentialStatusAsync(store, seed, issued.Credential, ScopedIngestionCredentialStatus.Revoked);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest("/v1/logs", issued.Secret);
        request.Headers.Remove("X-AITO-Harness");
        request.Headers.Add("X-Correlation-Id", "codex-ingest-revoked-missing-harness-001");

        using var response = await client.SendAsync(request);

        await AssertOtlpRejectionAsync(response, HttpStatusCode.Unauthorized, "invalid_credential", issued.Secret);

        var auditEvent = Assert.Single(
            await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId),
            auditEvent => auditEvent.CorrelationId == "codex-ingest-revoked-missing-harness-001");
        Assert.Equal("revoked", auditEvent.EvidenceMetadata["result"]);
    }

    [Fact]
    public async Task CodexOtlpEndpointRejectsInactiveCredentialDerivedTenant()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        await store.SetCustomerOrganizationStatusAsync(
            seed.Organization.CustomerOrganizationId,
            CustomerOrganizationStatus.Suspended);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest("/v1/logs", issued.Secret);
        request.Headers.Add("X-Correlation-Id", "codex-ingest-inactive-tenant-001");

        using var response = await client.SendAsync(request);

        await AssertOtlpRejectionAsync(response, HttpStatusCode.Forbidden, "invalid_tenant", issued.Secret);

        var auditEvent = Assert.Single(
            await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId),
            auditEvent => auditEvent.CorrelationId == "codex-ingest-inactive-tenant-001");
        Assert.Equal(ProductAuthorizationAction.TelemetryIngest, auditEvent.Action);
        Assert.Equal("invalid_tenant", auditEvent.EvidenceMetadata["result"]);
    }

    [Fact]
    public async Task CodexOtlpEndpointRejectsTenantHintMismatchWithoutChangingCredentialDerivedTenant()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        await CreateTenantAsync(store, slug: "fabrikam", externalTenantId: "fabrikam-tenant");
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest("/v1/logs", issued.Secret);
        request.Headers.Add("X-Customer-Organization-Slug", "fabrikam");
        request.Headers.Add("X-Correlation-Id", "codex-ingest-tenant-mismatch-001");

        using var response = await client.SendAsync(request);

        await AssertOtlpRejectionAsync(response, HttpStatusCode.Forbidden, "tenant_context_mismatch", issued.Secret);

        var contosoAuditEvent = Assert.Single(
            await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId),
            auditEvent => auditEvent.CorrelationId == "codex-ingest-tenant-mismatch-001");
        Assert.Equal(ProductAuthorizationAction.TelemetryIngest, contosoAuditEvent.Action);
        Assert.Equal("ingestion_rejection", contosoAuditEvent.EvidenceMetadata["operation"]);
        Assert.Equal("tenant_context_mismatch", contosoAuditEvent.EvidenceMetadata["result"]);

        var rejection = Assert.Single(await store.ListIngestionRejectionsAsync(seed.Organization.CustomerOrganizationId));
        Assert.Equal(issued.Credential.ScopedIngestionCredentialId, rejection.ScopedIngestionCredentialId);
        Assert.Equal("tenant_context_mismatch", rejection.ReasonCode);
        Assert.Equal(StatusCodes.Status403Forbidden, rejection.HttpStatus);
        Assert.Equal("codex-ingest-tenant-mismatch-001", rejection.CorrelationId);
        Assert.Equal(contosoAuditEvent.AuditEventId, rejection.AuditEventId);
        Assert.Equal("tenant_context_mismatch", rejection.EvidenceMetadata["result"]);
        Assert.DoesNotContain(issued.Secret, rejection.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodexOtlpEndpointRejectsDataResidencyMismatchWithAuditableMetadata()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store, endpointRegion: "westeurope");
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest("/v1/logs", issued.Secret);
        request.Headers.Add("X-Correlation-Id", "codex-ingest-residency-mismatch-001");

        using var response = await client.SendAsync(request);

        await AssertOtlpRejectionAsync(response, HttpStatusCode.Forbidden, "residency_mismatch", issued.Secret);

        var auditEvent = Assert.Single(
            await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId),
            auditEvent => auditEvent.CorrelationId == "codex-ingest-residency-mismatch-001");
        Assert.Equal(ProductAuthorizationAction.TelemetryIngest, auditEvent.Action);
        Assert.Equal("ingestion_rejection", auditEvent.EvidenceMetadata["operation"]);
        Assert.Equal("residency_mismatch", auditEvent.EvidenceMetadata["result"]);

        var rejection = AssertSingleRejection(
            await store.ListIngestionRejectionsAsync(seed.Organization.CustomerOrganizationId),
            "codex-ingest-residency-mismatch-001",
            "residency_mismatch");
        Assert.Equal(StatusCodes.Status403Forbidden, rejection.HttpStatus);
        Assert.Equal(auditEvent.AuditEventId, rejection.AuditEventId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("capture_candidate")]
    [InlineData("redaction_required")]
    public async Task CodexOtlpEndpointRejectsMissingOrUnsupportedPolicyContextWithAuditableMetadata(
        string? contentCaptureMode)
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest("/v1/logs", issued.Secret);
        request.Headers.Remove("X-AITO-Content-Capture-Mode");
        request.Headers.Add("X-Correlation-Id", "codex-ingest-policy-context-001");

        if (contentCaptureMode is not null)
        {
            request.Headers.Add("X-AITO-Content-Capture-Mode", contentCaptureMode);
        }

        using var response = await client.SendAsync(request);

        await AssertOtlpRejectionAsync(response, HttpStatusCode.Forbidden, "policy_context_missing", issued.Secret);

        var auditEvent = Assert.Single(
            await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId),
            auditEvent => auditEvent.CorrelationId == "codex-ingest-policy-context-001");
        Assert.Equal(ProductAuthorizationAction.TelemetryIngest, auditEvent.Action);
        Assert.Equal("ingestion_rejection", auditEvent.EvidenceMetadata["operation"]);
        Assert.Equal("policy_context_missing", auditEvent.EvidenceMetadata["result"]);

        var rejection = AssertSingleRejection(
            await store.ListIngestionRejectionsAsync(seed.Organization.CustomerOrganizationId),
            "codex-ingest-policy-context-001",
            "policy_context_missing");
        Assert.Equal(StatusCodes.Status403Forbidden, rejection.HttpStatus);
        Assert.Equal(auditEvent.AuditEventId, rejection.AuditEventId);
        if (!string.IsNullOrWhiteSpace(contentCaptureMode))
        {
            AssertRejectionMetadataDoesNotContain(rejection, contentCaptureMode);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2025-01-01")]
    [InlineData("not-a-date")]
    public async Task CodexOtlpEndpointRejectsUnsupportedSchemaVersion(string? schemaVersion)
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest("/v1/logs", issued.Secret);
        request.Headers.Remove("X-AITO-Schema-Version");

        if (schemaVersion is not null)
        {
            request.Headers.Add("X-AITO-Schema-Version", schemaVersion);
        }

        using var response = await client.SendAsync(request);

        await AssertOtlpRejectionAsync(response, HttpStatusCode.BadRequest, "unsupported_schema", issued.Secret);
        var auditEvent = Assert.Single(
            await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId),
            auditEvent => auditEvent.Action == ProductAuthorizationAction.TelemetryIngest &&
                auditEvent.EvidenceMetadata["operation"] == "ingestion_rejection" &&
                auditEvent.EvidenceMetadata["result"] == "unsupported_schema");

        var rejection = Assert.Single(await store.ListIngestionRejectionsAsync(seed.Organization.CustomerOrganizationId));
        Assert.Equal("unsupported_schema", rejection.ReasonCode);
        Assert.Equal(StatusCodes.Status400BadRequest, rejection.HttpStatus);
        Assert.Equal(auditEvent.AuditEventId, rejection.AuditEventId);
        Assert.Equal("logs", rejection.SignalType);
    }

    [Theory]
    [MemberData(nameof(OtlpSignalRoutes))]
    public async Task CodexOtlpEndpointRejectsMalformedBinaryPayloadWithoutPersistingContent(string path)
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest(path, issued.Secret, payload: [0x0A, 0x80]);
        request.Headers.Add("X-Correlation-Id", "codex-ingest-malformed-otlp-001");

        using var response = await client.SendAsync(request);

        await AssertOtlpRejectionAsync(response, HttpStatusCode.BadRequest, "malformed_otlp", issued.Secret);

        var auditEvent = Assert.Single(
            await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId),
            auditEvent => auditEvent.CorrelationId == "codex-ingest-malformed-otlp-001");
        Assert.Equal(ProductAuthorizationAction.TelemetryIngest, auditEvent.Action);
        Assert.Equal("malformed_otlp", auditEvent.EvidenceMetadata["result"]);
        Assert.DoesNotContain(auditEvent.EvidenceMetadata.Values, value => value.Contains("prompt", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(auditEvent.EvidenceMetadata.Values, value => value.Contains("command", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(auditEvent.EvidenceMetadata.Values, value => value.Contains("tool", StringComparison.OrdinalIgnoreCase));

        var rejection = AssertSingleRejection(
            await store.ListIngestionRejectionsAsync(seed.Organization.CustomerOrganizationId),
            "codex-ingest-malformed-otlp-001",
            "malformed_otlp",
            path);
        Assert.Equal(StatusCodes.Status400BadRequest, rejection.HttpStatus);
        Assert.Equal(auditEvent.AuditEventId, rejection.AuditEventId);
        AssertRejectionMetadataDoesNotContain(rejection, "0A80");
    }

    [Theory]
    [MemberData(nameof(OtlpSignalRoutes))]
    public async Task CodexOtlpEndpointRejectsMalformedNestedResourcePayload(string path)
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest(path, issued.Secret, payload: [0x0A, 0x02, 0xFF, 0xFF]);

        using var response = await client.SendAsync(request);

        await AssertOtlpRejectionAsync(response, HttpStatusCode.BadRequest, "malformed_otlp", issued.Secret);
    }

    [Theory]
    [MemberData(nameof(OtlpWrongSignalPayloads))]
    public async Task CodexOtlpEndpointRejectsPayloadsOnWrongSignalRoutes(
        string path,
        byte[] payload)
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest(path, issued.Secret, payload: payload);

        using var response = await client.SendAsync(request);

        await AssertOtlpRejectionAsync(response, HttpStatusCode.BadRequest, "malformed_otlp", issued.Secret);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task CodexOtlpEndpointRejectsAllZeroTraceIdentifiers(
        bool allZeroTraceId,
        bool allZeroSpanId)
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest(
            "/v1/traces",
            issued.Secret,
            payload: CreateTracePayload(allZeroTraceId, allZeroSpanId));

        using var response = await client.SendAsync(request);

        await AssertOtlpRejectionAsync(response, HttpStatusCode.BadRequest, "malformed_otlp", issued.Secret);
    }

    [Fact]
    public async Task CodexOtlpEndpointRejectsScopeOnlyLogsPayload()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest("/v1/logs", issued.Secret, payload: ScopeOnlyOtlpLogExportRequest);

        using var response = await client.SendAsync(request);

        await AssertOtlpRejectionAsync(response, HttpStatusCode.BadRequest, "malformed_otlp", issued.Secret);
    }

    [Fact]
    public async Task CodexOtlpEndpointRejectsEmptyFieldOneEnvelope()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest("/v1/logs", issued.Secret, payload: [0x0A, 0x00]);

        using var response = await client.SendAsync(request);

        await AssertOtlpRejectionAsync(response, HttpStatusCode.BadRequest, "malformed_otlp", issued.Secret);
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("text/plain")]
    [InlineData("application/x-protobuf-bad")]
    [InlineData("application/x-protobuf; charset=utf-8")]
    [InlineData("Application/X-Protobuf")]
    public async Task CodexOtlpEndpointRejectsUnsupportedContentTypes(string contentType)
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest("/v1/logs", issued.Secret, payload: [0x7B, 0x7D]);
        Assert.NotNull(request.Content);
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);

        using var response = await client.SendAsync(request);

        await AssertOtlpRejectionAsync(response, HttpStatusCode.UnsupportedMediaType, "unsupported_content_type", issued.Secret);
        Assert.Contains(
            await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId),
            auditEvent => auditEvent.Action == ProductAuthorizationAction.TelemetryIngest &&
                auditEvent.EvidenceMetadata["result"] == "unsupported_content_type");

        var rejection = Assert.Single(await store.ListIngestionRejectionsAsync(seed.Organization.CustomerOrganizationId));
        Assert.Equal("unsupported_content_type", rejection.ReasonCode);
        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, rejection.HttpStatus);
        Assert.Equal("logs", rejection.SignalType);
    }

    [Fact]
    public async Task CodexOtlpEndpointRejectsMissingContentType()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest("/v1/logs", issued.Secret);
        Assert.NotNull(request.Content);
        request.Content.Headers.ContentType = null;

        using var response = await client.SendAsync(request);

        await AssertOtlpRejectionAsync(response, HttpStatusCode.UnsupportedMediaType, "unsupported_content_type", issued.Secret);
    }

    [Fact]
    public async Task CodexOtlpEndpointRejectsOversizedPayloadBeforeParsing()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest("/v1/logs", issued.Secret, payload: new byte[(1024 * 1024) + 1]);

        using var response = await client.SendAsync(request);

        await AssertOtlpRejectionAsync(response, HttpStatusCode.RequestEntityTooLarge, "payload_too_large", issued.Secret);

        var rejection = Assert.Single(await store.ListIngestionRejectionsAsync(seed.Organization.CustomerOrganizationId));
        Assert.Equal("payload_too_large", rejection.ReasonCode);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, rejection.HttpStatus);
        Assert.Equal("logs", rejection.SignalType);
    }

    private static WebApplicationFactory<TokenObservabilityIngestionAssemblyMarker> CreateFactory(
        InMemoryTenantMetadataStore store,
        string? endpointRegion = null)
    {
        return new WebApplicationFactory<TokenObservabilityIngestionAssemblyMarker>()
            .WithWebHostBuilder(builder =>
            {
                if (endpointRegion is not null)
                {
                    builder.UseSetting("ProductIngestion:Region", endpointRegion);
                }

                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<InMemoryTenantMetadataStore>();
                    services.AddSingleton(store);
                });
            });
    }

    private static HttpRequestMessage CreateOtlpRequest(
        string path,
        string? secret,
        byte[]? payload = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-AITO-Harness", Harness);
        request.Headers.Add("X-AITO-Setup-Profile-Id", SetupProfileId);
        request.Headers.Add("X-AITO-Schema-Version", SchemaVersion);
        request.Headers.Add("X-AITO-Content-Capture-Mode", "metadata-only");

        if (!string.IsNullOrWhiteSpace(secret))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        }

        request.Content = new ByteArrayContent(payload ?? CreateValidPayload(path));
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(OtlpContentType);

        return request;
    }

    private static byte[] CreateValidPayload(string path)
    {
        return path switch
        {
            "/v1/logs" => MinimalOtlpLogExportRequest,
            "/v1/traces" => MinimalOtlpTraceExportRequest,
            "/v1/metrics" => MinimalOtlpMetricExportRequest,
            _ => throw new ArgumentOutOfRangeException(nameof(path), path, null)
        };
    }

    private static byte[] CreateTracePayload(bool allZeroTraceId, bool allZeroSpanId)
    {
        var payload = MinimalOtlpTraceExportRequest.ToArray();

        if (allZeroTraceId)
        {
            Array.Clear(payload, index: 8, length: 16);
        }

        if (allZeroSpanId)
        {
            Array.Clear(payload, index: 26, length: 8);
        }

        return payload;
    }

    private static byte[] CreateLogPayloadWithBody(string body)
    {
        using var anyValue = new MemoryStream();
        WriteLengthDelimitedField(anyValue, fieldNumber: 1, Encoding.UTF8.GetBytes(body));

        using var logRecord = new MemoryStream();
        WriteLengthDelimitedField(logRecord, fieldNumber: 3, Encoding.UTF8.GetBytes("info"));
        WriteLengthDelimitedField(logRecord, fieldNumber: 5, anyValue.ToArray());

        using var scopeLogs = new MemoryStream();
        WriteLengthDelimitedField(scopeLogs, fieldNumber: 2, logRecord.ToArray());

        using var resourceLogs = new MemoryStream();
        WriteLengthDelimitedField(resourceLogs, fieldNumber: 2, scopeLogs.ToArray());

        using var exportRequest = new MemoryStream();
        WriteLengthDelimitedField(exportRequest, fieldNumber: 1, resourceLogs.ToArray());
        return exportRequest.ToArray();
    }

    private static byte[] CreateMetricPayload(string metricName)
    {
        using var metric = new MemoryStream();
        WriteLengthDelimitedField(metric, fieldNumber: 1, Encoding.UTF8.GetBytes(metricName));
        WriteLengthDelimitedField(metric, fieldNumber: 5, []);

        using var scopeMetrics = new MemoryStream();
        WriteLengthDelimitedField(scopeMetrics, fieldNumber: 2, metric.ToArray());

        using var resourceMetrics = new MemoryStream();
        WriteLengthDelimitedField(resourceMetrics, fieldNumber: 2, scopeMetrics.ToArray());

        using var exportRequest = new MemoryStream();
        WriteLengthDelimitedField(exportRequest, fieldNumber: 1, resourceMetrics.ToArray());
        return exportRequest.ToArray();
    }

    private static byte[] CreateMetricPayloadWithIntData(string metricName, uint value)
    {
        using var numberDataPoint = new MemoryStream();
        WriteVarintField(numberDataPoint, fieldNumber: 6, value);

        using var sum = new MemoryStream();
        WriteLengthDelimitedField(sum, fieldNumber: 1, numberDataPoint.ToArray());

        using var metric = new MemoryStream();
        WriteLengthDelimitedField(metric, fieldNumber: 1, Encoding.UTF8.GetBytes(metricName));
        WriteLengthDelimitedField(metric, fieldNumber: 5, sum.ToArray());

        using var scopeMetrics = new MemoryStream();
        WriteLengthDelimitedField(scopeMetrics, fieldNumber: 2, metric.ToArray());

        using var resourceMetrics = new MemoryStream();
        WriteLengthDelimitedField(resourceMetrics, fieldNumber: 2, scopeMetrics.ToArray());

        using var exportRequest = new MemoryStream();
        WriteLengthDelimitedField(exportRequest, fieldNumber: 1, resourceMetrics.ToArray());
        return exportRequest.ToArray();
    }

    private static void WriteVarintField(Stream stream, int fieldNumber, uint value)
    {
        WriteVarint(stream, (uint)(fieldNumber << 3));
        WriteVarint(stream, value);
    }

    private static void WriteLengthDelimitedField(Stream stream, int fieldNumber, byte[] value)
    {
        WriteVarint(stream, (uint)((fieldNumber << 3) | 2));
        WriteVarint(stream, (uint)value.Length);
        stream.Write(value);
    }

    private static void WriteVarint(Stream stream, uint value)
    {
        while (value > 0x7F)
        {
            stream.WriteByte((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }

        stream.WriteByte((byte)value);
    }

    private static async Task<IssuedScopedIngestionCredential> IssueCredentialAsync(
        InMemoryTenantMetadataStore store,
        TenantSeed seed)
    {
        var lifecycle = new ScopedIngestionCredentialLifecycleService(store, new StaticTenantMetadataClock(Now));
        var admin = await CreateProductUserAsync(store, seed, "admin-subject");
        var developer = await CreateProductUserAsync(store, seed, "developer-subject");

        return await lifecycle.CreateAsync(
            seed.Organization.CustomerOrganizationId,
            new IssueScopedIngestionCredentialRequest(
                HarnessSetupProfileId: SetupProfileId,
                ProductUserId: developer.ProductUserId,
                AllowedHarness: CodingAgentHarness.CodexCli,
                AllowedScopes: [new ProductScope(ProductScopeKind.Organization, ScopeId: null)],
                ExpiresAtUtc: Now.AddDays(30),
                CreatedByProductUserId: admin.ProductUserId,
                ActorEffectiveRole: ProductRole.PlatformAdmin,
                CorrelationId: $"credential-create-{Guid.NewGuid():N}",
                AuditEventId: $"audit-credential-create-{Guid.NewGuid():N}"));
    }

    private static Task SetCredentialStatusAsync(
        InMemoryTenantMetadataStore store,
        TenantSeed seed,
        ScopedIngestionCredential credential,
        ScopedIngestionCredentialStatus status)
    {
        var request = new ScopedIngestionCredentialLifecycleRequest(
            ChangedByProductUserId: credential.CreatedByProductUserId,
            ActorEffectiveRole: ProductRole.PlatformAdmin,
            CorrelationId: $"credential-status-{Guid.NewGuid():N}",
            AuditEventId: $"audit-credential-status-{Guid.NewGuid():N}");

        return status switch
        {
            ScopedIngestionCredentialStatus.Disabled => store.DisableScopedIngestionCredentialAsync(
                seed.Organization.CustomerOrganizationId,
                credential.ScopedIngestionCredentialId,
                request),
            ScopedIngestionCredentialStatus.Revoked => store.RevokeScopedIngestionCredentialAsync(
                seed.Organization.CustomerOrganizationId,
                credential.ScopedIngestionCredentialId,
                request),
            ScopedIngestionCredentialStatus.Expired => store.MarkScopedIngestionCredentialExpiredAsync(
                seed.Organization.CustomerOrganizationId,
                credential.ScopedIngestionCredentialId,
                request),
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
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

    private static async Task AssertOtlpRejectionAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode,
        string? forbiddenText = null)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(OtlpContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.TryGetValues("X-AITO-Rejection-Code", out var codes));
        Assert.Equal(expectedCode, Assert.Single(codes));

        var responseBody = await response.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(responseBody);

        if (forbiddenText is not null)
        {
            Assert.DoesNotContain(forbiddenText, Convert.ToBase64String(responseBody), StringComparison.Ordinal);
        }
    }

    private static IngestionRejectionRecord AssertSingleRejection(
        IReadOnlyList<IngestionRejectionRecord> rejections,
        string correlationId,
        string reasonCode,
        string expectedRoute = "/v1/logs")
    {
        var rejection = Assert.Single(
            rejections,
            rejection => rejection.CorrelationId == correlationId);
        Assert.Equal(reasonCode, rejection.ReasonCode);
        Assert.Equal(correlationId, rejection.CorrelationId);
        Assert.Equal(reasonCode, rejection.EvidenceMetadata["result"]);
        Assert.Equal("ingestion_rejection", rejection.EvidenceMetadata["operation"]);
        Assert.Equal(expectedRoute, rejection.RequestRoute);
        return rejection;
    }

    private static void AssertRejectionMetadataDoesNotContain(
        IngestionRejectionRecord rejection,
        string forbiddenText)
    {
        Assert.DoesNotContain(forbiddenText, rejection.HarnessSetupProfileId ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(forbiddenText, rejection.DeclaredHarness ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(forbiddenText, rejection.SignalType, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(forbiddenText, rejection.RequestRoute, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(forbiddenText, rejection.ReasonCode, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(forbiddenText, rejection.CorrelationId, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            rejection.EvidenceMetadata,
            item => item.Key.Contains(forbiddenText, StringComparison.OrdinalIgnoreCase) ||
                item.Value.Contains(forbiddenText, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record TenantSeed(CustomerOrganization Organization, IdentityTenant IdentityTenant);
}
