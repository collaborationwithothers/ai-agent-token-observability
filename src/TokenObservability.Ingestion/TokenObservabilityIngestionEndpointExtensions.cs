using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TokenObservability.Domain.Authorization;
using TokenObservability.Domain.Ingestion;
using TokenObservability.Domain.Tenancy;
using TokenObservability.Infrastructure.Ingestion;
using TokenObservability.Infrastructure.Persistence;

namespace TokenObservability.Ingestion;

internal static class TokenObservabilityIngestionEndpointExtensions
{
    private const string SupportedSchemaVersion = "2026-06-01";
    private const string SupportedContentCaptureMode = "metadata-only";
    private const long MaximumPayloadBytes = 1024 * 1024;

    public static void AddTokenObservabilityIngestionServices(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<TokenObservabilityIngestionOptions>(
            builder.Configuration.GetSection("ProductIngestion"));
        builder.Services.TryAddSingleton<ITenantMetadataClock, SystemTenantMetadataClock>();
        builder.Services.TryAddSingleton<InMemoryTenantMetadataStore>();
        builder.Services.TryAddSingleton<ScopedIngestionCredentialLifecycleService>();
    }

    public static void MapTokenObservabilityIngestionEndpoints(this WebApplication app)
    {
        app.MapGet("/ingestion", () => Results.Ok(new
        {
            service = "token-observability-ingestion",
            status = "available"
        }));

        app.MapPost("/v1/logs", HandleLogsAsync);
        app.MapPost("/v1/traces", HandleTracesAsync);
        app.MapPost("/v1/metrics", HandleMetricsAsync);
    }

    private static Task<IResult> HandleLogsAsync(
        HttpContext httpContext,
        ScopedIngestionCredentialLifecycleService credentialLifecycleService,
        InMemoryTenantMetadataStore tenantMetadataStore,
        IOptions<TokenObservabilityIngestionOptions> options)
    {
        return HandleOtlpSignalAsync(
            httpContext,
            credentialLifecycleService,
            tenantMetadataStore,
            options.Value,
            "logs");
    }

    private static Task<IResult> HandleTracesAsync(
        HttpContext httpContext,
        ScopedIngestionCredentialLifecycleService credentialLifecycleService,
        InMemoryTenantMetadataStore tenantMetadataStore,
        IOptions<TokenObservabilityIngestionOptions> options)
    {
        return HandleOtlpSignalAsync(
            httpContext,
            credentialLifecycleService,
            tenantMetadataStore,
            options.Value,
            "traces");
    }

    private static Task<IResult> HandleMetricsAsync(
        HttpContext httpContext,
        ScopedIngestionCredentialLifecycleService credentialLifecycleService,
        InMemoryTenantMetadataStore tenantMetadataStore,
        IOptions<TokenObservabilityIngestionOptions> options)
    {
        return HandleOtlpSignalAsync(
            httpContext,
            credentialLifecycleService,
            tenantMetadataStore,
            options.Value,
            "metrics");
    }

