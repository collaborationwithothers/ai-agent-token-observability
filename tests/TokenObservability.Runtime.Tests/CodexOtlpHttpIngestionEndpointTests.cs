using System.Net;
using System.Net.Http.Headers;
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
    public async Task CodexOtlpEndpointRejectsMissingCredentialBeforeParsingPayload()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        await CreateTenantAsync(store);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateOtlpRequest("/v1/logs", secret: null, payload: [0xFF, 0xFF, 0xFF]);

        using var response = await client.SendAsync(request);

        await AssertOtlpRejectionAsync(response, HttpStatusCode.Unauthorized, "invalid_credential");
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
        Assert.Contains(
            await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId),
            auditEvent => auditEvent.Action == ProductAuthorizationAction.TelemetryIngest &&
                auditEvent.EvidenceMetadata["operation"] == "ingestion_rejection" &&
                auditEvent.EvidenceMetadata["result"] == "unsupported_schema");
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

    private sealed record TenantSeed(CustomerOrganization Organization, IdentityTenant IdentityTenant);
}
