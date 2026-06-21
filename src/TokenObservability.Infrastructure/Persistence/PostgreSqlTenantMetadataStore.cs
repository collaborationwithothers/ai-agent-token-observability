using System.Collections.ObjectModel;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using TokenObservability.Domain.Authorization;
using TokenObservability.Domain.Ingestion;
using TokenObservability.Domain.Pricing;
using TokenObservability.Domain.Recommendations;
using TokenObservability.Domain.Tenancy;

namespace TokenObservability.Infrastructure.Persistence;

public sealed class PostgreSqlTenantMetadataStore(
    NpgsqlDataSource dataSource,
    ITenantMetadataClock clock) : ITenantMetadataStore
{
    private static readonly string[] SensitiveEvidenceKeyFragments =
    [
        "raw_prompt",
        "prompt_text",
        "code_content",
        "command_output",
        "tool_result",
        "secret",
        "connection_string",
        "password",
        "api_key",
        "access_token"
    ];

    private static readonly string[] SensitiveEvidenceValueFragments =
    [
        "bearer ",
        "sk-",
        "accountkey=",
        "password=",
        "secret=",
        "api_key=",
        "access_token=",
        "connection string",
        "connectionstring",
        "private key",
        "raw prompt",
        "prompt text",
        "code content",
        "command output",
        "tool result"
    ];

    private readonly PostgreSqlProductApiIdempotencyStore idempotencyStore = new(dataSource, clock);

    public bool CanLoadMissingCustomerOrganizations => false;

    public Task<CustomerOrganization> CreateCustomerOrganizationAsync(CreateCustomerOrganizationRequest request)
    {
        throw new NotSupportedException("PostgreSQL tenant creation is owned by tenant onboarding.");
    }

    public Task<CustomerOrganization> EnsureCustomerOrganizationLoadedAsync(EnsureCustomerOrganizationLoadedRequest request)
    {
        throw new NotSupportedException("PostgreSQL tenant creation is owned by tenant onboarding.");
    }

    public async Task<CustomerOrganization?> FindCustomerOrganizationBySlugAsync(string slug)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT customer_organization_id, slug, display_name, data_residency_region, isolation_tier, status, created_at_utc, updated_at_utc
            FROM customer_organization
            WHERE slug = @slug
              AND status = 'active'
            """);
        command.Parameters.AddWithValue("slug", slug.Trim().ToLowerInvariant());
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? ReadCustomerOrganization(reader)
            : null;
    }

    public async Task<CustomerOrganization?> FindCustomerOrganizationAsync(CustomerOrganizationId customerOrganizationId)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT customer_organization_id, slug, display_name, data_residency_region, isolation_tier, status, created_at_utc, updated_at_utc
            FROM customer_organization
            WHERE customer_organization_id = @customer_organization_id
            """);
        command.Parameters.AddWithValue("customer_organization_id", customerOrganizationId.Value);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? ReadCustomerOrganization(reader)
            : null;
    }

    public async Task<IdentityTenant?> FindIdentityTenantForClaimsAsync(
        CustomerOrganizationId customerOrganizationId,
        AuthenticatedTokenClaims claims)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT identity_tenant_id, customer_organization_id, provider, issuer, external_tenant_id, allowed_audiences_json::text,
                   jwks_uri, display_name, status, last_validated_at_utc, created_at_utc, updated_at_utc
            FROM identity_tenant
            WHERE customer_organization_id = @customer_organization_id
              AND issuer = @issuer
              AND external_tenant_id = @external_tenant_id
              AND status = 'active'
            """);
        command.Parameters.AddWithValue("customer_organization_id", customerOrganizationId.Value);
        command.Parameters.AddWithValue("issuer", claims.Issuer.Trim());
        command.Parameters.AddWithValue("external_tenant_id", claims.ExternalTenantId.Trim());
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var tenant = ReadIdentityTenant(reader);
            if (tenant.AllowedAudiences.Contains(claims.Audience.Trim(), StringComparer.Ordinal))
            {
                return tenant;
            }
        }

        return null;
    }

    public async Task<GovernanceAuditEvent?> FindGovernanceAuditEventAsync(
        CustomerOrganizationId customerOrganizationId,
        string auditEventId)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT audit_event_id, customer_organization_id, actor_product_user_id, effective_role, action, target_resource_kind,
                   target_resource_id, decision, denial_reason, correlation_id, evidence_metadata_json::text, created_at_utc
            FROM governance_audit_event
            WHERE customer_organization_id = @customer_organization_id
              AND audit_event_id = @audit_event_id
            """);
        command.Parameters.AddWithValue("customer_organization_id", customerOrganizationId.Value);
        command.Parameters.AddWithValue("audit_event_id", auditEventId.Trim());
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? ReadGovernanceAuditEvent(reader)
            : null;
    }

    public async Task<IReadOnlyList<GovernanceAuditEvent>> ListGovernanceAuditEventsAsync(
        CustomerOrganizationId customerOrganizationId)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT audit_event_id, customer_organization_id, actor_product_user_id, effective_role, action, target_resource_kind,
                   target_resource_id, decision, denial_reason, correlation_id, evidence_metadata_json::text, created_at_utc
            FROM governance_audit_event
            WHERE customer_organization_id = @customer_organization_id
            ORDER BY created_at_utc, audit_event_id
            """);
        command.Parameters.AddWithValue("customer_organization_id", customerOrganizationId.Value);
        await using var reader = await command.ExecuteReaderAsync();
        var events = new List<GovernanceAuditEvent>();
        while (await reader.ReadAsync())
        {
            events.Add(ReadGovernanceAuditEvent(reader));
        }

        return events;
    }

    public async Task<GovernanceAuditEvent> RecordGovernanceAuditEventAsync(
        CustomerOrganizationId customerOrganizationId,
        CreateGovernanceAuditEventRequest request)
    {
        var now = clock.UtcNow.ToUniversalTime();
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await RequireCustomerOrganizationAsync(connection, transaction, customerOrganizationId);
        if (request.ActorProductUserId is not null)
        {
            await RequireProductUserAsync(connection, transaction, customerOrganizationId, request.ActorProductUserId.Value);
        }

        var audit = new GovernanceAuditEvent(
            request.AuditEventId.Trim(),
            customerOrganizationId,
            request.ActorProductUserId,
            request.EffectiveRole,
            request.Action,
            request.TargetScope.Kind.ToString(),
            GetTargetResourceId(customerOrganizationId, request.TargetScope),
            request.Decision.Trim(),
            request.DenialReason,
            request.CorrelationId.Trim(),
            new Dictionary<string, string>(request.EvidenceMetadata, StringComparer.Ordinal),
            now);
        await InsertGovernanceAuditEventAsync(connection, transaction, audit);
        await transaction.CommitAsync();
        return audit;
    }

    public async Task<ProductAuthorizationDecision> AuthorizeProductActionAsync(
        CustomerOrganizationId customerOrganizationId,
        IdentityTenantId identityTenantId,
        AuthenticatedTokenClaims claims,
        ProductAuthorizationAction action,
        ProductScope requestedScope,
        string? correlationId = null)
    {
        var identityTenant = await FindIdentityTenantByIdAsync(customerOrganizationId, identityTenantId);
        if (identityTenant is null ||
            !StringComparer.Ordinal.Equals(identityTenant.Issuer, claims.Issuer.Trim()) ||
            !StringComparer.Ordinal.Equals(identityTenant.ExternalTenantId, claims.ExternalTenantId.Trim()) ||
            !identityTenant.AllowedAudiences.Contains(claims.Audience.Trim(), StringComparer.Ordinal))
        {
            await RecordAuthorizationDenialAsync(
                customerOrganizationId,
                action,
                requestedScope,
                ProductAuthorizationDenialReason.InvalidTenant,
                correlationId ?? $"authorization-{Guid.NewGuid():N}");
            return new ProductAuthorizationDecision(false, ProductAuthorizationDenialReason.InvalidTenant, null, [], []);
        }

        var productUser = await FindProductUserByExternalSubjectAsync(customerOrganizationId, identityTenantId, claims.Subject);
        if (productUser is null)
        {
            await RecordAuthorizationDenialAsync(
                customerOrganizationId,
                action,
                requestedScope,
                ProductAuthorizationDenialReason.InvalidTenant,
                correlationId ?? $"authorization-{Guid.NewGuid():N}");
            return new ProductAuthorizationDecision(false, ProductAuthorizationDenialReason.InvalidTenant, null, [], []);
        }

        var mappings = await ListActiveProductRoleMappingsAsync(customerOrganizationId, identityTenantId);
        var matchedMappings = mappings.Where(mapping => PrincipalMatches(mapping, claims)).ToArray();
        if (matchedMappings.Length == 0)
        {
            await RecordAuthorizationDenialAsync(customerOrganizationId, action, requestedScope, ProductAuthorizationDenialReason.MissingRoleMapping, correlationId ?? $"authorization-{Guid.NewGuid():N}");
            return new ProductAuthorizationDecision(false, ProductAuthorizationDenialReason.MissingRoleMapping, productUser, [], []);
        }

        var scopedMappings = matchedMappings.Where(mapping => ScopeMatches(mapping, requestedScope)).ToArray();
        if (scopedMappings.Length == 0)
        {
            await RecordAuthorizationDenialAsync(customerOrganizationId, action, requestedScope, ProductAuthorizationDenialReason.ScopeMismatch, correlationId ?? $"authorization-{Guid.NewGuid():N}");
            return new ProductAuthorizationDecision(false, ProductAuthorizationDenialReason.ScopeMismatch, productUser, matchedMappings.Select(static mapping => mapping.ProductRole).Distinct().ToArray(), matchedMappings);
        }

        if (!scopedMappings.Any(mapping => RoleAllowsAction(mapping.ProductRole, action)))
        {
            await RecordAuthorizationDenialAsync(customerOrganizationId, action, requestedScope, ProductAuthorizationDenialReason.InsufficientRole, correlationId ?? $"authorization-{Guid.NewGuid():N}");
            return new ProductAuthorizationDecision(false, ProductAuthorizationDenialReason.InsufficientRole, productUser, scopedMappings.Select(static mapping => mapping.ProductRole).Distinct().ToArray(), scopedMappings);
        }

        return new ProductAuthorizationDecision(
            true,
            ProductAuthorizationDenialReason.None,
            productUser,
            scopedMappings.Select(static mapping => mapping.ProductRole).Distinct().ToArray(),
            scopedMappings);
    }

    public async Task RecordAuthorizationDenialAsync(
        CustomerOrganizationId customerOrganizationId,
        ProductAuthorizationAction action,
        ProductScope requestedScope,
        ProductAuthorizationDenialReason denialReason,
        string correlationId)
    {
        var audit = new GovernanceAuditEvent(
            $"audit-authz-{Guid.NewGuid():N}",
            customerOrganizationId,
            ActorProductUserId: null,
            EffectiveRole: null,
            action,
            requestedScope.Kind.ToString(),
            GetTargetResourceId(customerOrganizationId, requestedScope),
            "denied",
            denialReason,
            correlationId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["evidence_kind"] = "authorization_decision",
                ["operation"] = "authorization_denied",
                ["result"] = "denied",
                ["denial_reason"] = denialReason.ToString()
            },
            clock.UtcNow.ToUniversalTime());

        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await RequireCustomerOrganizationAsync(connection, transaction, customerOrganizationId);
        await InsertGovernanceAuditEventAsync(connection, transaction, audit);
        await transaction.CommitAsync();
    }

    public Task<IReadOnlyList<IngestionRejectionRecord>> ListIngestionRejectionsAsync(
        CustomerOrganizationId customerOrganizationId)
    {
        throw CreateUnsupportedProductRuntimeSurfaceException();
    }

    public Task<IReadOnlyList<AgentSessionRecord>> ListAgentSessionsAsync(
        CustomerOrganizationId customerOrganizationId)
    {
        throw CreateUnsupportedProductRuntimeSurfaceException();
    }

    public Task<IReadOnlyList<TokenObservationRecord>> ListTokenObservationsAsync(
        CustomerOrganizationId customerOrganizationId,
        string agentSessionId)
    {
        throw CreateUnsupportedProductRuntimeSurfaceException();
    }

    public Task<IReadOnlyList<TokenHotspotRecord>> ListTokenHotspotsAsync(
        CustomerOrganizationId customerOrganizationId,
        string agentSessionId)
    {
        throw CreateUnsupportedProductRuntimeSurfaceException();
    }

    public Task<IReadOnlyList<ContentReferenceRecord>> ListContentReferencesForSessionAsync(
        CustomerOrganizationId customerOrganizationId,
        string agentSessionId)
    {
        throw CreateUnsupportedProductRuntimeSurfaceException();
    }

    public Task<IReadOnlyList<ContentReferenceRecord>> ListContentReviewItemsAsync(
        CustomerOrganizationId customerOrganizationId,
        ContentReferenceCaptureState? state = null)
    {
        throw CreateUnsupportedProductRuntimeSurfaceException();
    }

    public Task<ContentReferenceRecord?> FindContentReferenceAsync(
        CustomerOrganizationId customerOrganizationId,
        ContentReferenceId contentReferenceId)
    {
        throw CreateUnsupportedProductRuntimeSurfaceException();
    }

    public Task<RedactionReviewRecord> ReviewContentReferenceAsync(ReviewContentReferenceRequest request)
    {
        throw CreateUnsupportedProductRuntimeSurfaceException();
    }

    public async Task<PricingBasisRecord> CreatePricingBasisRecordAsync(CreatePricingBasisRecordRequest request)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await RequireCustomerOrganizationAsync(connection, transaction, request.CustomerOrganizationId);
        if (await FindGovernanceAuditEventAsync(connection, transaction, request.CustomerOrganizationId, request.AuditEventId) is null)
        {
            throw new InvalidOperationException("Pricing basis audit event does not belong to the customer organization.");
        }

        var normalized = NormalizePricingBasisRequest(request);
        var now = clock.UtcNow.ToUniversalTime();
        var record = new PricingBasisRecord(
            $"pricing-basis-{Guid.NewGuid():N}",
            request.CustomerOrganizationId,
            normalized.Harness,
            normalized.ProviderName,
            normalized.ModelName,
            request.TokenType,
            normalized.BillingRoute,
            normalized.Currency,
            request.PricePerMillionTokens,
            normalized.PricingVersion,
            request.SourceKind,
            request.ReviewState,
            request.EffectiveFromUtc.ToUniversalTime(),
            request.EffectiveToUtc?.ToUniversalTime(),
            normalized.AuditEventId,
            normalized.SourceMetadata,
            now,
            now);
        await InsertPricingBasisRecordAsync(connection, transaction, record);
        await transaction.CommitAsync();
        return record;
    }

    public async Task<PricingBasisRecord> CreatePricingSeedCandidateRecordAsync(
        CreatePricingBasisRecordRequest request,
        string correlationId)
    {
        if (request.SourceKind != PricingSourceKind.AutomatedSeed ||
            request.ReviewState != PricingReviewState.Candidate)
        {
            throw new ArgumentException("Pricing seed records must be automated seed candidates.", nameof(request));
        }

        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await RequireCustomerOrganizationAsync(connection, transaction, request.CustomerOrganizationId);

        var normalized = NormalizePricingBasisRequest(request);
        var now = clock.UtcNow.ToUniversalTime();
        var record = new PricingBasisRecord(
            $"pricing-basis-{Guid.NewGuid():N}",
            request.CustomerOrganizationId,
            normalized.Harness,
            normalized.ProviderName,
            normalized.ModelName,
            request.TokenType,
            normalized.BillingRoute,
            normalized.Currency,
            request.PricePerMillionTokens,
            normalized.PricingVersion,
            request.SourceKind,
            request.ReviewState,
            request.EffectiveFromUtc.ToUniversalTime(),
            request.EffectiveToUtc?.ToUniversalTime(),
            normalized.AuditEventId,
            normalized.SourceMetadata,
            now,
            now);
        var audit = CreatePricingAuditEvent(
            record,
            actorProductUserId: null,
            actorEffectiveRole: null,
            "pricing_seed_refresh",
            "candidate",
            correlationId,
            createdAtUtc: now);
        await InsertGovernanceAuditEventAsync(connection, transaction, audit);
        await InsertPricingBasisRecordAsync(connection, transaction, record);
        await transaction.CommitAsync();
        return record;
    }

    public async Task<PricingBasisRecord> CreateCustomerPricingOverrideAsync(
        CreatePricingBasisRecordRequest request,
        ProductUserId actorProductUserId,
        ProductRole actorEffectiveRole,
        string correlationId)
    {
        if (request.SourceKind != PricingSourceKind.AdminOverride)
        {
            throw new ArgumentException("Customer pricing overrides must use admin override source kind.", nameof(request));
        }

        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await RequireCustomerOrganizationAsync(connection, transaction, request.CustomerOrganizationId);
        await RequireProductUserAsync(connection, transaction, request.CustomerOrganizationId, actorProductUserId);

        var normalized = NormalizePricingBasisRequest(request);
        var now = clock.UtcNow.ToUniversalTime();
        var record = new PricingBasisRecord(
            $"pricing-basis-{Guid.NewGuid():N}",
            request.CustomerOrganizationId,
            normalized.Harness,
            normalized.ProviderName,
            normalized.ModelName,
            request.TokenType,
            normalized.BillingRoute,
            normalized.Currency,
            request.PricePerMillionTokens,
            normalized.PricingVersion,
            request.SourceKind,
            request.ReviewState,
            request.EffectiveFromUtc.ToUniversalTime(),
            request.EffectiveToUtc?.ToUniversalTime(),
            normalized.AuditEventId,
            normalized.SourceMetadata,
            now,
            now);
        var audit = CreatePricingAuditEvent(record, actorProductUserId, actorEffectiveRole, "pricing_override_create", "created", correlationId);
        await InsertGovernanceAuditEventAsync(connection, transaction, audit);
        await InsertPricingBasisRecordAsync(connection, transaction, record);
        await transaction.CommitAsync();
        return record;
    }

    public async Task<IReadOnlyList<PricingBasisRecord>> ListPricingBasisRecordsAsync(
        CustomerOrganizationId customerOrganizationId)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT pricing_basis_id, customer_organization_id, harness, provider_name, model_name, token_type, billing_route,
                   currency, price_per_million_tokens, pricing_version, source_kind, review_state, effective_from_utc,
                   effective_to_utc, audit_event_id, source_metadata_json::text, created_at_utc, updated_at_utc
            FROM pricing_basis
            WHERE customer_organization_id = @customer_organization_id
            ORDER BY provider_name, model_name, token_type, billing_route, effective_from_utc DESC, pricing_basis_id
            """);
        command.Parameters.AddWithValue("customer_organization_id", customerOrganizationId.Value);
        await using var reader = await command.ExecuteReaderAsync();
        var records = new List<PricingBasisRecord>();
        while (await reader.ReadAsync())
        {
            records.Add(ReadPricingBasisRecord(reader));
        }

        return records;
    }

    public Task<PricingBasisRecord> ApprovePricingBasisAsync(
        PricingBasisReviewRequest request,
        ProductUserId actorProductUserId,
        ProductRole actorEffectiveRole)
    {
        return ReviewPricingBasisAsync(request, actorProductUserId, actorEffectiveRole, PricingReviewState.Approved, "approved");
    }

    public Task<PricingBasisRecord> RejectPricingBasisAsync(
        PricingBasisReviewRequest request,
        ProductUserId actorProductUserId,
        ProductRole actorEffectiveRole)
    {
        return ReviewPricingBasisAsync(request, actorProductUserId, actorEffectiveRole, PricingReviewState.Rejected, "rejected");
    }

    public Task<PricingBasisRecord> SupersedePricingBasisAsync(
        PricingBasisReviewRequest request,
        ProductUserId actorProductUserId,
        ProductRole actorEffectiveRole)
    {
        return ReviewPricingBasisAsync(request, actorProductUserId, actorEffectiveRole, PricingReviewState.Superseded, "superseded");
    }

    public async Task<CostEstimateRecord> RecordCostEstimateAsync(CreateCostEstimateRecordRequest request)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await RequireCustomerOrganizationAsync(connection, transaction, request.CustomerOrganizationId);
        await RequireAgentSessionAsync(connection, transaction, request.CustomerOrganizationId, request.AgentSessionId.Trim());
        if (!string.IsNullOrWhiteSpace(request.PricingBasisId))
        {
            await RequirePricingBasisAsync(connection, transaction, request.CustomerOrganizationId, request.PricingBasisId.Trim());
        }

        var record = new CostEstimateRecord(
            $"cost-estimate-{Guid.NewGuid():N}",
            request.CustomerOrganizationId,
            request.AgentSessionId.Trim(),
            string.IsNullOrWhiteSpace(request.ModelInvocationId) ? null : request.ModelInvocationId.Trim(),
            string.IsNullOrWhiteSpace(request.PricingBasisId) ? null : request.PricingBasisId.Trim(),
            string.IsNullOrWhiteSpace(request.PricingVersion) ? null : request.PricingVersion.Trim(),
            request.Currency.Trim().ToUpperInvariant(),
            request.EstimatedCost,
            request.CostStatus,
            request.SourceKind,
            request.TokenMetricStatus,
            request.TokenMetricConfidence,
            request.ProviderName.Trim(),
            request.ModelName.Trim(),
            request.BillingRoute.Trim(),
            request.TokenType,
            clock.UtcNow.ToUniversalTime());
        await InsertCostEstimateAsync(connection, transaction, record);
        await transaction.CommitAsync();
        return record;
    }

    public async Task<IReadOnlyList<CostEstimateRecord>> ListCostEstimatesAsync(
        CustomerOrganizationId customerOrganizationId)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT cost_estimate_id, customer_organization_id, agent_session_id, model_invocation_id, pricing_basis_id,
                   pricing_version, currency, estimated_cost, cost_status, source_kind, token_metric_status,
                   token_metric_confidence, provider_name, model_name, billing_route, token_type, created_at_utc
            FROM cost_estimate
            WHERE customer_organization_id = @customer_organization_id
            ORDER BY created_at_utc, cost_estimate_id
            """);
        command.Parameters.AddWithValue("customer_organization_id", customerOrganizationId.Value);
        await using var reader = await command.ExecuteReaderAsync();
        var estimates = new List<CostEstimateRecord>();
        while (await reader.ReadAsync())
        {
            estimates.Add(ReadCostEstimateRecord(reader));
        }

        return estimates;
    }

    public async Task<IReadOnlyList<CostMixBucket>> ListCostMixAsync(
        CustomerOrganizationId customerOrganizationId)
    {
        var estimates = await ListCostEstimatesAsync(customerOrganizationId);
        return estimates
            .GroupBy(static estimate => new
            {
                estimate.ProviderName,
                estimate.ModelName,
                estimate.BillingRoute,
                estimate.TokenType,
                estimate.CostStatus,
                estimate.Currency
            })
            .Select(static group => new CostMixBucket(
                group.Key.ProviderName,
                group.Key.ModelName,
                group.Key.BillingRoute,
                group.Key.TokenType,
                group.Key.CostStatus,
                group.Key.Currency,
                group.Any(estimate => estimate.EstimatedCost is null)
                    ? null
                    : group.Sum(estimate => estimate.EstimatedCost),
                group.Count(),
                group.Select(estimate => estimate.TokenMetricStatus).Distinct().Count() == 1
                    ? group.First().TokenMetricStatus
                    : TokenMetricStatus.Mixed))
            .OrderBy(static bucket => bucket.ProviderName, StringComparer.Ordinal)
            .ThenBy(static bucket => bucket.ModelName, StringComparer.Ordinal)
            .ThenBy(static bucket => bucket.TokenType)
            .ThenBy(static bucket => bucket.BillingRoute, StringComparer.Ordinal)
            .ThenBy(static bucket => bucket.CostStatus)
            .ToArray();
    }

    public async Task<CostEstimateRecord> EstimateAndRecordTokenObservationCostAsync(
        EstimateTokenObservationCostRequest request)
    {
        var records = await ListPricingBasisRecordsAsync(request.CustomerOrganizationId);
        return await RecordCostEstimateAsync(PricingCostCalculator.EstimateTokenObservationCost(request, records));
    }

    public Task<IReadOnlyList<RecommendationRecord>> ListRecommendationsForSessionAsync(
        CustomerOrganizationId customerOrganizationId,
        string agentSessionId)
    {
        throw CreateUnsupportedProductRuntimeSurfaceException();
    }

    public Task<RecommendationRecord?> FindRecommendationAsync(
        CustomerOrganizationId customerOrganizationId,
        RecommendationId recommendationId)
    {
        throw CreateUnsupportedProductRuntimeSurfaceException();
    }

    public Task<RecommendationRegenerationRequest> CreateRecommendationRegenerationRequestAsync(
        CreateRecommendationRegenerationRequest request,
        ProductUserId actorProductUserId,
        ProductRole actorEffectiveRole)
    {
        throw CreateUnsupportedProductRuntimeSurfaceException();
    }

    public Task<ProductApiIdempotencyReservation> ReserveProductApiIdempotencyRecordAsync(
        ReserveProductApiIdempotencyRecordRequest request)
    {
        return idempotencyStore.ReserveProductApiIdempotencyRecordAsync(request);
    }

    public Task<ProductApiIdempotencyRecord> CompleteProductApiIdempotencyRecordAsync(
        CompleteProductApiIdempotencyRecordRequest request)
    {
        return idempotencyStore.CompleteProductApiIdempotencyRecordAsync(request);
    }

    private async Task<PricingBasisRecord> ReviewPricingBasisAsync(
        PricingBasisReviewRequest request,
        ProductUserId actorProductUserId,
        ProductRole actorEffectiveRole,
        PricingReviewState reviewState,
        string auditResult)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await RequireCustomerOrganizationAsync(connection, transaction, request.CustomerOrganizationId);
        await RequireProductUserAsync(connection, transaction, request.CustomerOrganizationId, actorProductUserId);
        var existing = await FindPricingBasisAsync(connection, transaction, request.CustomerOrganizationId, request.PricingBasisId.Trim()) ??
            throw new InvalidOperationException("Pricing basis does not belong to the customer organization.");
        if (existing.ReviewState != PricingReviewState.Candidate &&
            (reviewState != PricingReviewState.Superseded || existing.ReviewState != PricingReviewState.Approved))
        {
            throw new InvalidOperationException("Only candidate pricing basis records, or approved records being superseded, can be reviewed.");
        }

        var now = clock.UtcNow.ToUniversalTime();
        var audit = CreatePricingAuditEvent(existing, actorProductUserId, actorEffectiveRole, $"pricing_basis_{auditResult}", auditResult, request.CorrelationId, request.AuditEventId, reviewState, now);
        await InsertGovernanceAuditEventAsync(connection, transaction, audit);
        var effectiveToUtc = reviewState == PricingReviewState.Superseded &&
            existing.ReviewState == PricingReviewState.Approved
            ? ClosePricingEffectiveWindow(existing, now)
            : existing.EffectiveToUtc;

        if (reviewState == PricingReviewState.Approved)
        {
            await using var supersede = new NpgsqlCommand("""
                UPDATE pricing_basis
                SET review_state = 'superseded',
                    effective_to_utc = CASE
                        WHEN @effective_to_utc > effective_from_utc THEN @effective_to_utc
                        ELSE effective_from_utc + interval '1 millisecond'
                    END,
                    updated_at_utc = @updated_at_utc
                WHERE customer_organization_id = @customer_organization_id
                  AND pricing_basis_id <> @pricing_basis_id
                  AND review_state = 'approved'
                  AND harness = @harness
                  AND provider_name = @provider_name
                  AND model_name = @model_name
                  AND billing_route = @billing_route
                  AND effective_from_utc <= @effective_to_utc
                  AND (effective_to_utc IS NULL OR effective_to_utc > @effective_to_utc)
                  AND token_type = @token_type
                  AND source_kind = @source_kind
                """, connection, transaction);
            supersede.Parameters.AddWithValue("customer_organization_id", existing.CustomerOrganizationId.Value);
            supersede.Parameters.AddWithValue("pricing_basis_id", existing.PricingBasisId);
            supersede.Parameters.AddWithValue("effective_to_utc", existing.EffectiveFromUtc);
            supersede.Parameters.AddWithValue("updated_at_utc", now);
            supersede.Parameters.AddWithValue("harness", existing.Harness);
            supersede.Parameters.AddWithValue("provider_name", existing.ProviderName);
            supersede.Parameters.AddWithValue("model_name", existing.ModelName);
            supersede.Parameters.AddWithValue("billing_route", existing.BillingRoute);
            supersede.Parameters.AddWithValue("token_type", ToWirePricingTokenType(existing.TokenType));
            supersede.Parameters.AddWithValue("source_kind", ToWirePricingSourceKind(existing.SourceKind));
            await supersede.ExecuteNonQueryAsync();
        }

        await using (var update = new NpgsqlCommand("""
            UPDATE pricing_basis
            SET review_state = @review_state,
                effective_to_utc = @effective_to_utc,
                audit_event_id = @audit_event_id,
                updated_at_utc = @updated_at_utc
            WHERE customer_organization_id = @customer_organization_id
              AND pricing_basis_id = @pricing_basis_id
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("review_state", ToWirePricingReviewState(reviewState));
            update.Parameters.Add("effective_to_utc", NpgsqlDbType.TimestampTz).Value = (object?)effectiveToUtc ?? DBNull.Value;
            update.Parameters.AddWithValue("audit_event_id", audit.AuditEventId);
            update.Parameters.AddWithValue("updated_at_utc", now);
            update.Parameters.AddWithValue("customer_organization_id", request.CustomerOrganizationId.Value);
            update.Parameters.AddWithValue("pricing_basis_id", existing.PricingBasisId);
            await update.ExecuteNonQueryAsync();
        }

        var updated = await FindPricingBasisAsync(connection, transaction, request.CustomerOrganizationId, existing.PricingBasisId) ??
            throw new InvalidOperationException("Pricing basis was not found after review.");
        await transaction.CommitAsync();
        return updated;
    }

    private static async Task InsertGovernanceAuditEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        GovernanceAuditEvent audit)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO governance_audit_event (
                audit_event_id, customer_organization_id, actor_product_user_id, effective_role, action, target_resource_kind,
                target_resource_id, decision, denial_reason, correlation_id, evidence_metadata_json, created_at_utc)
            VALUES (
                @audit_event_id, @customer_organization_id, @actor_product_user_id, @effective_role, @action, @target_resource_kind,
                @target_resource_id, @decision, @denial_reason, @correlation_id, @evidence_metadata_json, @created_at_utc)
            """, connection, transaction);
        command.Parameters.AddWithValue("audit_event_id", audit.AuditEventId);
        command.Parameters.AddWithValue("customer_organization_id", audit.CustomerOrganizationId.Value);
        command.Parameters.AddWithValue("actor_product_user_id", (object?)audit.ActorProductUserId?.Value ?? DBNull.Value);
        command.Parameters.AddWithValue("effective_role", (object?)audit.EffectiveRole?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("action", audit.Action.ToString());
        command.Parameters.AddWithValue("target_resource_kind", audit.TargetResourceKind);
        command.Parameters.AddWithValue("target_resource_id", audit.TargetResourceId);
        command.Parameters.AddWithValue("decision", audit.Decision);
        command.Parameters.AddWithValue("denial_reason", (object?)audit.DenialReason?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("correlation_id", audit.CorrelationId);
        command.Parameters.Add("evidence_metadata_json", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(audit.EvidenceMetadata);
        command.Parameters.AddWithValue("created_at_utc", audit.CreatedAtUtc);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertPricingBasisRecordAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PricingBasisRecord record)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO pricing_basis (
                pricing_basis_id, customer_organization_id, harness, provider_name, model_name, token_type, billing_route,
                currency, price_per_million_tokens, pricing_version, source_kind, review_state, effective_from_utc,
                effective_to_utc, audit_event_id, source_metadata_json, created_at_utc, updated_at_utc)
            VALUES (
                @pricing_basis_id, @customer_organization_id, @harness, @provider_name, @model_name, @token_type, @billing_route,
                @currency, @price_per_million_tokens, @pricing_version, @source_kind, @review_state, @effective_from_utc,
                @effective_to_utc, @audit_event_id, @source_metadata_json, @created_at_utc, @updated_at_utc)
            """, connection, transaction);
        command.Parameters.AddWithValue("pricing_basis_id", record.PricingBasisId);
        command.Parameters.AddWithValue("customer_organization_id", record.CustomerOrganizationId.Value);
        command.Parameters.AddWithValue("harness", record.Harness);
        command.Parameters.AddWithValue("provider_name", record.ProviderName);
        command.Parameters.AddWithValue("model_name", record.ModelName);
        command.Parameters.AddWithValue("token_type", ToWirePricingTokenType(record.TokenType));
        command.Parameters.AddWithValue("billing_route", record.BillingRoute);
        command.Parameters.AddWithValue("currency", record.Currency);
        command.Parameters.AddWithValue("price_per_million_tokens", record.PricePerMillionTokens);
        command.Parameters.AddWithValue("pricing_version", record.PricingVersion);
        command.Parameters.AddWithValue("source_kind", ToWirePricingSourceKind(record.SourceKind));
        command.Parameters.AddWithValue("review_state", ToWirePricingReviewState(record.ReviewState));
        command.Parameters.AddWithValue("effective_from_utc", record.EffectiveFromUtc);
        command.Parameters.AddWithValue("effective_to_utc", (object?)record.EffectiveToUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("audit_event_id", record.AuditEventId);
        command.Parameters.Add("source_metadata_json", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(record.SourceMetadata);
        command.Parameters.AddWithValue("created_at_utc", record.CreatedAtUtc);
        command.Parameters.AddWithValue("updated_at_utc", record.UpdatedAtUtc);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertCostEstimateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CostEstimateRecord record)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO cost_estimate (
                cost_estimate_id, customer_organization_id, agent_session_id, model_invocation_id, pricing_basis_id,
                pricing_version, currency, estimated_cost, cost_status, source_kind, token_metric_status,
                token_metric_confidence, provider_name, model_name, billing_route, token_type, created_at_utc)
            VALUES (
                @cost_estimate_id, @customer_organization_id, @agent_session_id, @model_invocation_id, @pricing_basis_id,
                @pricing_version, @currency, @estimated_cost, @cost_status, @source_kind, @token_metric_status,
                @token_metric_confidence, @provider_name, @model_name, @billing_route, @token_type, @created_at_utc)
            """, connection, transaction);
        command.Parameters.AddWithValue("cost_estimate_id", record.CostEstimateId);
        command.Parameters.AddWithValue("customer_organization_id", record.CustomerOrganizationId.Value);
        command.Parameters.AddWithValue("agent_session_id", record.AgentSessionId);
        command.Parameters.AddWithValue("model_invocation_id", (object?)record.ModelInvocationId ?? DBNull.Value);
        command.Parameters.AddWithValue("pricing_basis_id", (object?)record.PricingBasisId ?? DBNull.Value);
        command.Parameters.AddWithValue("pricing_version", (object?)record.PricingVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("currency", record.Currency);
        command.Parameters.AddWithValue("estimated_cost", (object?)record.EstimatedCost ?? DBNull.Value);
        command.Parameters.AddWithValue("cost_status", ToWireCostStatus(record.CostStatus));
        command.Parameters.AddWithValue("source_kind", ToWireCostEstimateSourceKind(record.SourceKind));
        command.Parameters.AddWithValue("token_metric_status", ToWireTokenMetricStatus(record.TokenMetricStatus));
        command.Parameters.AddWithValue("token_metric_confidence", ToWireTokenMetricConfidence(record.TokenMetricConfidence));
        command.Parameters.AddWithValue("provider_name", record.ProviderName);
        command.Parameters.AddWithValue("model_name", record.ModelName);
        command.Parameters.AddWithValue("billing_route", record.BillingRoute);
        command.Parameters.AddWithValue("token_type", ToWirePricingTokenType(record.TokenType));
        command.Parameters.AddWithValue("created_at_utc", record.CreatedAtUtc);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<IdentityTenant?> FindIdentityTenantByIdAsync(
        CustomerOrganizationId customerOrganizationId,
        IdentityTenantId identityTenantId)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT identity_tenant_id, customer_organization_id, provider, issuer, external_tenant_id, allowed_audiences_json::text,
                   jwks_uri, display_name, status, last_validated_at_utc, created_at_utc, updated_at_utc
            FROM identity_tenant
            WHERE customer_organization_id = @customer_organization_id
              AND identity_tenant_id = @identity_tenant_id
              AND status = 'active'
            """);
        command.Parameters.AddWithValue("customer_organization_id", customerOrganizationId.Value);
        command.Parameters.AddWithValue("identity_tenant_id", identityTenantId.Value);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadIdentityTenant(reader) : null;
    }

    private async Task<ProductUser?> FindProductUserByExternalSubjectAsync(
        CustomerOrganizationId customerOrganizationId,
        IdentityTenantId identityTenantId,
        string externalSubjectId)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT product_user_id, customer_organization_id, identity_tenant_id, external_subject_id, display_label,
                   email, status, first_seen_at_utc, last_seen_at_utc, created_at_utc, updated_at_utc
            FROM product_user
            WHERE customer_organization_id = @customer_organization_id
              AND identity_tenant_id = @identity_tenant_id
              AND external_subject_id = @external_subject_id
              AND status = 'active'
            """);
        command.Parameters.AddWithValue("customer_organization_id", customerOrganizationId.Value);
        command.Parameters.AddWithValue("identity_tenant_id", identityTenantId.Value);
        command.Parameters.AddWithValue("external_subject_id", externalSubjectId.Trim());
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadProductUser(reader) : null;
    }

    private async Task<IReadOnlyList<ProductRoleMapping>> ListActiveProductRoleMappingsAsync(
        CustomerOrganizationId customerOrganizationId,
        IdentityTenantId identityTenantId)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT product_role_mapping_id, customer_organization_id, identity_tenant_id, external_principal_type,
                   external_principal_id, product_role, scope_kind, scope_id, status, effective_from_utc,
                   effective_to_utc, created_by_product_user_id, changed_by_product_user_id, audit_event_id,
                   created_at_utc, updated_at_utc
            FROM product_role_mapping
            WHERE customer_organization_id = @customer_organization_id
              AND identity_tenant_id = @identity_tenant_id
              AND status = 'active'
              AND effective_from_utc <= @now
              AND (effective_to_utc IS NULL OR effective_to_utc > @now)
            """);
        command.Parameters.AddWithValue("customer_organization_id", customerOrganizationId.Value);
        command.Parameters.AddWithValue("identity_tenant_id", identityTenantId.Value);
        command.Parameters.AddWithValue("now", clock.UtcNow.ToUniversalTime());
        await using var reader = await command.ExecuteReaderAsync();
        var mappings = new List<ProductRoleMapping>();
        while (await reader.ReadAsync())
        {
            mappings.Add(ReadProductRoleMapping(reader));
        }

        return mappings;
    }

    private static async Task RequireCustomerOrganizationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CustomerOrganizationId customerOrganizationId)
    {
        await using var command = new NpgsqlCommand("""
            SELECT 1
            FROM customer_organization
            WHERE customer_organization_id = @customer_organization_id
              AND status = 'active'
            """, connection, transaction);
        command.Parameters.AddWithValue("customer_organization_id", customerOrganizationId.Value);
        if (await command.ExecuteScalarAsync() is null)
        {
            throw new InvalidOperationException("Customer organization is not active.");
        }
    }

    private static async Task RequireProductUserAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CustomerOrganizationId customerOrganizationId,
        ProductUserId productUserId)
    {
        await using var command = new NpgsqlCommand("""
            SELECT 1
            FROM product_user
            WHERE customer_organization_id = @customer_organization_id
              AND product_user_id = @product_user_id
            """, connection, transaction);
        command.Parameters.AddWithValue("customer_organization_id", customerOrganizationId.Value);
        command.Parameters.AddWithValue("product_user_id", productUserId.Value);
        if (await command.ExecuteScalarAsync() is null)
        {
            throw new InvalidOperationException("Product user does not belong to the customer organization.");
        }
    }

    private static async Task RequireAgentSessionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CustomerOrganizationId customerOrganizationId,
        string agentSessionId)
    {
        await using var command = new NpgsqlCommand("""
            SELECT 1
            FROM agent_session
            WHERE customer_organization_id = @customer_organization_id
              AND agent_session_id = @agent_session_id
            """, connection, transaction);
        command.Parameters.AddWithValue("customer_organization_id", customerOrganizationId.Value);
        command.Parameters.AddWithValue("agent_session_id", agentSessionId);
        if (await command.ExecuteScalarAsync() is null)
        {
            throw new InvalidOperationException("Cost estimate session does not belong to the customer organization.");
        }
    }

    private static async Task RequirePricingBasisAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CustomerOrganizationId customerOrganizationId,
        string pricingBasisId)
    {
        if (await FindPricingBasisAsync(connection, transaction, customerOrganizationId, pricingBasisId) is null)
        {
            throw new InvalidOperationException("Cost estimate pricing basis does not belong to the customer organization.");
        }
    }

    private static async Task<GovernanceAuditEvent?> FindGovernanceAuditEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CustomerOrganizationId customerOrganizationId,
        string auditEventId)
    {
        await using var command = new NpgsqlCommand("""
            SELECT audit_event_id, customer_organization_id, actor_product_user_id, effective_role, action, target_resource_kind,
                   target_resource_id, decision, denial_reason, correlation_id, evidence_metadata_json::text, created_at_utc
            FROM governance_audit_event
            WHERE customer_organization_id = @customer_organization_id
              AND audit_event_id = @audit_event_id
            """, connection, transaction);
        command.Parameters.AddWithValue("customer_organization_id", customerOrganizationId.Value);
        command.Parameters.AddWithValue("audit_event_id", auditEventId);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadGovernanceAuditEvent(reader) : null;
    }

    private static async Task<PricingBasisRecord?> FindPricingBasisAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CustomerOrganizationId customerOrganizationId,
        string pricingBasisId)
    {
        await using var command = new NpgsqlCommand("""
            SELECT pricing_basis_id, customer_organization_id, harness, provider_name, model_name, token_type, billing_route,
                   currency, price_per_million_tokens, pricing_version, source_kind, review_state, effective_from_utc,
                   effective_to_utc, audit_event_id, source_metadata_json::text, created_at_utc, updated_at_utc
            FROM pricing_basis
            WHERE customer_organization_id = @customer_organization_id
              AND pricing_basis_id = @pricing_basis_id
            """, connection, transaction);
        command.Parameters.AddWithValue("customer_organization_id", customerOrganizationId.Value);
        command.Parameters.AddWithValue("pricing_basis_id", pricingBasisId);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadPricingBasisRecord(reader) : null;
    }

    private static CustomerOrganization ReadCustomerOrganization(NpgsqlDataReader reader)
    {
        return new CustomerOrganization(
            new CustomerOrganizationId(reader.GetGuid(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            FromWireCustomerOrganizationIsolationTier(reader.GetString(4)),
            FromWireCustomerOrganizationStatus(reader.GetString(5)),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetFieldValue<DateTimeOffset>(7));
    }

    private static IdentityTenant ReadIdentityTenant(NpgsqlDataReader reader)
    {
        return new IdentityTenant(
            new IdentityTenantId(reader.GetGuid(0)),
            new CustomerOrganizationId(reader.GetGuid(1)),
            FromWireIdentityTenantProvider(reader.GetString(2)),
            reader.GetString(3),
            reader.GetString(4),
            JsonSerializer.Deserialize<string[]>(reader.GetString(5)) ?? [],
            reader.IsDBNull(6) ? null : new Uri(reader.GetString(6)),
            reader.GetString(7),
            FromWireIdentityTenantStatus(reader.GetString(8)),
            reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
            reader.GetFieldValue<DateTimeOffset>(10),
            reader.GetFieldValue<DateTimeOffset>(11));
    }

    private static ProductUser ReadProductUser(NpgsqlDataReader reader)
    {
        return new ProductUser(
            new ProductUserId(reader.GetGuid(0)),
            new CustomerOrganizationId(reader.GetGuid(1)),
            new IdentityTenantId(reader.GetGuid(2)),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            FromWireProductUserStatus(reader.GetString(6)),
            reader.GetFieldValue<DateTimeOffset>(7),
            reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
            reader.GetFieldValue<DateTimeOffset>(9),
            reader.GetFieldValue<DateTimeOffset>(10));
    }

    private static ProductRoleMapping ReadProductRoleMapping(NpgsqlDataReader reader)
    {
        return new ProductRoleMapping(
            new ProductRoleMappingId(reader.GetGuid(0)),
            new CustomerOrganizationId(reader.GetGuid(1)),
            new IdentityTenantId(reader.GetGuid(2)),
            FromWireExternalPrincipalType(reader.GetString(3)),
            reader.GetString(4),
            Enum.Parse<ProductRole>(reader.GetString(5), ignoreCase: false),
            FromWireProductScopeKind(reader.GetString(6)),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            FromWireProductRoleMappingStatus(reader.GetString(8)),
            reader.GetFieldValue<DateTimeOffset>(9),
            reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
            new ProductUserId(reader.GetGuid(11)),
            new ProductUserId(reader.GetGuid(12)),
            reader.GetString(13),
            reader.GetFieldValue<DateTimeOffset>(14),
            reader.GetFieldValue<DateTimeOffset>(15));
    }

    private static GovernanceAuditEvent ReadGovernanceAuditEvent(NpgsqlDataReader reader)
    {
        var denialReason = reader.IsDBNull(8)
            ? (ProductAuthorizationDenialReason?)null
            : Enum.Parse<ProductAuthorizationDenialReason>(reader.GetString(8), ignoreCase: false);
        return new GovernanceAuditEvent(
            reader.GetString(0),
            new CustomerOrganizationId(reader.GetGuid(1)),
            reader.IsDBNull(2) ? null : new ProductUserId(reader.GetGuid(2)),
            reader.IsDBNull(3) ? null : Enum.Parse<ProductRole>(reader.GetString(3), ignoreCase: false),
            Enum.Parse<ProductAuthorizationAction>(reader.GetString(4), ignoreCase: false),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            denialReason,
            reader.GetString(9),
            JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(10)) ?? new Dictionary<string, string>(StringComparer.Ordinal),
            reader.GetFieldValue<DateTimeOffset>(11));
    }

    private static PricingBasisRecord ReadPricingBasisRecord(NpgsqlDataReader reader)
    {
        return new PricingBasisRecord(
            reader.GetString(0),
            new CustomerOrganizationId(reader.GetGuid(1)),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            FromWirePricingTokenType(reader.GetString(5)),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetDecimal(8),
            reader.GetString(9),
            FromWirePricingSourceKind(reader.GetString(10)),
            FromWirePricingReviewState(reader.GetString(11)),
            reader.GetFieldValue<DateTimeOffset>(12),
            reader.IsDBNull(13) ? null : reader.GetFieldValue<DateTimeOffset>(13),
            reader.GetString(14),
            JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(15)) ?? new Dictionary<string, string>(StringComparer.Ordinal),
            reader.GetFieldValue<DateTimeOffset>(16),
            reader.GetFieldValue<DateTimeOffset>(17));
    }

    private static CostEstimateRecord ReadCostEstimateRecord(NpgsqlDataReader reader)
    {
        return new CostEstimateRecord(
            reader.GetString(0),
            new CustomerOrganizationId(reader.GetGuid(1)),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetDecimal(7),
            FromWireCostStatus(reader.GetString(8)),
            FromWireCostEstimateSourceKind(reader.GetString(9)),
            FromWireTokenMetricStatus(reader.GetString(10)),
            FromWireTokenMetricConfidence(reader.GetString(11)),
            reader.GetString(12),
            reader.GetString(13),
            reader.GetString(14),
            FromWirePricingTokenType(reader.GetString(15)),
            reader.GetFieldValue<DateTimeOffset>(16));
    }

    private static GovernanceAuditEvent CreatePricingAuditEvent(
        PricingBasisRecord record,
        ProductUserId? actorProductUserId,
        ProductRole? actorEffectiveRole,
        string operation,
        string result,
        string correlationId,
        string? auditEventId = null,
        PricingReviewState? reviewState = null,
        DateTimeOffset? createdAtUtc = null)
    {
        return new GovernanceAuditEvent(
            auditEventId ?? record.AuditEventId,
            record.CustomerOrganizationId,
            actorProductUserId,
            actorEffectiveRole,
            ProductAuthorizationAction.PricingManage,
            ProductScopeKind.Pricing.ToString(),
            record.PricingBasisId,
            result == "created" || operation == "pricing_seed_refresh" ? "created" : "updated",
            DenialReason: null,
            correlationId.Trim(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["evidence_kind"] = "admin_operation",
                ["operation"] = operation,
                ["result"] = result,
                ["pricing_basis_id"] = record.PricingBasisId,
                ["pricing_version"] = record.PricingVersion,
                ["provider_name"] = record.ProviderName,
                ["model_name"] = record.ModelName,
                ["token_type"] = ToWirePricingTokenType(record.TokenType),
                ["billing_route"] = record.BillingRoute,
                ["source_kind"] = ToWirePricingSourceKind(record.SourceKind),
                ["review_state"] = ToWirePricingReviewState(reviewState ?? record.ReviewState)
            },
            createdAtUtc ?? record.CreatedAtUtc);
    }

    private static DateTimeOffset ClosePricingEffectiveWindow(PricingBasisRecord existing, DateTimeOffset now)
    {
        if (existing.EffectiveToUtc is not null && existing.EffectiveToUtc <= now)
        {
            return existing.EffectiveToUtc.Value;
        }

        return now > existing.EffectiveFromUtc
            ? now
            : existing.EffectiveFromUtc.AddMilliseconds(1);
    }

    private static NotSupportedException CreateUnsupportedProductRuntimeSurfaceException()
    {
        return new NotSupportedException("PostgreSQL tenant metadata runtime support is limited to pricing, audit, authorization, and idempotency in this slice.");
    }

    private static NormalizedPricingBasisRequest NormalizePricingBasisRequest(
        CreatePricingBasisRecordRequest request)
    {
        var harness = NormalizePricingHarness(request.Harness, nameof(request.Harness));
        var providerName = NormalizePricingLabel(request.ProviderName, nameof(request.ProviderName));
        var modelName = NormalizePricingLabel(request.ModelName, nameof(request.ModelName));
        var billingRoute = NormalizePricingLabel(request.BillingRoute, nameof(request.BillingRoute));
        var currency = NormalizeCurrency(request.Currency, nameof(request.Currency));
        var pricingVersion = NormalizePricingVersion(request.PricingVersion, nameof(request.PricingVersion));
        var auditEventId = NormalizeRequiredText(request.AuditEventId, nameof(request.AuditEventId));
        var sourceMetadata = NormalizePricingSourceMetadata(request.SourceMetadata);

        if (request.PricePerMillionTokens < 0)
        {
            throw new ArgumentException("Pricing rates must be non-negative.", nameof(request.PricePerMillionTokens));
        }

        if (request.EffectiveToUtc is not null &&
            request.EffectiveToUtc.Value.ToUniversalTime() <= request.EffectiveFromUtc.ToUniversalTime())
        {
            throw new ArgumentException("Pricing effective end must be after start.", nameof(request.EffectiveToUtc));
        }

        return new NormalizedPricingBasisRequest(
            harness,
            providerName,
            modelName,
            billingRoute,
            currency,
            pricingVersion,
            auditEventId,
            sourceMetadata);
    }

    private static IReadOnlyDictionary<string, string> NormalizePricingSourceMetadata(
        IReadOnlyDictionary<string, string> sourceMetadata)
    {
        ArgumentNullException.ThrowIfNull(sourceMetadata);

        if (sourceMetadata.Count == 0)
        {
            throw new ArgumentException("Pricing source metadata is required.", nameof(sourceMetadata));
        }

        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in sourceMetadata.OrderBy(static entry => entry.Key, StringComparer.Ordinal))
        {
            var normalizedKey = NormalizeRequiredText(key, nameof(sourceMetadata));
            var normalizedValue = NormalizeRequiredText(value, nameof(sourceMetadata));

            if (normalizedKey.Length > 128 ||
                normalizedValue.Length > 512 ||
                ContainsSensitiveFragment(normalizedKey) ||
                ContainsSensitiveFragment(normalizedValue))
            {
                throw new ArgumentException("Pricing source metadata must not contain sensitive values.", nameof(sourceMetadata));
            }

            var isAllowed = normalizedKey switch
            {
                "source_url" => Uri.TryCreate(normalizedValue, UriKind.Absolute, out var uri) &&
                    uri.Scheme == Uri.UriSchemeHttps,
                "source_retrieved_at_utc" => DateTimeOffset.TryParse(normalizedValue, out _),
                "provider_sku_name" or
                "meter_id" or
                "region" or
                "billing_route" or
                "provider_document_version" => normalizedValue.All(static character =>
                    char.IsAsciiLetterOrDigit(character) ||
                    character is '-' or '_' or ':' or '.' or '/' or ' '),
                _ => false
            };

            if (!isAllowed)
            {
                throw new ArgumentException("Pricing source metadata key or value is not supported.", nameof(sourceMetadata));
            }

            normalized.Add(normalizedKey, normalizedValue);
        }

        return new ReadOnlyDictionary<string, string>(normalized);
    }

    private static string NormalizePricingHarness(string value, string parameterName)
    {
        var normalized = NormalizeRequiredText(value, parameterName).ToLowerInvariant();
        return normalized is "codex-cli"
            ? normalized
            : throw new ArgumentException("Pricing harness is not supported.", parameterName);
    }

    private static string NormalizePricingLabel(string value, string parameterName)
    {
        var normalized = NormalizeRequiredText(value, parameterName);
        return normalized.Length <= 128 &&
            normalized.All(static character =>
                char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_' or ':' or '.' or '/')
            ? normalized
            : throw new ArgumentException("Pricing label is not allowed.", parameterName);
    }

    private static string NormalizeCurrency(string value, string parameterName)
    {
        var normalized = NormalizeRequiredText(value, parameterName).ToUpperInvariant();
        return normalized.Length == 3 && normalized.All(static character => char.IsAsciiLetterUpper(character))
            ? normalized
            : throw new ArgumentException("Currency must use a three-letter code.", parameterName);
    }

    private static string NormalizePricingVersion(string value, string parameterName)
    {
        var normalized = NormalizeRequiredText(value, parameterName);
        return normalized.Length <= 128 &&
            normalized.All(static character =>
                char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_' or ':' or '.' or '/')
            ? normalized
            : throw new ArgumentException("Pricing version is not allowed.", parameterName);
    }

    private static string NormalizeRequiredText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        return value.Trim();
    }

    private static bool ContainsSensitiveFragment(string value)
    {
        return SensitiveEvidenceValueFragments.Any(fragment =>
                value.Contains(fragment, StringComparison.OrdinalIgnoreCase)) ||
            SensitiveEvidenceKeyFragments.Any(fragment =>
                value.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetTargetResourceId(CustomerOrganizationId customerOrganizationId, ProductScope scope)
    {
        return scope.Kind == ProductScopeKind.Organization
            ? customerOrganizationId.ToString()
            : scope.ScopeId ?? scope.Kind.ToString();
    }

    private static bool PrincipalMatches(ProductRoleMapping mapping, AuthenticatedTokenClaims claims)
    {
        return mapping.ExternalPrincipalType switch
        {
            ExternalPrincipalType.GroupObjectId => claims.GroupObjectIds.Contains(mapping.ExternalPrincipalId, StringComparer.Ordinal),
            ExternalPrincipalType.AppRole => claims.AppRoles.Contains(mapping.ExternalPrincipalId, StringComparer.Ordinal),
            ExternalPrincipalType.UserSubject => StringComparer.Ordinal.Equals(claims.Subject, mapping.ExternalPrincipalId),
            ExternalPrincipalType.ServicePrincipal => StringComparer.Ordinal.Equals(claims.Subject, mapping.ExternalPrincipalId),
            _ => false
        };
    }

    private static bool ScopeMatches(ProductRoleMapping mapping, ProductScope requestedScope)
    {
        if (mapping.ScopeKind == ProductScopeKind.Organization)
        {
            return true;
        }

        if (mapping.ScopeKind == ProductScopeKind.Self)
        {
            return requestedScope.Kind == ProductScopeKind.Self &&
                string.IsNullOrWhiteSpace(mapping.ScopeId) &&
                string.IsNullOrWhiteSpace(requestedScope.ScopeId);
        }

        if (string.IsNullOrWhiteSpace(requestedScope.ScopeId))
        {
            return false;
        }

        return mapping.ScopeKind == requestedScope.Kind &&
            StringComparer.Ordinal.Equals(mapping.ScopeId, requestedScope.ScopeId.Trim());
    }

    private static bool RoleAllowsAction(ProductRole role, ProductAuthorizationAction action)
    {
        return role switch
        {
            _ when action == ProductAuthorizationAction.CurrentUserRead => true,
            ProductRole.PlatformAdmin => action is
                ProductAuthorizationAction.TenantRead or
                ProductAuthorizationAction.TenantUpdate or
                ProductAuthorizationAction.OverviewRead or
                ProductAuthorizationAction.IdentityManage or
                ProductAuthorizationAction.HarnessProfileManage or
                ProductAuthorizationAction.IngestionCredentialManage or
                ProductAuthorizationAction.SessionReadScoped or
                ProductAuthorizationAction.SessionInvestigate or
                ProductAuthorizationAction.RecommendationRead or
                ProductAuthorizationAction.RecommendationRegenerate or
                ProductAuthorizationAction.PricingManage or
                ProductAuthorizationAction.BudgetManage or
                ProductAuthorizationAction.AuditRead,
            ProductRole.SecurityReviewer => action is
                ProductAuthorizationAction.OverviewRead or
                ProductAuthorizationAction.ContentReviewRead or
                ProductAuthorizationAction.ContentReviewDecide or
                ProductAuthorizationAction.SessionInvestigate or
                ProductAuthorizationAction.RecommendationRead or
                ProductAuthorizationAction.RecommendationRegenerate or
                ProductAuthorizationAction.AuditRead,
            ProductRole.EngineeringLead => action is
                ProductAuthorizationAction.OverviewRead or
                ProductAuthorizationAction.SessionReadScoped or
                ProductAuthorizationAction.SessionInvestigate or
                ProductAuthorizationAction.RecommendationRead or
                ProductAuthorizationAction.RecommendationRegenerate or
                ProductAuthorizationAction.BudgetManage,
            ProductRole.Developer => action is
                ProductAuthorizationAction.OverviewRead or
                ProductAuthorizationAction.SessionReadOwn or
                ProductAuthorizationAction.RecommendationRead or
                ProductAuthorizationAction.RecommendationRegenerate,
            ProductRole.ReadOnlyViewer => action is ProductAuthorizationAction.OverviewRead,
            _ => false
        };
    }

    private static CustomerOrganizationIsolationTier FromWireCustomerOrganizationIsolationTier(string value)
    {
        return value switch
        {
            "shared" => CustomerOrganizationIsolationTier.Shared,
            "dedicated_data" => CustomerOrganizationIsolationTier.DedicatedData,
            "dedicated_cell" => CustomerOrganizationIsolationTier.DedicatedCell,
            _ => throw new InvalidOperationException("Customer organization isolation tier is not supported.")
        };
    }

    private static CustomerOrganizationStatus FromWireCustomerOrganizationStatus(string value)
    {
        return value switch
        {
            "active" => CustomerOrganizationStatus.Active,
            "suspended" => CustomerOrganizationStatus.Suspended,
            "offboarding" => CustomerOrganizationStatus.Offboarding,
            "deleted" => CustomerOrganizationStatus.Deleted,
            _ => throw new InvalidOperationException("Customer organization status is not supported.")
        };
    }

    private static IdentityTenantProvider FromWireIdentityTenantProvider(string value)
    {
        return value switch
        {
            "microsoft_entra" => IdentityTenantProvider.MicrosoftEntra,
            _ => throw new InvalidOperationException("Identity tenant provider is not supported.")
        };
    }

    private static IdentityTenantStatus FromWireIdentityTenantStatus(string value)
    {
        return value switch
        {
            "active" => IdentityTenantStatus.Active,
            "disabled" => IdentityTenantStatus.Disabled,
            "pending_validation" => IdentityTenantStatus.PendingValidation,
            _ => throw new InvalidOperationException("Identity tenant status is not supported.")
        };
    }

    private static ProductUserStatus FromWireProductUserStatus(string value)
    {
        return value switch
        {
            "active" => ProductUserStatus.Active,
            "disabled" => ProductUserStatus.Disabled,
            "deleted" => ProductUserStatus.Deleted,
            _ => throw new InvalidOperationException("Product user status is not supported.")
        };
    }

    private static ExternalPrincipalType FromWireExternalPrincipalType(string value)
    {
        return value switch
        {
            "app_role" => ExternalPrincipalType.AppRole,
            "group_object_id" => ExternalPrincipalType.GroupObjectId,
            "user_subject" => ExternalPrincipalType.UserSubject,
            "service_principal" => ExternalPrincipalType.ServicePrincipal,
            _ => throw new InvalidOperationException("External principal type is not supported.")
        };
    }

    private static ProductScopeKind FromWireProductScopeKind(string value)
    {
        return value switch
        {
            "organization" => ProductScopeKind.Organization,
            "team" => ProductScopeKind.Team,
            "repository" => ProductScopeKind.Repository,
            "harness_profile" => ProductScopeKind.HarnessProfile,
            "self" => ProductScopeKind.Self,
            "content_review_queue" => ProductScopeKind.ContentReviewQueue,
            "pricing" => ProductScopeKind.Pricing,
            "tenant_admin" => ProductScopeKind.TenantAdmin,
            _ => throw new InvalidOperationException("Product scope kind is not supported.")
        };
    }

    private static ProductRoleMappingStatus FromWireProductRoleMappingStatus(string value)
    {
        return value switch
        {
            "active" => ProductRoleMappingStatus.Active,
            "disabled" => ProductRoleMappingStatus.Disabled,
            "expired" => ProductRoleMappingStatus.Expired,
            _ => throw new InvalidOperationException("Product role mapping status is not supported.")
        };
    }

    private static string ToWirePricingTokenType(PricingTokenType tokenType)
    {
        return tokenType switch
        {
            PricingTokenType.Input => "input",
            PricingTokenType.Output => "output",
            PricingTokenType.CachedInput => "cached_input",
            PricingTokenType.ReasoningOutput => "reasoning_output",
            _ => throw new ArgumentOutOfRangeException(nameof(tokenType), tokenType, null)
        };
    }

    private static PricingTokenType FromWirePricingTokenType(string value)
    {
        return value switch
        {
            "input" => PricingTokenType.Input,
            "output" => PricingTokenType.Output,
            "cached_input" => PricingTokenType.CachedInput,
            "reasoning_output" => PricingTokenType.ReasoningOutput,
            _ => throw new InvalidOperationException("Pricing token type is not supported.")
        };
    }

    private static string ToWirePricingSourceKind(PricingSourceKind sourceKind)
    {
        return sourceKind switch
        {
            PricingSourceKind.AutomatedSeed => "automated_seed",
            PricingSourceKind.AdminOverride => "admin_override",
            PricingSourceKind.ProviderDocs => "provider_docs",
            PricingSourceKind.EnterpriseContract => "enterprise_contract",
            _ => throw new ArgumentOutOfRangeException(nameof(sourceKind), sourceKind, null)
        };
    }

    private static PricingSourceKind FromWirePricingSourceKind(string value)
    {
        return value switch
        {
            "automated_seed" => PricingSourceKind.AutomatedSeed,
            "admin_override" => PricingSourceKind.AdminOverride,
            "provider_docs" => PricingSourceKind.ProviderDocs,
            "enterprise_contract" => PricingSourceKind.EnterpriseContract,
            _ => throw new InvalidOperationException("Pricing source kind is not supported.")
        };
    }

    private static string ToWirePricingReviewState(PricingReviewState reviewState)
    {
        return reviewState switch
        {
            PricingReviewState.Candidate => "candidate",
            PricingReviewState.Approved => "approved",
            PricingReviewState.Rejected => "rejected",
            PricingReviewState.Superseded => "superseded",
            _ => throw new ArgumentOutOfRangeException(nameof(reviewState), reviewState, null)
        };
    }

    private static PricingReviewState FromWirePricingReviewState(string value)
    {
        return value switch
        {
            "candidate" => PricingReviewState.Candidate,
            "approved" => PricingReviewState.Approved,
            "rejected" => PricingReviewState.Rejected,
            "superseded" => PricingReviewState.Superseded,
            _ => throw new InvalidOperationException("Pricing review state is not supported.")
        };
    }

    private static string ToWireCostStatus(CostEstimateStatus costStatus)
    {
        return costStatus switch
        {
            CostEstimateStatus.Estimated => "estimated",
            CostEstimateStatus.Unavailable => "unavailable",
            CostEstimateStatus.NotApplicable => "not_applicable",
            CostEstimateStatus.Mixed => "mixed",
            _ => throw new ArgumentOutOfRangeException(nameof(costStatus), costStatus, null)
        };
    }

    private static CostEstimateStatus FromWireCostStatus(string value)
    {
        return value switch
        {
            "estimated" => CostEstimateStatus.Estimated,
            "unavailable" => CostEstimateStatus.Unavailable,
            "not_applicable" => CostEstimateStatus.NotApplicable,
            "mixed" => CostEstimateStatus.Mixed,
            _ => throw new InvalidOperationException("Cost estimate status is not supported.")
        };
    }

    private static string ToWireCostEstimateSourceKind(CostEstimateSourceKind sourceKind)
    {
        return sourceKind switch
        {
            CostEstimateSourceKind.DerivedFromObservedTokens => "derived_from_observed_tokens",
            CostEstimateSourceKind.DerivedFromEstimatedTokens => "derived_from_estimated_tokens",
            CostEstimateSourceKind.ManualOverride => "manual_override",
            CostEstimateSourceKind.Unavailable => "unavailable",
            _ => throw new ArgumentOutOfRangeException(nameof(sourceKind), sourceKind, null)
        };
    }

    private static CostEstimateSourceKind FromWireCostEstimateSourceKind(string value)
    {
        return value switch
        {
            "derived_from_observed_tokens" => CostEstimateSourceKind.DerivedFromObservedTokens,
            "derived_from_estimated_tokens" => CostEstimateSourceKind.DerivedFromEstimatedTokens,
            "manual_override" => CostEstimateSourceKind.ManualOverride,
            "unavailable" => CostEstimateSourceKind.Unavailable,
            _ => throw new InvalidOperationException("Cost estimate source kind is not supported.")
        };
    }

    private static string ToWireTokenMetricStatus(TokenMetricStatus status)
    {
        return status switch
        {
            TokenMetricStatus.Observed => "observed",
            TokenMetricStatus.Derived => "derived",
            TokenMetricStatus.Estimated => "estimated",
            TokenMetricStatus.Unavailable => "unavailable",
            TokenMetricStatus.NotApplicable => "not_applicable",
            TokenMetricStatus.Mixed => "mixed",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
    }

    private static TokenMetricStatus FromWireTokenMetricStatus(string value)
    {
        return value switch
        {
            "observed" => TokenMetricStatus.Observed,
            "derived" => TokenMetricStatus.Derived,
            "estimated" => TokenMetricStatus.Estimated,
            "unavailable" => TokenMetricStatus.Unavailable,
            "not_applicable" => TokenMetricStatus.NotApplicable,
            "mixed" => TokenMetricStatus.Mixed,
            _ => throw new InvalidOperationException("Token metric status is not supported.")
        };
    }

    private static string ToWireTokenMetricConfidence(TokenMetricConfidence confidence)
    {
        return confidence switch
        {
            TokenMetricConfidence.Observed => "observed",
            TokenMetricConfidence.Deterministic => "deterministic",
            TokenMetricConfidence.Estimated => "estimated",
            TokenMetricConfidence.LlmInferred => "llm_inferred",
            TokenMetricConfidence.Unavailable => "unavailable",
            _ => throw new ArgumentOutOfRangeException(nameof(confidence), confidence, null)
        };
    }

    private static TokenMetricConfidence FromWireTokenMetricConfidence(string value)
    {
        return value switch
        {
            "observed" => TokenMetricConfidence.Observed,
            "deterministic" => TokenMetricConfidence.Deterministic,
            "estimated" => TokenMetricConfidence.Estimated,
            "llm_inferred" => TokenMetricConfidence.LlmInferred,
            "unavailable" => TokenMetricConfidence.Unavailable,
            _ => throw new InvalidOperationException("Token metric confidence is not supported.")
        };
    }

    private sealed record NormalizedPricingBasisRequest(
        string Harness,
        string ProviderName,
        string ModelName,
        string BillingRoute,
        string Currency,
        string PricingVersion,
        string AuditEventId,
        IReadOnlyDictionary<string, string> SourceMetadata);
}