    private static async Task<IResult> HandleOtlpSignalAsync(
        HttpContext httpContext,
        ScopedIngestionCredentialLifecycleService credentialLifecycleService,
        InMemoryTenantMetadataStore tenantMetadataStore,
        TokenObservabilityIngestionOptions options,
        string signalType)
    {
        var correlationId = ResolveCorrelationId(httpContext);
        var route = httpContext.Request.Path.Value ?? $"/v1/{signalType}";
        var bearerSecret = ReadBearerSecret(httpContext.Request.Headers.Authorization.ToString());

        if (bearerSecret is null)
        {
            await RecordCredentialRejectionAsync(
                httpContext,
                tenantMetadataStore,
                credential: null,
                signalType,
                route,
                StatusCodes.Status401Unauthorized,
                "invalid_credential",
                correlationId);

            return CreateProblem(
                httpContext,
                "Missing or invalid scoped ingestion credential.",
                StatusCodes.Status401Unauthorized,
                "invalid_credential",
                correlationId);
        }

        var validation = await credentialLifecycleService.ValidateForIngestionAsync(
            new ValidateScopedIngestionCredentialForIngestionRequest(
                Secret: bearerSecret,
                DeclaredHarness: ReadHeader(httpContext, "X-AITO-Harness"),
                HarnessSetupProfileId: ReadHeader(httpContext, "X-AITO-Setup-Profile-Id"),
                CorrelationId: correlationId));

        if (!validation.IsValid)
        {
            var statusCode = ToCredentialStatusCode(validation.FailureReason);
            var code = ToCredentialRejectionCode(validation.FailureReason, statusCode);
            var auditEventId = await FindCredentialValidationAuditEventIdAsync(
                tenantMetadataStore,
                validation.Credential,
                correlationId);

            await RecordCredentialRejectionAsync(
                httpContext,
                tenantMetadataStore,
                validation.Credential,
                signalType,
                route,
                statusCode,
                code,
                correlationId,
                auditEventId);

            return CreateCredentialProblem(httpContext, validation.FailureReason, correlationId);
        }

        if (validation.Credential is null)
        {
            await RecordCredentialRejectionAsync(
                httpContext,
                tenantMetadataStore,
                credential: null,
                signalType,
                route,
                StatusCodes.Status401Unauthorized,
                "invalid_credential",
                correlationId);

            return CreateProblem(
                httpContext,
                "Scoped ingestion credential is not authorized for this request.",
                StatusCodes.Status401Unauthorized,
                "invalid_credential",
                correlationId);
        }

        var credential = validation.Credential;

        if (!StringComparer.Ordinal.Equals(ReadHeader(httpContext, "X-AITO-Schema-Version"), SupportedSchemaVersion))
        {
            await RecordIngestionRejectionAsync(
                tenantMetadataStore,
                credential,
                signalType,
                route,
                "unsupported_schema",
                correlationId);

            return CreateProblem(
                httpContext,
                "Unsupported ingestion schema version.",
                StatusCodes.Status400BadRequest,
                "unsupported_schema",
                correlationId);
        }

        if (await TenantHintMismatchesAsync(httpContext, tenantMetadataStore, credential))
        {
            await RecordIngestionRejectionAsync(
                tenantMetadataStore,
                credential,
                signalType,
                route,
                "tenant_context_mismatch",
                correlationId);

            return CreateProblem(
                httpContext,
                "Tenant context does not match the scoped ingestion credential.",
                StatusCodes.Status403Forbidden,
                "tenant_context_mismatch",
                correlationId);
        }

        if (await DataResidencyMismatchesAsync(tenantMetadataStore, credential, options.Region))
        {
            await RecordIngestionRejectionAsync(
                tenantMetadataStore,
                credential,
                signalType,
                route,
                "residency_mismatch",
                correlationId);

            return CreateProblem(
                httpContext,
                "Tenant data residency region does not match this ingestion endpoint.",
                StatusCodes.Status403Forbidden,
                "residency_mismatch",
                correlationId);
        }

        if (!HasValidPolicyContext(httpContext))
        {
            await RecordIngestionRejectionAsync(
                tenantMetadataStore,
                credential,
                signalType,
                route,
                "policy_context_missing",
                correlationId);

            return CreateProblem(
                httpContext,
                "Content capture policy context is missing or unsupported.",
                StatusCodes.Status403Forbidden,
                "policy_context_missing",
                correlationId);
        }

        var maxPayloadBytes = options.MaximumPayloadBytes is > 0
            ? options.MaximumPayloadBytes
            : MaximumPayloadBytes;

        if (httpContext.Request.ContentLength is > 0 &&
            httpContext.Request.ContentLength > maxPayloadBytes)
        {
            await RecordIngestionRejectionAsync(
                tenantMetadataStore,
                credential,
                signalType,
                route,
                "payload_too_large",
                correlationId);

            return CreateProblem(
                httpContext,
                "OTLP payload is too large.",
                StatusCodes.Status413PayloadTooLarge,
                "payload_too_large",
                correlationId);
        }

        var payloadValidation = await ValidatePayloadAsync(httpContext.Request, maxPayloadBytes, signalType);

        if (!payloadValidation.IsValid)
        {
            await RecordIngestionRejectionAsync(
                tenantMetadataStore,
                credential,
                signalType,
                route,
                payloadValidation.Code,
                correlationId);

            return CreateProblem(
                httpContext,
                payloadValidation.Title,
                payloadValidation.StatusCode,
                payloadValidation.Code,
                correlationId);
        }

        httpContext.Response.Headers["X-Correlation-Id"] = correlationId;
        return new OtlpProtobufResult([], StatusCodes.Status200OK);
    }

