using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
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
    public async Task AcceptedCodexTelemetryCreatesNormalizedEnvelopeAndSessionRecord()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest("/v1/logs", issued.Secret);
        request.Headers.Add("X-Correlation-Id", "codex-session-correlation-001");
        request.Headers.Add("X-AITO-Conversation-Id", "codex-conversation-001");
        request.Headers.Add("X-AITO-Turn-Id", "turn-001");
        request.Headers.Add("X-AITO-Source-Event-Id", "event-001");
        request.Headers.Add("X-AITO-Source-Event-Name", "codex.conversation_starts");
        request.Headers.Add("X-AITO-Source-Event-Timestamp", "2026-06-17T11:15:00Z");
        request.Headers.Add("X-AITO-Model", "gpt-5-codex");
        request.Headers.Add("X-AITO-Harness-Version", "codex-cli/1.2.3");
        request.Headers.Add("X-AITO-Sandbox", "workspace-write");
        request.Headers.Add("X-AITO-Approval-Policy", "on-request");
        request.Headers.Add("X-AITO-Harness-User", "developer@example.test");
        request.Headers.Add("X-AITO-Repository-Evidence-State", "observed");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = Assert.Single(await store.ListTelemetryEnvelopesAsync(seed.Organization.CustomerOrganizationId));
        Assert.Equal(seed.Organization.CustomerOrganizationId, envelope.CustomerOrganizationId);
        Assert.Equal(SetupProfileId, envelope.HarnessSetupProfileId);
        Assert.Equal(issued.Credential.ScopedIngestionCredentialId, envelope.ScopedIngestionCredentialId);
        Assert.Equal(issued.Credential.ProductUserId, envelope.ProductUserId);
        Assert.Equal("codex-cli", envelope.Harness);
        Assert.Equal(SchemaVersion, envelope.SchemaVersion);
        Assert.Equal("log", envelope.SignalType);
        Assert.Equal("codex.conversation_starts", envelope.SourceEventName);
        Assert.Equal(new DateTimeOffset(2026, 6, 17, 11, 15, 0, TimeSpan.Zero), envelope.SourceEventTimestampUtc);
        Assert.NotNull(envelope.ConversationIdHash);
        Assert.NotNull(envelope.TurnIdHash);
        Assert.Equal("gpt-5-codex", envelope.ModelName);
        Assert.Equal("codex-cli/1.2.3", envelope.HarnessVersion);
        Assert.Equal("metadata_only", envelope.ContentPolicyDecision);
        Assert.Equal("metadata_only", envelope.ContentCaptureState);
        Assert.Equal("not_required", envelope.RedactionState);
        Assert.Equal("observed", envelope.EvidenceState);
        Assert.Equal("unavailable", envelope.MetricState);
        Assert.Equal("harness_emitted", envelope.SourceEvidenceKind);
        Assert.Equal("codex-session-correlation-001", envelope.CorrelationId);
        Assert.Equal("accepted", envelope.RoutingDecision["result"]);
        Assert.Equal("metadata_only", envelope.RoutingDecision["content_capture"]);
        Assert.Equal(SchemaVersion, envelope.IngestionVersionMetadata["schema_version"]);
        Assert.Equal("codex-cli/1.2.3", envelope.IngestionVersionMetadata["harness_version"]);
        Assert.DoesNotContain("codex-conversation-001", envelope.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("developer@example.test", envelope.ToString(), StringComparison.Ordinal);

        var session = Assert.Single(await store.ListAgentSessionsAsync(seed.Organization.CustomerOrganizationId));
        Assert.Equal(seed.Organization.CustomerOrganizationId, session.CustomerOrganizationId);
        Assert.Equal(issued.Credential.ProductUserId, session.ProductUserId);
        Assert.Equal(SetupProfileId, session.HarnessSetupProfileId);
        Assert.Equal("codex-cli", session.Harness);
        Assert.Equal(envelope.ConversationIdHash, session.ProviderSessionIdHash);
        Assert.Equal(envelope.SourceEventTimestampUtc, session.StartedAtUtc);
        Assert.Equal(envelope.SourceEventTimestampUtc, session.EndedAtUtc);
        Assert.Equal("active", session.SessionStatus);
        Assert.Equal("workspace-write", session.SandboxSetting);
        Assert.Equal("on-request", session.ApprovalSetting);
        Assert.Equal("observed", session.RepositoryEvidenceState);
        Assert.Equal("metadata_only", session.ContentCaptureSummary);
        Assert.Equal("not_started", session.RecommendationStatus);
        Assert.Equal("gpt-5-codex", Assert.Single(session.ModelNames));
        Assert.Equal(envelope.TelemetryEnvelopeId, Assert.Single(session.SourceTelemetryEnvelopeIds));
    }

    [Theory]
    [InlineData("/v1/logs", "log")]
    [InlineData("/v1/traces", "trace")]
    public async Task AcceptedCodexTelemetryRoutesSafeDiagnosticEventWithAggregateCorrelation(
        string path,
        string expectedSignalType)
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var diagnostics = new RecordingIngestionDiagnosticSink();
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store, diagnostics);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest(path, issued.Secret);
        AddSessionHeaders(request, "event-diagnostic-001", "2026-06-17T11:15:00Z", "gpt-5-codex");
        request.Headers.Remove("X-Correlation-Id");
        request.Headers.Add("X-Correlation-Id", "codex-diagnostic-correlation-001");
        request.Headers.Add("X-AITO-Raw-Prompt", "summarize the secret prompt");
        request.Headers.Add("X-AITO-Command-Output", "secret command output");
        request.Headers.Add("X-AITO-Tool-Result", "tool result with file content");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = Assert.Single(await store.ListTelemetryEnvelopesAsync(seed.Organization.CustomerOrganizationId));
        var session = Assert.Single(await store.ListAgentSessionsAsync(seed.Organization.CustomerOrganizationId));
        var diagnosticEvent = Assert.Single(diagnostics.Events);
        Assert.Equal("tokenobs.ingestion.request", diagnosticEvent.OperationName);
        Assert.Equal(expectedSignalType, diagnosticEvent.SignalType);
        Assert.Equal("accepted", diagnosticEvent.Outcome);
        Assert.Null(diagnosticEvent.RejectionReason);
        Assert.Equal(StatusCodes.Status200OK, diagnosticEvent.HttpStatus);
        Assert.Equal(path, diagnosticEvent.RequestRoute);
        Assert.Equal("codex-diagnostic-correlation-001", diagnosticEvent.CorrelationId);
        Assert.Equal(seed.Organization.CustomerOrganizationId, diagnosticEvent.CustomerOrganizationId);
        Assert.Equal("contoso", diagnosticEvent.CustomerOrganizationSlug);
        Assert.Equal("codex-cli", diagnosticEvent.Harness);
        Assert.Equal(SetupProfileId, diagnosticEvent.HarnessSetupProfileId);
        Assert.Equal("application_insights_log_analytics", diagnosticEvent.DiagnosticStore);
        Assert.Equal("metadata_only", diagnosticEvent.ContentCaptureState);
        Assert.Null(diagnosticEvent.AuditEventId);
        Assert.Equal(envelope.TelemetryEnvelopeId, diagnosticEvent.TelemetryEnvelopeId);
        Assert.Equal(session.AgentSessionId, diagnosticEvent.AgentSessionId);
        Assert.Equal("succeeded", diagnosticEvent.AggregateMetricExportOutcome);
        Assert.Equal(6, diagnosticEvent.AggregateMetricPointCount);
        Assert.Null(diagnosticEvent.AggregateMetricExportFailureReason);
        Assert.Equal("metadata_only", diagnosticEvent.Properties["content_capture"]);
        Assert.Equal("application_insights", diagnosticEvent.Properties["diagnostic_store"]);
        Assert.DoesNotContain("event-diagnostic-001", diagnosticEvent.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("codex-conversation-001", diagnosticEvent.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("summarize the secret prompt", diagnosticEvent.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret command output", diagnosticEvent.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tool result with file content", diagnosticEvent.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/v1/logs", "log")]
    [InlineData("/v1/traces", "trace")]
    public async Task RejectedCodexTelemetryRoutesSafeDiagnosticEventWithAuditCorrelation(
        string path,
        string expectedSignalType)
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var diagnostics = new RecordingIngestionDiagnosticSink();
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store, diagnostics);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest(path, issued.Secret);
        request.Headers.Remove("X-AITO-Content-Capture-Mode");
        request.Headers.Add("X-AITO-Content-Capture-Mode", "capture_candidate");
        request.Headers.Add("X-Correlation-Id", "codex-diagnostic-rejection-001");
        request.Headers.Add("X-AITO-Raw-Prompt", "prompt text must not be logged");

        using var response = await client.SendAsync(request);

        await AssertOtlpRejectionAsync(response, HttpStatusCode.Forbidden, "policy_context_missing", "capture_candidate");
        var rejection = Assert.Single(await store.ListIngestionRejectionsAsync(seed.Organization.CustomerOrganizationId));
        var auditEvent = Assert.Single(
            await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId),
            auditEvent => auditEvent.Action == ProductAuthorizationAction.TelemetryIngest);
        var diagnosticEvent = Assert.Single(diagnostics.Events);
        Assert.Equal("tokenobs.ingestion.request", diagnosticEvent.OperationName);
        Assert.Equal(expectedSignalType, diagnosticEvent.SignalType);
        Assert.Equal("rejected", diagnosticEvent.Outcome);
        Assert.Equal("policy_context_missing", diagnosticEvent.RejectionReason);
        Assert.Equal(StatusCodes.Status403Forbidden, diagnosticEvent.HttpStatus);
        Assert.Equal(path, diagnosticEvent.RequestRoute);
        Assert.Equal("codex-diagnostic-rejection-001", diagnosticEvent.CorrelationId);
        Assert.Equal(seed.Organization.CustomerOrganizationId, diagnosticEvent.CustomerOrganizationId);
        Assert.Equal("contoso", diagnosticEvent.CustomerOrganizationSlug);
        Assert.Equal("codex-cli", diagnosticEvent.Harness);
        Assert.Equal(SetupProfileId, diagnosticEvent.HarnessSetupProfileId);
        Assert.Equal("application_insights_log_analytics", diagnosticEvent.DiagnosticStore);
        Assert.Equal("metadata_only", diagnosticEvent.ContentCaptureState);
        Assert.Equal(auditEvent.AuditEventId, diagnosticEvent.AuditEventId);
        Assert.Null(diagnosticEvent.TelemetryEnvelopeId);
        Assert.Null(diagnosticEvent.AgentSessionId);
        Assert.Equal("not_applicable", diagnosticEvent.AggregateMetricExportOutcome);
        Assert.Null(diagnosticEvent.AggregateMetricPointCount);
        Assert.Equal("policy_context_missing", diagnosticEvent.Properties["rejection_reason"]);
        Assert.Equal(rejection.IngestionRejectionId.ToString(), diagnosticEvent.Properties["ingestion_rejection_id"]);
        Assert.DoesNotContain("capture_candidate", diagnosticEvent.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prompt text must not be logged", diagnosticEvent.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(issued.Secret, diagnosticEvent.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void IngestionServiceGraphRoutesDiagnosticsThroughStructuredLoggerByDefault()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        using var factory = CreateFactory(store);

        var sink = factory.Services.GetRequiredService<IIngestionDiagnosticSink>();

        Assert.IsType<IngestionDiagnosticLoggerSink>(sink);
    }

    [Fact]
    public void IngestionServiceGraphConfiguresOpenTelemetryForAzureMonitorExport()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        using var factory = CreateFactory(
            store,
            azureMonitorConnectionString: "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://example.invalid/");

        var hostedServices = factory.Services.GetServices<IHostedService>();

        Assert.Contains(hostedServices, service =>
            service.GetType().Assembly.GetName().Name?.Contains("OpenTelemetry", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task IngestionDiagnosticLoggerSinkEmitsTraceActivityWithSafeTags()
    {
        var capturedActivities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == IngestionDiagnosticLoggerSink.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => capturedActivities.Add(activity)
        };
        ActivitySource.AddActivityListener(listener);
        var diagnosticEvent = new IngestionDiagnosticEvent(
            OperationName: "tokenobs.ingestion.request",
            SignalType: "log",
            Outcome: "rejected",
            RejectionReason: "policy_context_missing",
            HttpStatus: StatusCodes.Status403Forbidden,
            RequestRoute: "/v1/logs",
            CorrelationId: "codex-trace-correlation-001",
            CustomerOrganizationId: CustomerOrganizationId.NewId(),
            CustomerOrganizationSlug: "contoso",
            Harness: "codex-cli",
            HarnessSetupProfileId: SetupProfileId,
            DiagnosticStore: "application_insights_log_analytics",
            ContentCaptureState: "metadata_only",
            AuditEventId: "audit-event-001",
            TelemetryEnvelopeId: null,
            AgentSessionId: null,
            AggregateMetricExportOutcome: "not_applicable",
            AggregateMetricPointCount: null,
            AggregateMetricExportFailureReason: null,
            new Dictionary<string, string>
            {
                ["rejection_reason"] = "policy_context_missing",
                ["content_capture"] = "metadata_only"
            });
        var sink = new IngestionDiagnosticLoggerSink(NullLogger<IngestionDiagnosticLoggerSink>.Instance);

        await sink.RouteAsync(diagnosticEvent);

        var activity = Assert.Single(capturedActivities);
        Assert.Equal("tokenobs.ingestion.request", activity.OperationName);
        Assert.Equal("codex-trace-correlation-001", activity.GetTagItem("tokenobs.correlation_id"));
        Assert.Equal("rejected", activity.GetTagItem("tokenobs.ingestion.outcome"));
        Assert.Equal("policy_context_missing", activity.GetTagItem("tokenobs.ingestion.rejection_reason"));
        Assert.Equal("contoso", activity.GetTagItem("tokenobs.customer_organization_slug"));
        Assert.Equal("codex-cli", activity.GetTagItem("tokenobs.harness"));
        Assert.Equal("metadata_only", activity.GetTagItem("tokenobs.content_capture_state"));
        Assert.Equal("application_insights_log_analytics", activity.GetTagItem("tokenobs.diagnostic_store"));
        Assert.DoesNotContain("prompt", activity.Tags.Select(tag => tag.Value), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("command output", activity.Tags.Select(tag => tag.Value), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("tool result", activity.Tags.Select(tag => tag.Value), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TokenObservationStorePreservesMetricStatesNullsAndTrueZeroes()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var productUser = await CreateProductUserAsync(store, seed, "developer-subject");
        var session = await store.UpsertAgentSessionAsync(new CreateAgentSessionRecordRequest(
            seed.Organization.CustomerOrganizationId,
            productUser.ProductUserId,
            SetupProfileId,
            CodingAgentHarness.CodexCli,
            ProviderSessionIdHash: null,
            StartedAtUtc: Now,
            EndedAtUtc: null,
            AgentSessionStatus.Active,
            RepositoryEvidenceState.Unavailable,
            ContentCaptureSummary.MetadataOnly,
            RecommendationStatus.NotStarted,
            TokenMetricStatus.Mixed,
            TokenMetricConfidence.Estimated));

        await store.RecordTokenObservationAsync(new CreateTokenObservationRecordRequest(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            ModelInvocationId: null,
            TokenMetricName.InputTokens,
            Value: 0,
            TokenMetricStatus.Observed,
            TokenMetricConfidence.Observed,
            TokenObservationSourceKind.CodexEvent,
            SourceTelemetryEnvelopeId: null));
        await store.RecordTokenObservationAsync(new CreateTokenObservationRecordRequest(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            ModelInvocationId: null,
            TokenMetricName.OutputTokens,
            Value: 25,
            TokenMetricStatus.Estimated,
            TokenMetricConfidence.Estimated,
            TokenObservationSourceKind.Estimator,
            SourceTelemetryEnvelopeId: null));
        await store.RecordTokenObservationAsync(new CreateTokenObservationRecordRequest(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            ModelInvocationId: null,
            TokenMetricName.CachedInputTokens,
            Value: null,
            TokenMetricStatus.Unavailable,
            TokenMetricConfidence.Unavailable,
            TokenObservationSourceKind.Missing,
            SourceTelemetryEnvelopeId: null));
        await store.RecordTokenObservationAsync(new CreateTokenObservationRecordRequest(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            ModelInvocationId: null,
            TokenMetricName.ReasoningOutputTokens,
            Value: null,
            TokenMetricStatus.NotApplicable,
            TokenMetricConfidence.Unavailable,
            TokenObservationSourceKind.Missing,
            SourceTelemetryEnvelopeId: null));
        await store.RecordTokenObservationAsync(new CreateTokenObservationRecordRequest(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            ModelInvocationId: "summary-only-001",
            TokenMetricName.TotalTokens,
            Value: 25,
            TokenMetricStatus.Mixed,
            TokenMetricConfidence.Estimated,
            TokenObservationSourceKind.DerivedSummary,
            SourceTelemetryEnvelopeId: null));

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
                Assert.Equal(TokenMetricStatus.Mixed, observation.MetricStatus);
            });

        await Assert.ThrowsAsync<ArgumentException>(() => store.RecordTokenObservationAsync(
            new CreateTokenObservationRecordRequest(
                seed.Organization.CustomerOrganizationId,
                session.AgentSessionId,
                ModelInvocationId: null,
                TokenMetricName.InputTokens,
                Value: 1,
                TokenMetricStatus.Unavailable,
                TokenMetricConfidence.Unavailable,
                TokenObservationSourceKind.Missing,
                SourceTelemetryEnvelopeId: null)));
        await Assert.ThrowsAsync<ArgumentException>(() => store.RecordTokenObservationAsync(
            new CreateTokenObservationRecordRequest(
                seed.Organization.CustomerOrganizationId,
                session.AgentSessionId,
                ModelInvocationId: null,
                TokenMetricName.InputTokens,
                Value: 0,
                TokenMetricStatus.Estimated,
                TokenMetricConfidence.Estimated,
                TokenObservationSourceKind.Estimator,
                SourceTelemetryEnvelopeId: null)));
    }

    [Fact]
    public async Task AcceptedCodexTelemetryExportsAggregateMetricPointsOnIngestionPath()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest("/v1/logs", issued.Secret);
        AddSessionHeaders(request, "event-aggregate-001", "2026-06-17T11:15:00Z", "gpt-5-codex");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var persistedPoints = await store.ListAggregateMetricPointsAsync(seed.Organization.CustomerOrganizationId);
        var sessionPoint = Assert.Single(persistedPoints, point => point.Name == "tokenobs_sessions_started_total");
        Assert.Equal(1, sessionPoint.Value);
        Assert.Equal("contoso", sessionPoint.Labels["customer_organization_slug"]);
        Assert.Equal("dv", sessionPoint.Labels["environment"]);
        Assert.Equal("eastus2", sessionPoint.Labels["region"]);
        Assert.Equal("codex", sessionPoint.Labels["harness"]);
        Assert.Equal(5, persistedPoints.Count(point => point.Name == "tokenobs_token_metric_states_total"));
        Assert.DoesNotContain(persistedPoints, point => point.Name == "tokenobs_tokens_total");
        Assert.All(persistedPoints, point =>
        {
            AssertForbiddenAggregateLabelsAreAbsent(point.Labels);
            Assert.DoesNotContain("event-aggregate-001", point.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("codex-conversation-001", point.ToString(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task AggregateMetricsExporterExportsTenantScopedPointsFromAcceptedSession()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        var envelope = await RecordAcceptedEnvelopeAsync(
            store,
            issued.Credential,
            new string('c', 64),
            "accepted-session-correlation-002",
            modelName: "gpt-5-codex");
        var session = Assert.Single(await store.ListAgentSessionsAsync(seed.Organization.CustomerOrganizationId));
        await store.RecordTokenObservationAsync(new CreateTokenObservationRecordRequest(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            ModelInvocationId: null,
            TokenMetricName.InputTokens,
            Value: 0,
            TokenMetricStatus.Observed,
            TokenMetricConfidence.Observed,
            TokenObservationSourceKind.CodexEvent,
            envelope.TelemetryEnvelopeId));
        await store.RecordTokenObservationAsync(new CreateTokenObservationRecordRequest(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            ModelInvocationId: null,
            TokenMetricName.OutputTokens,
            Value: 25,
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
            ModelInvocationId: "summary-only-001",
            TokenMetricName.TotalTokens,
            Value: 25,
            TokenMetricStatus.Mixed,
            TokenMetricConfidence.Estimated,
            TokenObservationSourceKind.DerivedSummary,
            envelope.TelemetryEnvelopeId));
        var sink = new RecordingAggregateMetricSink();
        var exporter = new AggregateMetricsExporter(
            store,
            sink,
            new AggregateMetricsExportOptions(Environment: "dv"));

        var result = await exporter.ExportAcceptedSessionAsync(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            "aggregate-export-correlation-001");

        Assert.True(result.Succeeded);
        Assert.Equal(9, result.ExportedPointCount);
        Assert.Empty(await store.ListAggregateMetricExportFailuresAsync(seed.Organization.CustomerOrganizationId));
        var persistedPoints = await store.ListAggregateMetricPointsAsync(seed.Organization.CustomerOrganizationId);
        Assert.Equal(persistedPoints.Count, sink.Points.Count);

        var sessionPoint = Assert.Single(persistedPoints, point => point.Name == "tokenobs_sessions_started_total");
        Assert.Equal(1, sessionPoint.Value);
        Assert.Equal("sessions", sessionPoint.Unit);
        Assert.Equal("contoso", sessionPoint.Labels["customer_organization_slug"]);
        Assert.Equal("dv", sessionPoint.Labels["environment"]);
        Assert.Equal("eastus2", sessionPoint.Labels["region"]);
        Assert.Equal("codex", sessionPoint.Labels["harness"]);

        var tokenPoints = persistedPoints
            .Where(point => point.Name == "tokenobs_tokens_total")
            .OrderBy(point => point.Labels["token_type"], StringComparer.Ordinal)
            .ToArray();

        Assert.Collection(
            tokenPoints,
            point =>
            {
                Assert.Equal("input", point.Labels["token_type"]);
                Assert.Equal(0, point.Value);
                Assert.Equal("observed", point.Labels["metric_status"]);
                Assert.Equal("observed", point.Labels["metric_confidence"]);
            },
            point =>
            {
                Assert.Equal("output", point.Labels["token_type"]);
                Assert.Equal(25, point.Value);
                Assert.Equal("estimated", point.Labels["metric_status"]);
                Assert.Equal("estimated", point.Labels["metric_confidence"]);
            },
            point =>
            {
                Assert.Equal("total", point.Labels["token_type"]);
                Assert.Equal(25, point.Value);
                Assert.Equal("mixed", point.Labels["metric_status"]);
                Assert.Equal("estimated", point.Labels["metric_confidence"]);
            });

        var statePoints = persistedPoints
            .Where(point => point.Name == "tokenobs_token_metric_states_total")
            .OrderBy(point => point.Labels["token_type"], StringComparer.Ordinal)
            .ToArray();

        Assert.Collection(
            statePoints,
            point =>
            {
                Assert.Equal("cached_input", point.Labels["token_type"]);
                Assert.Equal("unavailable", point.Labels["metric_status"]);
                Assert.Equal("unavailable", point.Labels["metric_confidence"]);
            },
            point =>
            {
                Assert.Equal("input", point.Labels["token_type"]);
                Assert.Equal("observed", point.Labels["metric_status"]);
                Assert.Equal("observed", point.Labels["metric_confidence"]);
            },
            point =>
            {
                Assert.Equal("output", point.Labels["token_type"]);
                Assert.Equal("estimated", point.Labels["metric_status"]);
                Assert.Equal("estimated", point.Labels["metric_confidence"]);
            },
            point =>
            {
                Assert.Equal("reasoning_output", point.Labels["token_type"]);
                Assert.Equal("not_applicable", point.Labels["metric_status"]);
                Assert.Equal("unavailable", point.Labels["metric_confidence"]);
            },
            point =>
            {
                Assert.Equal("total", point.Labels["token_type"]);
                Assert.Equal("mixed", point.Labels["metric_status"]);
                Assert.Equal("estimated", point.Labels["metric_confidence"]);
            });

        Assert.All(persistedPoints, point =>
        {
            AssertForbiddenAggregateLabelsAreAbsent(point.Labels);
            Assert.Equal("openai", point.Labels.GetValueOrDefault("model_provider", "openai"));
            Assert.DoesNotContain("event-aggregate-001", point.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("codex-conversation-001", point.ToString(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task AggregateMetricsExporterRecordsBoundedFailureWithoutLosingAcceptedSession()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        var envelope = await RecordAcceptedEnvelopeAsync(
            store,
            issued.Credential,
            new string('b', 64),
            "accepted-session-correlation-001");
        var session = Assert.Single(await store.ListAgentSessionsAsync(seed.Organization.CustomerOrganizationId));
        await store.RecordTokenObservationAsync(new CreateTokenObservationRecordRequest(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            ModelInvocationId: null,
            TokenMetricName.InputTokens,
            Value: 10,
            TokenMetricStatus.Observed,
            TokenMetricConfidence.Observed,
            TokenObservationSourceKind.CodexEvent,
            envelope.TelemetryEnvelopeId));
        var exporter = new AggregateMetricsExporter(
            store,
            new ThrowingAggregateMetricSink(),
            new AggregateMetricsExportOptions(Environment: "dv"));

        var result = await exporter.ExportAcceptedSessionAsync(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            "aggregate-export-failure-correlation-001");

        Assert.False(result.Succeeded);
        Assert.Equal("sink_failure", result.FailureReason);
        Assert.Single(await store.ListAgentSessionsAsync(seed.Organization.CustomerOrganizationId));
        Assert.Empty(await store.ListAggregateMetricPointsAsync(seed.Organization.CustomerOrganizationId));
        var failure = Assert.Single(await store.ListAggregateMetricExportFailuresAsync(seed.Organization.CustomerOrganizationId));
        Assert.Equal(session.AgentSessionId, failure.AgentSessionId);
        Assert.Equal("sink_failure", failure.FailureReason);
        Assert.Equal("aggregate-export-failure-correlation-001", failure.CorrelationId);
        Assert.DoesNotContain("source event", failure.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AggregateMetricsExporterValidatesMetricShapeBeforeSinkExport()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        var envelope = await RecordAcceptedEnvelopeAsync(
            store,
            issued.Credential,
            new string('d', 64),
            "accepted-session-correlation-003",
            modelName: "sk-secret-model");
        var session = Assert.Single(await store.ListAgentSessionsAsync(seed.Organization.CustomerOrganizationId));
        await store.RecordTokenObservationAsync(new CreateTokenObservationRecordRequest(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            ModelInvocationId: null,
            TokenMetricName.InputTokens,
            Value: 10,
            TokenMetricStatus.Observed,
            TokenMetricConfidence.Observed,
            TokenObservationSourceKind.CodexEvent,
            envelope.TelemetryEnvelopeId));
        var sink = new RecordingAggregateMetricSink();
        var exporter = new AggregateMetricsExporter(
            store,
            sink,
            new AggregateMetricsExportOptions(Environment: "dv"));

        var result = await exporter.ExportAcceptedSessionAsync(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            "aggregate-export-invalid-shape-correlation-001");

        Assert.False(result.Succeeded);
        Assert.Equal("invalid_metric_shape", result.FailureReason);
        Assert.Empty(sink.Points);
        Assert.Empty(await store.ListAggregateMetricPointsAsync(seed.Organization.CustomerOrganizationId));
        var failure = Assert.Single(await store.ListAggregateMetricExportFailuresAsync(seed.Organization.CustomerOrganizationId));
        Assert.Equal("invalid_metric_shape", failure.FailureReason);
        Assert.Equal("aggregate-export-invalid-shape-correlation-001", failure.CorrelationId);
        Assert.DoesNotContain("sk-secret-model", failure.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TelemetryEnvelopeDedupeIsScopedPerCustomerOrganization()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var contoso = await CreateTenantAsync(store);
        var fabrikam = await CreateTenantAsync(store, slug: "fabrikam", externalTenantId: "fabrikam-tenant");
        var contosoCredential = await IssueCredentialAsync(store, contoso);
        var fabrikamCredential = await IssueCredentialAsync(store, fabrikam);
        var sharedDedupeHash = new string('a', 64);

        var contosoEnvelope = await RecordAcceptedEnvelopeAsync(
            store,
            contosoCredential.Credential,
            sharedDedupeHash,
            "contoso-correlation-001");
        var fabrikamEnvelope = await RecordAcceptedEnvelopeAsync(
            store,
            fabrikamCredential.Credential,
            sharedDedupeHash,
            "fabrikam-correlation-001");

        Assert.NotEqual(contosoEnvelope.TelemetryEnvelopeId, fabrikamEnvelope.TelemetryEnvelopeId);
        Assert.Equal(contoso.Organization.CustomerOrganizationId, contosoEnvelope.CustomerOrganizationId);
        Assert.Equal(fabrikam.Organization.CustomerOrganizationId, fabrikamEnvelope.CustomerOrganizationId);
        Assert.Same(contosoEnvelope, Assert.Single(await store.ListTelemetryEnvelopesAsync(contoso.Organization.CustomerOrganizationId)));
        Assert.Same(fabrikamEnvelope, Assert.Single(await store.ListTelemetryEnvelopesAsync(fabrikam.Organization.CustomerOrganizationId)));
    }

    [Fact]
    public async Task AcceptedCodexTelemetryUpdatesExistingSessionAndDeduplicatesEnvelope()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        using (var first = CreateOtlpRequest("/v1/logs", issued.Secret))
        {
            AddSessionHeaders(first, "event-001", "2026-06-17T11:15:00Z", "gpt-5-codex");
            using var firstResponse = await client.SendAsync(first);
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        }

        using (var duplicate = CreateOtlpRequest("/v1/logs", issued.Secret))
        {
            AddSessionHeaders(duplicate, "event-001", "2026-06-17T11:15:00Z", "gpt-5-codex");
            using var duplicateResponse = await client.SendAsync(duplicate);
            Assert.Equal(HttpStatusCode.OK, duplicateResponse.StatusCode);
        }

        using (var update = CreateOtlpRequest("/v1/logs", issued.Secret))
        {
            AddSessionHeaders(update, "event-002", "2026-06-17T11:25:00Z", "gpt-5.1-codex");
            using var updateResponse = await client.SendAsync(update);
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        }

        var envelopes = await store.ListTelemetryEnvelopesAsync(seed.Organization.CustomerOrganizationId);
        var session = Assert.Single(await store.ListAgentSessionsAsync(seed.Organization.CustomerOrganizationId));

        Assert.Equal(2, envelopes.Count);
        Assert.Equal(new DateTimeOffset(2026, 6, 17, 11, 15, 0, TimeSpan.Zero), session.StartedAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 6, 17, 11, 25, 0, TimeSpan.Zero), session.EndedAtUtc);
        Assert.Equal(["gpt-5-codex", "gpt-5.1-codex"], session.ModelNames);
        Assert.Equal(envelopes.Select(envelope => envelope.TelemetryEnvelopeId).Order(StringComparer.Ordinal), session.SourceTelemetryEnvelopeIds.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AcceptedCodexTelemetryDeduplicatesRetryWithoutSourceEventId()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        using (var first = CreateOtlpRequest("/v1/logs", issued.Secret))
        {
            AddSessionHeaders(first, "event-removed", "2026-06-17T11:15:00Z", "gpt-5-codex");
            first.Headers.Remove("X-AITO-Source-Event-Id");
            first.Headers.Remove("X-Correlation-Id");
            first.Headers.Add("X-Correlation-Id", "retry-correlation-001");
            using var firstResponse = await client.SendAsync(first);
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        }

        using (var retry = CreateOtlpRequest("/v1/logs", issued.Secret))
        {
            AddSessionHeaders(retry, "event-removed", "2026-06-17T11:15:00Z", "gpt-5-codex");
            retry.Headers.Remove("X-AITO-Source-Event-Id");
            retry.Headers.Remove("X-Correlation-Id");
            retry.Headers.Add("X-Correlation-Id", "retry-correlation-002");
            using var retryResponse = await client.SendAsync(retry);
            Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        }

        var envelope = Assert.Single(await store.ListTelemetryEnvelopesAsync(seed.Organization.CustomerOrganizationId));
        var session = Assert.Single(await store.ListAgentSessionsAsync(seed.Organization.CustomerOrganizationId));

        Assert.Null(envelope.SourceEventId);
        Assert.Equal("retry-correlation-001", envelope.CorrelationId);
        Assert.Equal(envelope.TelemetryEnvelopeId, Assert.Single(session.SourceTelemetryEnvelopeIds));
    }

    [Fact]
    public async Task CodexOtlpEndpointRecordsPolicyHiddenCandidateMetadataByDefault()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        const string rawPrompt = "prompt text that must not be fingerprinted";
        var payload = CreateLogPayloadWithBody($"aito.content.prompt:{rawPrompt}");
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest("/v1/logs", issued.Secret, payload);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = Assert.Single(await store.ListTelemetryEnvelopesAsync(seed.Organization.CustomerOrganizationId));
        Assert.Single(await store.ListAgentSessionsAsync(seed.Organization.CustomerOrganizationId));
        Assert.Empty(await store.ListIngestionRejectionsAsync(seed.Organization.CustomerOrganizationId));
        var metadata = Assert.Single(await store.ListContentCandidateMetadataAsync(seed.Organization.CustomerOrganizationId));
        Assert.Equal("content-capture-default-disabled", metadata.PolicyVersionId);
        Assert.Equal(issued.Credential.ScopedIngestionCredentialId, metadata.ScopedIngestionCredentialId);
        Assert.Equal(SetupProfileId, metadata.HarnessSetupProfileId);
        Assert.Equal($"{envelope.TelemetryEnvelopeId}:otlp.log.body", metadata.TelemetryReference);
        Assert.Equal(ContentClass.PromptSnippet, metadata.ContentClass);
        Assert.Equal(rawPrompt.Length, metadata.OriginalLength);
        Assert.Equal(ContentCapturePolicyDecision.PolicyDenied, metadata.PolicyDecision);
        Assert.Equal(ContentCandidateEvidenceState.PolicyHidden, metadata.EvidenceState);
        Assert.Equal(ContentRedactionStatus.NotRequired, metadata.RedactionStatus);
        Assert.DoesNotContain(rawPrompt, metadata.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CodexOtlpEndpointUsesActiveScopedPolicyForHarnessEmittedCandidate()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        await store.SetActiveContentCapturePolicyAsync(new SetActiveContentCapturePolicyRequest(
            new ContentCapturePolicy(
                seed.Organization.CustomerOrganizationId,
                "content-policy-active-001",
                CaptureEnabledByDefault: false,
                Rules:
                [
                    new ContentCapturePolicyRule(
                        AllowCapture: true,
                        Harness: CodingAgentHarness.CodexCli,
                        HarnessSetupProfileId: SetupProfileId,
                        ProductUserId: issued.Credential.ProductUserId,
                        ContentClass: ContentClass.PromptSnippet,
                        RetentionClass: ContentRetentionClass.MetadataOnly,
                        RecommendationUse: RecommendationUse.Disabled)
                ]),
            issued.Credential.ProductUserId,
            ProductRole.PlatformAdmin,
            CorrelationId: "content-policy-activation-001",
            ContentCapturePolicyChangeKind.Activated));
        const string rawPrompt = "policy allowed prompt candidate still waits for redaction";
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest("/v1/logs", issued.Secret, CreateLogPayloadWithBody($"aito.content.prompt:{rawPrompt}"));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var metadata = Assert.Single(await store.ListContentCandidateMetadataAsync(seed.Organization.CustomerOrganizationId));
        Assert.Equal("content-policy-active-001", metadata.PolicyVersionId);
        Assert.Equal(ContentCapturePolicyDecision.PolicyDenied, metadata.PolicyDecision);
        Assert.Equal(ContentCandidateEvidenceState.PolicyHidden, metadata.EvidenceState);
        Assert.Equal(ContentRedactionStatus.NotRequired, metadata.RedactionStatus);
        Assert.Equal(rawPrompt.Length, metadata.OriginalLength);
        Assert.DoesNotContain(rawPrompt, metadata.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CodexOtlpEndpointRecordsExtractionFailureMetadataWithoutRawContent()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest("/v1/logs", issued.Secret, CreateLogPayloadWithBody("aito.content.prompt:"));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await store.ListIngestionRejectionsAsync(seed.Organization.CustomerOrganizationId));
        var metadata = Assert.Single(await store.ListContentCandidateMetadataAsync(seed.Organization.CustomerOrganizationId));
        Assert.Equal("content-capture-default-disabled", metadata.PolicyVersionId);
        Assert.Equal("otlp.log.body", metadata.TelemetryReference.Split(':').Last());
        Assert.Equal(ContentClass.PromptSnippet, metadata.ContentClass);
        Assert.Equal(0, metadata.OriginalLength);
        Assert.Equal(ContentCapturePolicyDecision.PolicyDenied, metadata.PolicyDecision);
        Assert.Equal(ContentCandidateEvidenceState.RedactionFailed, metadata.EvidenceState);
        Assert.Equal(ContentRedactionStatus.Failed, metadata.RedactionStatus);
    }

    [Fact]
    public async Task AcceptedCodexTelemetryWithoutEventMetadataCreatesDistinctEnvelopesInOnePartialSession()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        using (var first = CreateOtlpRequest("/v1/logs", issued.Secret))
        {
            first.Headers.Add("X-Correlation-Id", "minimal-correlation-001");
            using var firstResponse = await client.SendAsync(first);
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        }

        using (var second = CreateOtlpRequest("/v1/logs", issued.Secret))
        {
            second.Headers.Add("X-Correlation-Id", "minimal-correlation-002");
            using var secondResponse = await client.SendAsync(second);
            Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        }

        var envelopes = await store.ListTelemetryEnvelopesAsync(seed.Organization.CustomerOrganizationId);
        var session = Assert.Single(await store.ListAgentSessionsAsync(seed.Organization.CustomerOrganizationId));

        Assert.Equal(2, envelopes.Count);
        Assert.All(envelopes, envelope =>
        {
            Assert.Equal("otlp.log.export", envelope.SourceEventName);
            Assert.Null(envelope.SourceEventId);
            Assert.Null(envelope.SourceEventTimestampUtc);
            Assert.Null(envelope.ConversationIdHash);
        });
        Assert.Null(session.ProviderSessionIdHash);
        Assert.Equal(envelopes.Select(envelope => envelope.TelemetryEnvelopeId).Order(StringComparer.Ordinal), session.SourceTelemetryEnvelopeIds.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AcceptedCodexTelemetryWithoutEventMetadataDeduplicatesWithExplicitSafeDedupeKey()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        using (var first = CreateOtlpRequest("/v1/logs", issued.Secret))
        {
            first.Headers.Add("X-Correlation-Id", "minimal-retry-correlation-001");
            first.Headers.Add("X-AITO-Dedupe-Key", "codex-retry-key-001");
            using var firstResponse = await client.SendAsync(first);
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        }

        using (var retry = CreateOtlpRequest("/v1/logs", issued.Secret))
        {
            retry.Headers.Add("X-Correlation-Id", "minimal-retry-correlation-002");
            retry.Headers.Add("X-AITO-Dedupe-Key", "codex-retry-key-001");
            using var retryResponse = await client.SendAsync(retry);
            Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        }

        var envelope = Assert.Single(await store.ListTelemetryEnvelopesAsync(seed.Organization.CustomerOrganizationId));
        var session = Assert.Single(await store.ListAgentSessionsAsync(seed.Organization.CustomerOrganizationId));

        Assert.Null(envelope.SourceEventId);
        Assert.Null(envelope.SourceEventTimestampUtc);
        Assert.Equal("minimal-retry-correlation-001", envelope.CorrelationId);
        Assert.Equal(envelope.TelemetryEnvelopeId, Assert.Single(session.SourceTelemetryEnvelopeIds));
    }

    [Fact]
    public async Task RejectedWrongTenantTelemetryDoesNotCreateAcceptedEnvelopeOrSession()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var contoso = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        await CreateTenantAsync(store, "fabrikam", "fabrikam-tenant");
        var issued = await IssueCredentialAsync(store, contoso);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest("/v1/logs", issued.Secret);
        request.Headers.Add("X-Customer-Organization-Slug", "fabrikam");
        AddSessionHeaders(request, "event-001", "2026-06-17T11:15:00Z", "gpt-5-codex");

        using var response = await client.SendAsync(request);

        await AssertOtlpRejectionAsync(response, HttpStatusCode.Forbidden, "tenant_context_mismatch");
        Assert.Empty(await store.ListTelemetryEnvelopesAsync(contoso.Organization.CustomerOrganizationId));
        Assert.Empty(await store.ListAgentSessionsAsync(contoso.Organization.CustomerOrganizationId));
    }

    [Fact]
    public async Task AcceptedCodexTelemetryDoesNotPersistRawContentHeaders()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store);
        var issued = await IssueCredentialAsync(store, seed);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest("/v1/logs", issued.Secret);
        AddSessionHeaders(request, "event-001", "2026-06-17T11:15:00Z", "gpt-5-codex");
        request.Headers.Add("X-AITO-Raw-Prompt", "please print the secret");
        request.Headers.Add("X-AITO-Command-Output", "secret command output");
        request.Headers.Add("X-AITO-Tool-Result", "tool result with file content");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = Assert.Single(await store.ListTelemetryEnvelopesAsync(seed.Organization.CustomerOrganizationId));
        var session = Assert.Single(await store.ListAgentSessionsAsync(seed.Organization.CustomerOrganizationId));
        Assert.DoesNotContain("please print the secret", envelope.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret command output", envelope.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tool result with file content", envelope.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("please print the secret", session.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret command output", session.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tool result with file content", session.ToString(), StringComparison.OrdinalIgnoreCase);
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
        IIngestionDiagnosticSink? diagnosticSink = null,
        string? endpointRegion = null,
        string? azureMonitorConnectionString = null)
    {
        return new WebApplicationFactory<TokenObservabilityIngestionAssemblyMarker>()
            .WithWebHostBuilder(builder =>
            {
                if (endpointRegion is not null)
                {
                    builder.UseSetting("ProductIngestion:Region", endpointRegion);
                }

                if (azureMonitorConnectionString is not null)
                {
                    builder.UseSetting("APPLICATIONINSIGHTS_CONNECTION_STRING", azureMonitorConnectionString);
                }

                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<InMemoryTenantMetadataStore>();
                    services.AddSingleton(store);

                    if (diagnosticSink is not null)
                    {
                        services.RemoveAll<IIngestionDiagnosticSink>();
                        services.AddSingleton(diagnosticSink);
                    }
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
        using var logRecord = new MemoryStream();
        WriteLengthDelimitedField(logRecord, fieldNumber: 3, Encoding.UTF8.GetBytes("info"));

        using var anyValue = new MemoryStream();
        WriteLengthDelimitedField(anyValue, fieldNumber: 1, Encoding.UTF8.GetBytes(body));
        WriteLengthDelimitedField(logRecord, fieldNumber: 5, anyValue.ToArray());

        using var scopeLogs = new MemoryStream();
        WriteLengthDelimitedField(scopeLogs, fieldNumber: 2, logRecord.ToArray());

        using var resourceLogs = new MemoryStream();
        WriteLengthDelimitedField(resourceLogs, fieldNumber: 2, scopeLogs.ToArray());

        using var export = new MemoryStream();
        WriteLengthDelimitedField(export, fieldNumber: 1, resourceLogs.ToArray());

        return export.ToArray();
    }

    private static byte[] CreateLogPayloadWithNonStringBody(uint value)
    {
        using var logRecord = new MemoryStream();
        WriteLengthDelimitedField(logRecord, fieldNumber: 3, Encoding.UTF8.GetBytes("info"));

        using var anyValue = new MemoryStream();
        WriteVarintField(anyValue, fieldNumber: 2, value);
        WriteLengthDelimitedField(logRecord, fieldNumber: 5, anyValue.ToArray());

        using var scopeLogs = new MemoryStream();
        WriteLengthDelimitedField(scopeLogs, fieldNumber: 2, logRecord.ToArray());

        using var resourceLogs = new MemoryStream();
        WriteLengthDelimitedField(resourceLogs, fieldNumber: 2, scopeLogs.ToArray());

        using var export = new MemoryStream();
        WriteLengthDelimitedField(export, fieldNumber: 1, resourceLogs.ToArray());

        return export.ToArray();
    }

    private static void WriteLengthDelimitedField(Stream stream, int fieldNumber, byte[] value)
    {
        WriteVarint(stream, (uint)((fieldNumber << 3) | 2));
        WriteVarint(stream, (uint)value.Length);
        stream.Write(value);
    }

    private static void WriteVarintField(Stream stream, int fieldNumber, uint value)
    {
        WriteVarint(stream, (uint)(fieldNumber << 3));
        WriteVarint(stream, value);
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

    private static void AddSessionHeaders(
        HttpRequestMessage request,
        string sourceEventId,
        string timestamp,
        string model)
    {
        request.Headers.Add("X-Correlation-Id", "codex-session-correlation-001");
        request.Headers.Add("X-AITO-Conversation-Id", "codex-conversation-001");
        request.Headers.Add("X-AITO-Turn-Id", "turn-001");
        request.Headers.Add("X-AITO-Source-Event-Id", sourceEventId);
        request.Headers.Add("X-AITO-Source-Event-Name", "codex.api_request");
        request.Headers.Add("X-AITO-Source-Event-Timestamp", timestamp);
        request.Headers.Add("X-AITO-Model", model);
        request.Headers.Add("X-AITO-Harness-Version", "codex-cli/1.2.3");
        request.Headers.Add("X-AITO-Sandbox", "workspace-write");
        request.Headers.Add("X-AITO-Approval-Policy", "on-request");
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

    private static Task<TelemetryEnvelopeRecord> RecordAcceptedEnvelopeAsync(
        InMemoryTenantMetadataStore store,
        ScopedIngestionCredential credential,
        string dedupeKeyHash,
        string correlationId,
        string? modelName = null)
    {
        return store.RecordTelemetryEnvelopeAsync(new CreateTelemetryEnvelopeRecordRequest(
            credential.CustomerOrganizationId,
            credential.HarnessSetupProfileId,
            credential.ScopedIngestionCredentialId,
            credential.ProductUserId,
            "codex-cli",
            SchemaVersion,
            "log",
            "codex.api_request",
            SourceEventTimestampUtc: Now,
            ConversationIdHash: null,
            TurnIdHash: null,
            SourceEventId: null,
            TraceIdHash: null,
            SpanIdHash: null,
            ModelName: modelName,
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
                ["diagnostic_store"] = "application_insights",
                ["metrics_store"] = "not_applicable",
                ["content_capture"] = "metadata_only"
            },
            EvidenceState: "observed",
            MetricState: "unavailable",
            TokenMetricStatus.Unavailable,
            TokenMetricConfidence.Unavailable,
            SourceEvidenceKind: "harness_emitted",
            correlationId,
            dedupeKeyHash,
            IngestionVersionMetadata: new Dictionary<string, string>
            {
                ["schema_version"] = SchemaVersion,
                ["harness_version"] = "unavailable",
                ["contract_version"] = SchemaVersion
            }));
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

    private static void AssertForbiddenAggregateLabelsAreAbsent(IReadOnlyDictionary<string, string> labels)
    {
        var forbiddenLabels = new[]
        {
            "session_id",
            "agent_session_id",
            "product_user_id",
            "developer",
            "credential_id",
            "trace_id",
            "span_id",
            "repository_path",
            "file_path",
            "prompt",
            "command_output",
            "tool_result",
            "source_event_id"
        };

        foreach (var forbiddenLabel in forbiddenLabels)
        {
            Assert.False(labels.ContainsKey(forbiddenLabel), $"Aggregate label '{forbiddenLabel}' must not be emitted.");
        }
    }

    private sealed class RecordingAggregateMetricSink : IAggregateMetricSink
    {
        public List<AggregateMetricPoint> Points { get; } = [];

        public Task ExportAsync(
            IReadOnlyList<AggregateMetricPoint> points,
            CancellationToken cancellationToken = default)
        {
            Points.AddRange(points);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingAggregateMetricSink : IAggregateMetricSink
    {
        public Task ExportAsync(
            IReadOnlyList<AggregateMetricPoint> points,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("The aggregate metric sink is unavailable.");
        }
    }

    private sealed class RecordingIngestionDiagnosticSink : IIngestionDiagnosticSink
    {
        public List<IngestionDiagnosticEvent> Events { get; } = [];

        public Task RouteAsync(
            IngestionDiagnosticEvent diagnosticEvent,
            CancellationToken cancellationToken = default)
        {
            Events.Add(diagnosticEvent);
            return Task.CompletedTask;
        }
    }

    private sealed record TenantSeed(CustomerOrganization Organization, IdentityTenant IdentityTenant);
}