    private static IResult CreateCredentialProblem(
        HttpContext httpContext,
        ScopedIngestionCredentialValidationFailureReason failureReason,
        string correlationId)
    {
        var statusCode = ToCredentialStatusCode(failureReason);
        var code = ToCredentialRejectionCode(failureReason, statusCode);

        return CreateProblem(
            httpContext,
            "Scoped ingestion credential is not authorized for this request.",
            statusCode,
            code,
            correlationId);
    }

    private static int ToCredentialStatusCode(ScopedIngestionCredentialValidationFailureReason failureReason)
    {
        return failureReason switch
        {
            ScopedIngestionCredentialValidationFailureReason.WrongHarness or
                ScopedIngestionCredentialValidationFailureReason.WrongHarnessProfile or
                ScopedIngestionCredentialValidationFailureReason.InvalidTenant or
                ScopedIngestionCredentialValidationFailureReason.MalformedHarnessContext => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status401Unauthorized
        };
    }

    private static string ToCredentialRejectionCode(
        ScopedIngestionCredentialValidationFailureReason failureReason,
        int statusCode)
    {
        return failureReason == ScopedIngestionCredentialValidationFailureReason.InvalidTenant
            ? "invalid_tenant"
            : statusCode == StatusCodes.Status403Forbidden
            ? "credential_out_of_scope"
            : "invalid_credential";
    }

    private static async Task<PayloadValidationResult> ValidatePayloadAsync(
        HttpRequest request,
        long maxPayloadBytes,
        string signalType)
    {
        if (!IsProtobufContentType(request.ContentType))
        {
            return PayloadValidationResult.Invalid(
                "Unsupported OTLP content type.",
                StatusCodes.Status415UnsupportedMediaType,
                "unsupported_content_type");
        }

        var payload = await ReadBoundedPayloadAsync(request.Body, maxPayloadBytes);

        if (payload is null)
        {
            return PayloadValidationResult.Invalid(
                "OTLP payload is too large.",
                StatusCodes.Status413PayloadTooLarge,
                "payload_too_large");
        }

        return OtlpProtobufEnvelopeValidator.HasValidExportRequest(payload, signalType)
            ? PayloadValidationResult.Valid()
            : PayloadValidationResult.Invalid(
                "Malformed OTLP payload.",
                StatusCodes.Status400BadRequest,
                "malformed_otlp");
    }

    private static async Task<bool> TenantHintMismatchesAsync(
        HttpContext httpContext,
        InMemoryTenantMetadataStore tenantMetadataStore,
        ScopedIngestionCredential credential)
    {
        var tenantSlug = ReadHeader(httpContext, "X-Customer-Organization-Slug");

        if (string.IsNullOrWhiteSpace(tenantSlug))
        {
            return false;
        }

        var hintedOrganization = await tenantMetadataStore.FindCustomerOrganizationBySlugAsync(tenantSlug);

        return hintedOrganization is null ||
            hintedOrganization.CustomerOrganizationId != credential.CustomerOrganizationId;
    }

    private static async Task<bool> DataResidencyMismatchesAsync(
        InMemoryTenantMetadataStore tenantMetadataStore,
        ScopedIngestionCredential credential,
        string? endpointRegion)
    {
        if (string.IsNullOrWhiteSpace(endpointRegion))
        {
            return false;
        }

        var customerOrganization = await tenantMetadataStore.FindCustomerOrganizationAsync(credential.CustomerOrganizationId);

        return customerOrganization is null ||
            !StringComparer.OrdinalIgnoreCase.Equals(customerOrganization.DataResidencyRegion, endpointRegion.Trim());
    }

    private static bool HasValidPolicyContext(HttpContext httpContext)
    {
        return StringComparer.Ordinal.Equals(
            ReadHeader(httpContext, "X-AITO-Content-Capture-Mode"),
            SupportedContentCaptureMode);
    }

    private static async Task RecordIngestionRejectionAsync(
        InMemoryTenantMetadataStore tenantMetadataStore,
        ScopedIngestionCredential credential,
        string signalType,
        string route,
        string reasonCode,
        string correlationId)
    {
        var evidenceMetadata = CreateIngestionRejectionEvidenceMetadata(
            reasonCode,
            route,
            credential.HarnessSetupProfileId);
        var auditEvent = await tenantMetadataStore.RecordGovernanceAuditEventAsync(
            credential.CustomerOrganizationId,
            new CreateGovernanceAuditEventRequest(
                AuditEventId: $"ingestion-rejection-{Guid.NewGuid():N}",
                ActorProductUserId: credential.ProductUserId,
                EffectiveRole: null,
                Action: ProductAuthorizationAction.TelemetryIngest,
                TargetScope: new ProductScope(ProductScopeKind.HarnessProfile, credential.HarnessSetupProfileId),
                Decision: "denied",
                DenialReason: ProductAuthorizationDenialReason.ScopeMismatch,
                CorrelationId: correlationId,
                EvidenceMetadata: evidenceMetadata));

        await tenantMetadataStore.RecordIngestionRejectionAsync(
            new CreateIngestionRejectionRecordRequest(
                CustomerOrganizationId: credential.CustomerOrganizationId,
                HarnessSetupProfileId: credential.HarnessSetupProfileId,
                ScopedIngestionCredentialId: credential.ScopedIngestionCredentialId,
                DeclaredHarness: ToWireHarness(credential.AllowedHarness),
                SignalType: signalType,
                RequestRoute: route,
                ReasonCode: reasonCode,
                HttpStatus: ToHttpStatus(reasonCode),
                CorrelationId: correlationId,
                AuditEventId: auditEvent.AuditEventId,
                EvidenceMetadata: evidenceMetadata));
    }

    private static async Task RecordCredentialRejectionAsync(
        HttpContext httpContext,
        InMemoryTenantMetadataStore tenantMetadataStore,
        ScopedIngestionCredential? credential,
        string signalType,
        string route,
        int httpStatus,
        string reasonCode,
        string correlationId,
        string? auditEventId = null)
    {
        var customerOrganizationId = credential?.CustomerOrganizationId ??
            await ResolveTenantCandidateAsync(httpContext, tenantMetadataStore);
        var harnessSetupProfileId = credential?.HarnessSetupProfileId;
        var declaredHarness = ReadKnownHarnessHeader(httpContext);
        var evidenceMetadata = CreateIngestionRejectionEvidenceMetadata(
            reasonCode,
            route,
            ToSafeScopeId(harnessSetupProfileId));
        var resolvedAuditEventId = auditEventId;

        if (resolvedAuditEventId is null && customerOrganizationId is { } tenantId)
        {
            var auditEvent = await tenantMetadataStore.RecordGovernanceAuditEventAsync(
                tenantId,
                new CreateGovernanceAuditEventRequest(
                    AuditEventId: $"ingestion-rejection-{Guid.NewGuid():N}",
                    ActorProductUserId: credential?.ProductUserId,
                    EffectiveRole: null,
                    Action: ProductAuthorizationAction.TelemetryIngest,
                    TargetScope: new ProductScope(ProductScopeKind.HarnessProfile, ToSafeScopeId(harnessSetupProfileId)),
                    Decision: "denied",
                    DenialReason: ToCredentialDenialReason(reasonCode),
                    CorrelationId: correlationId,
                    EvidenceMetadata: evidenceMetadata));
            resolvedAuditEventId = auditEvent.AuditEventId;
        }

        await tenantMetadataStore.RecordIngestionRejectionAsync(
            new CreateIngestionRejectionRecordRequest(
                CustomerOrganizationId: customerOrganizationId,
                HarnessSetupProfileId: string.IsNullOrWhiteSpace(harnessSetupProfileId) ? null : harnessSetupProfileId,
                ScopedIngestionCredentialId: credential?.ScopedIngestionCredentialId,
                DeclaredHarness: declaredHarness,
                SignalType: signalType,
                RequestRoute: route,
                ReasonCode: reasonCode,
                HttpStatus: httpStatus,
                CorrelationId: correlationId,
                AuditEventId: resolvedAuditEventId,
                EvidenceMetadata: evidenceMetadata));
    }

    private static ProductAuthorizationDenialReason ToCredentialDenialReason(string reasonCode)
    {
        return reasonCode == "invalid_tenant"
            ? ProductAuthorizationDenialReason.InvalidTenant
            : ProductAuthorizationDenialReason.ScopeMismatch;
    }

    private static async Task<string?> FindCredentialValidationAuditEventIdAsync(
        InMemoryTenantMetadataStore tenantMetadataStore,
        ScopedIngestionCredential? credential,
        string correlationId)
    {
        if (credential is null)
        {
            return null;
        }

        var auditEvents = await tenantMetadataStore.ListGovernanceAuditEventsAsync(credential.CustomerOrganizationId);

        return auditEvents
            .Where(auditEvent =>
                auditEvent.Action == ProductAuthorizationAction.TelemetryIngest &&
                auditEvent.Decision == "denied" &&
                StringComparer.Ordinal.Equals(auditEvent.CorrelationId, correlationId))
            .OrderByDescending(auditEvent => auditEvent.CreatedAtUtc)
            .Select(auditEvent => auditEvent.AuditEventId)
            .FirstOrDefault();
    }

    private static Dictionary<string, string> CreateIngestionRejectionEvidenceMetadata(
        string reasonCode,
        string route,
        string scopeId)
    {
        return new Dictionary<string, string>
        {
            ["evidence_kind"] = "ingestion_decision",
            ["operation"] = "ingestion_rejection",
            ["result"] = reasonCode,
            ["request_route"] = route,
            ["scope_kind"] = ProductScopeKind.HarnessProfile.ToString(),
            ["scope_id"] = scopeId
        };
    }

    private static async Task<CustomerOrganizationId?> ResolveTenantCandidateAsync(
        HttpContext httpContext,
        InMemoryTenantMetadataStore tenantMetadataStore)
    {
        var tenantSlug = ReadHeader(httpContext, "X-Customer-Organization-Slug");

        if (string.IsNullOrWhiteSpace(tenantSlug))
        {
            return null;
        }

        var organization = await tenantMetadataStore.FindCustomerOrganizationBySlugAsync(tenantSlug);

        return organization?.CustomerOrganizationId;
    }

    private static string? ReadKnownHarnessHeader(HttpContext httpContext)
    {
        var value = ReadHeader(httpContext, "X-AITO-Harness");
        return StringComparer.Ordinal.Equals(value, "codex-cli") ? value : null;
    }

    private static string ToSafeScopeId(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && IsSafeMetadataValue(value)
            ? value
            : "unknown";
    }

    private static bool IsSafeMetadataValue(string value)
    {
        return value.Length is > 0 and <= 128 &&
            value.All(static character =>
                char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_' or ':' or '.' or '/');
    }

    private static int ToHttpStatus(string reasonCode)
    {
        return reasonCode switch
        {
            "unsupported_schema" or "malformed_otlp" => StatusCodes.Status400BadRequest,
            "tenant_context_mismatch" or "residency_mismatch" or "policy_context_missing" => StatusCodes.Status403Forbidden,
            "payload_too_large" => StatusCodes.Status413PayloadTooLarge,
            "unsupported_content_type" => StatusCodes.Status415UnsupportedMediaType,
            _ => StatusCodes.Status400BadRequest
        };
    }

    private static string ToWireHarness(CodingAgentHarness harness)
    {
        return harness switch
        {
            CodingAgentHarness.CodexCli => "codex-cli",
            _ => throw new ArgumentOutOfRangeException(nameof(harness), harness, null)
        };
    }

    private static string? ReadBearerSecret(string authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader) ||
            !AuthenticationHeaderValue.TryParse(authorizationHeader, out var value) ||
            !StringComparer.OrdinalIgnoreCase.Equals(value.Scheme, "Bearer") ||
            string.IsNullOrWhiteSpace(value.Parameter))
        {
            return null;
        }

        return value.Parameter.Trim();
    }

    private static string ReadHeader(HttpContext httpContext, string name)
    {
        return httpContext.Request.Headers[name].ToString().Trim();
    }

    private static async Task<byte[]?> ReadBoundedPayloadAsync(Stream body, long maxPayloadBytes)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        long totalBytes = 0;

        while (true)
        {
            var bytesRead = await body.ReadAsync(buffer);

            if (bytesRead == 0)
            {
                return memory.ToArray();
            }

            totalBytes += bytesRead;

            if (totalBytes > maxPayloadBytes)
            {
                return null;
            }

            memory.Write(buffer, 0, bytesRead);
        }
    }

    private static IResult CreateProblem(
        HttpContext httpContext,
        string title,
        int statusCode,
        string code,
        string correlationId)
    {
        httpContext.Response.Headers["X-AITO-Rejection-Code"] = code;
        httpContext.Response.Headers["X-Correlation-Id"] = correlationId;

        return new OtlpProtobufResult(
            CreateGoogleRpcStatus(statusCode, title),
            statusCode);
    }

    private static byte[] CreateGoogleRpcStatus(int statusCode, string message)
    {
        using var memory = new MemoryStream();
        WriteVarintField(memory, fieldNumber: 1, value: ToGoogleRpcCode(statusCode));
        WriteLengthDelimitedField(memory, fieldNumber: 2, Encoding.UTF8.GetBytes(message));
        return memory.ToArray();
    }

    private static uint ToGoogleRpcCode(int statusCode)
    {
        return statusCode switch
        {
            StatusCodes.Status400BadRequest => 3,
            StatusCodes.Status401Unauthorized => 16,
            StatusCodes.Status403Forbidden => 7,
            StatusCodes.Status413PayloadTooLarge => 8,
            StatusCodes.Status415UnsupportedMediaType => 3,
            StatusCodes.Status429TooManyRequests => 8,
            _ => 2
        };
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

    private static string ResolveCorrelationId(HttpContext httpContext)
    {
        var requestedCorrelationId = httpContext.Request.Headers["X-Correlation-Id"].ToString().Trim();

        return IsSafeCorrelationId(requestedCorrelationId)
            ? requestedCorrelationId
            : $"corr-{Guid.NewGuid():N}";
    }

    private static bool IsSafeCorrelationId(string value)
    {
        return value.Length is >= 8 and <= 96 &&
            value.All(static character =>
                char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_' or '.');
    }

    private static bool IsProtobufContentType(string? contentType)
    {
        return StringComparer.Ordinal.Equals(contentType?.Trim(), "application/x-protobuf");
    }

    private sealed record PayloadValidationResult(
        bool IsValid,
        string Title,
        int StatusCode,
        string Code)
    {
        public static PayloadValidationResult Valid()
        {
            return new PayloadValidationResult(
                true,
                Title: string.Empty,
                StatusCodes.Status200OK,
                Code: string.Empty);
        }

        public static PayloadValidationResult Invalid(string title, int statusCode, string code)
        {
            return new PayloadValidationResult(false, title, statusCode, code);
        }
    }

    private sealed record OtlpProtobufResult(byte[] Body, int StatusCode) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = StatusCode;
            httpContext.Response.ContentType = "application/x-protobuf";
            httpContext.Response.ContentLength = Body.Length;

            return httpContext.Response.Body.WriteAsync(Body).AsTask();
        }
    }
}

internal sealed class TokenObservabilityIngestionOptions
{
    public long MaximumPayloadBytes { get; set; } = 1024 * 1024;

    public string? Region { get; set; }
}
