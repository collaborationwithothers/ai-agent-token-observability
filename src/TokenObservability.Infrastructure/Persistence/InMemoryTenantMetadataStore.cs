using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using TokenObservability.Domain.Authorization;
using TokenObservability.Domain.Ingestion;
using TokenObservability.Domain.Pricing;
using TokenObservability.Domain.Recommendations;
using TokenObservability.Domain.Tenancy;

namespace TokenObservability.Infrastructure.Persistence;

public sealed class InMemoryTenantMetadataStore(ITenantMetadataClock clock) : ITenantMetadataStore
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

    private static readonly string[] UnsupportedHotspotSummaryFragments =
    [
        "blame",
        "ranking",
        "leaderboard",
        "wrongness",
        "user error",
        "developer error",
        "obvious error",
        "fault"
    ];

    private static readonly HashSet<string> AllowedEvidenceMetadataKeys = new(StringComparer.Ordinal)
    {
        "denial_reason",
        "evidence_kind",
        "external_principal_type",
        "operation",
        "product_role",
        "request_route",
        "result",
        "requested_scope_id",
        "requested_scope_kind",
        "scope_id",
        "scope_kind",
        "policy_kind",
        "policy_version_id",
        "change_kind",
        "pricing_basis_id",
        "pricing_version",
        "provider_name",
        "model_name",
        "token_type",
        "billing_route",
        "source_kind",
        "review_state",
        "recommendation_kind",
        "authority_state",
        "validation_state",
        "evidence_packet_version",
        "prompt_template_version",
        "deployment_alias",
        "fallback_behavior",
        "structured_output_schema_version",
        "failure_code",
        "content_capture_policy_version",
        "recommendation_model_policy_version",
        "pricing_basis_version",
        "reason",
        "agent_session_id",
        "token_hotspot_id",
        "content_reference_id",
        "redaction_review_id",
        "capture_state",
        "redaction_status",
        "retention_class",
        "review_decision",
        "recommendation_eligible",
        "budget_policy_id",
        "budget_scope_kind",
        "budget_metric_kind",
        "budget_policy_status"
    };

    private readonly Dictionary<CustomerOrganizationId, CustomerOrganization> customerOrganizations = [];
    private readonly Dictionary<string, CustomerOrganizationId> organizationSlugIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<IdentityTenantId, IdentityTenant> identityTenants = [];
    private readonly Dictionary<ProductUserId, ProductUser> productUsers = [];
    private readonly Dictionary<ProductUserLookupKey, ProductUserId> productUserExternalSubjectIndex = [];
    private readonly Dictionary<ProductRoleMappingId, ProductRoleMapping> productRoleMappings = [];
    private readonly Dictionary<GovernanceAuditEventKey, GovernanceAuditEvent> governanceAuditEvents = [];
    private readonly Dictionary<ScopedIngestionCredentialId, ScopedIngestionCredential> scopedIngestionCredentials = [];
    private readonly Dictionary<IngestionRejectionId, IngestionRejectionRecord> ingestionRejections = [];
    private readonly Dictionary<string, TelemetryEnvelopeRecord> telemetryEnvelopes = [];
    private readonly Dictionary<string, ContentCandidateMetadata> contentCandidateMetadata = [];
    private readonly Dictionary<ContentReferenceId, ContentReferenceRecord> contentReferences = [];
    private readonly Dictionary<RedactionReviewId, RedactionReviewRecord> redactionReviews = [];
    private readonly Dictionary<CustomerOrganizationId, ContentCapturePolicy> activeContentCapturePolicies = [];
    private readonly Dictionary<TelemetryEnvelopeDedupeLookupKey, string> telemetryEnvelopeDedupeIndex = [];
    private readonly Dictionary<string, AgentSessionRecord> agentSessions = [];
    private readonly Dictionary<AgentSessionLookupKey, string> agentSessionLookupIndex = [];
    private readonly Dictionary<TokenObservationId, TokenObservationRecord> tokenObservations = [];
    private readonly Dictionary<string, PricingBasisRecord> pricingBasisRecords = [];
    private readonly Dictionary<string, CostEstimateRecord> costEstimates = [];
    private readonly Dictionary<string, BudgetPolicyRecord> budgetPolicies = [];
    private readonly Dictionary<ProductApiIdempotencyKey, ProductApiIdempotencyRecord> productApiIdempotencyRecords = [];
    private readonly Dictionary<TokenHotspotId, TokenHotspotRecord> tokenHotspots = [];
    private readonly Dictionary<TokenHotspotDetectionKey, TokenHotspotId> tokenHotspotDetectionKeyIndex = [];
    private readonly Dictionary<RecommendationId, RecommendationRecord> recommendations = [];
    private readonly Dictionary<RecommendationGenerationKey, RecommendationId> recommendationGenerationKeyIndex = [];
    private readonly Dictionary<RecommendationEvidenceId, RecommendationEvidenceRecord> recommendationEvidenceRecords = [];
    private readonly Dictionary<RecommendationRegenerationRequestId, RecommendationRegenerationRequest> recommendationRegenerationRequests = [];
    private readonly Dictionary<RecommendationModelPolicyKey, RecommendationModelPolicyRecord> recommendationModelPolicies = [];
    private readonly Dictionary<CustomerOrganizationId, string> activeRecommendationModelPolicyIndex = [];
    private readonly Dictionary<RecommendationPromptTemplateKey, RecommendationPromptTemplateRecord> recommendationPromptTemplates = [];
    private readonly Dictionary<RecommendationLlmGenerationFailureId, RecommendationLlmGenerationFailureRecord> recommendationLlmGenerationFailures = [];
    private readonly Dictionary<AggregateMetricPointId, AggregateMetricPointRecord> aggregateMetricPoints = [];
    private readonly List<AggregateTokenTimelineBucket> aggregateTokenTimelineBuckets = [];
    private readonly Dictionary<AggregateMetricExportFailureId, AggregateMetricExportFailureRecord> aggregateMetricExportFailures = [];
    private readonly object gate = new();

    public bool CanLoadMissingCustomerOrganizations => true;

    public Task<CustomerOrganization> CreateCustomerOrganizationAsync(CreateCustomerOrganizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var slug = NormalizeRequiredText(request.Slug, nameof(request.Slug)).ToLowerInvariant();
        var displayName = NormalizeRequiredText(request.DisplayName, nameof(request.DisplayName));
        var dataResidencyRegion = NormalizeRequiredText(request.DataResidencyRegion, nameof(request.DataResidencyRegion)).ToLowerInvariant();
        var now = clock.UtcNow.ToUniversalTime();

        lock (gate)
        {
            if (organizationSlugIndex.ContainsKey(slug))
            {
                throw new InvalidOperationException($"Customer organization slug already exists: {slug}");
            }

            var organization = new CustomerOrganization(
                CustomerOrganizationId.NewId(),
                slug,
                displayName,
                dataResidencyRegion,
                request.IsolationTier,
                CustomerOrganizationStatus.Active,
                now,
                now);

            customerOrganizations.Add(organization.CustomerOrganizationId, organization);
            organizationSlugIndex.Add(slug, organization.CustomerOrganizationId);

            return Task.FromResult(organization);
        }
    }

    public Task<CustomerOrganization> EnsureCustomerOrganizationLoadedAsync(EnsureCustomerOrganizationLoadedRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.CustomerOrganizationId == CustomerOrganizationId.Empty)
        {
            throw new ArgumentException("Customer organization id is required.", nameof(request));
        }

        var slug = NormalizeRequiredText(request.Slug, nameof(request.Slug)).ToLowerInvariant();
        var displayName = NormalizeRequiredText(request.DisplayName, nameof(request.DisplayName));
        var dataResidencyRegion = NormalizeRequiredText(request.DataResidencyRegion, nameof(request.DataResidencyRegion)).ToLowerInvariant();
        var now = clock.UtcNow.ToUniversalTime();

        lock (gate)
        {
            if (customerOrganizations.TryGetValue(request.CustomerOrganizationId, out var existing))
            {
                return Task.FromResult(existing);
            }

            if (organizationSlugIndex.ContainsKey(slug))
            {
                throw new InvalidOperationException($"Customer organization slug already exists: {slug}");
            }

            var organization = new CustomerOrganization(
                request.CustomerOrganizationId,
                slug,
                displayName,
                dataResidencyRegion,
                request.IsolationTier,
                CustomerOrganizationStatus.Active,
                now,
                now);
            customerOrganizations.Add(organization.CustomerOrganizationId, organization);
            organizationSlugIndex.Add(slug, organization.CustomerOrganizationId);
            return Task.FromResult(organization);
        }
    }

    public Task<CustomerOrganization?> FindCustomerOrganizationBySlugAsync(string slug)
    {
        var normalizedSlug = NormalizeRequiredText(slug, nameof(slug)).ToLowerInvariant();

        lock (gate)
        {
            return Task.FromResult(
                organizationSlugIndex.TryGetValue(normalizedSlug, out var organizationId) &&
                    customerOrganizations[organizationId].Status == CustomerOrganizationStatus.Active
                    ? customerOrganizations[organizationId]
                    : null);
        }
    }

    public Task<CustomerOrganization?> FindCustomerOrganizationAsync(CustomerOrganizationId customerOrganizationId)
    {
        lock (gate)
        {
            return Task.FromResult(
                customerOrganizations.TryGetValue(customerOrganizationId, out var organization)
                    ? organization
                    : null);
        }
    }

    public Task<IdentityTenant> CreateIdentityTenantAsync(
        CustomerOrganizationId customerOrganizationId,
        CreateIdentityTenantRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var issuer = NormalizeRequiredText(request.Issuer, nameof(request.Issuer));
        var externalTenantId = NormalizeRequiredText(request.ExternalTenantId, nameof(request.ExternalTenantId));
        var displayName = NormalizeRequiredText(request.DisplayName, nameof(request.DisplayName));
        var allowedAudiences = request.AllowedAudiences
            .Select(audience => NormalizeRequiredText(audience, nameof(request.AllowedAudiences)))
            .ToArray();
        var now = clock.UtcNow.ToUniversalTime();

        if (allowedAudiences.Length == 0)
        {
            throw new ArgumentException("At least one allowed audience is required.", nameof(request));
        }

        lock (gate)
        {
            RequireCustomerOrganization(customerOrganizationId);

            if (identityTenants.Values.Any(identityTenant =>
                    identityTenant.CustomerOrganizationId == customerOrganizationId &&
                    identityTenant.Provider == request.Provider &&
                    StringComparer.Ordinal.Equals(identityTenant.ExternalTenantId, externalTenantId)))
            {
                throw new InvalidOperationException("Identity tenant already exists for the customer organization.");
            }

            var identityTenant = new IdentityTenant(
                IdentityTenantId.NewId(),
                customerOrganizationId,
                request.Provider,
                issuer,
                externalTenantId,
                allowedAudiences,
                request.JwksUri,
                displayName,
                IdentityTenantStatus.Active,
                LastValidatedAtUtc: null,
                now,
                now);

            identityTenants.Add(identityTenant.IdentityTenantId, identityTenant);

            return Task.FromResult(identityTenant);
        }
    }

    public Task<ProductUser> CreateProductUserAsync(
        CustomerOrganizationId customerOrganizationId,
        IdentityTenantId identityTenantId,
        CreateProductUserRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var externalSubjectId = NormalizeRequiredText(request.ExternalSubjectId, nameof(request.ExternalSubjectId));
        var displayLabel = NormalizeRequiredText(request.DisplayLabel, nameof(request.DisplayLabel));
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        var now = clock.UtcNow.ToUniversalTime();

        lock (gate)
        {
            RequireCustomerOrganization(customerOrganizationId);
            RequireIdentityTenantForCustomerOrganization(customerOrganizationId, identityTenantId);

            var lookupKey = new ProductUserLookupKey(customerOrganizationId, identityTenantId, externalSubjectId);

            if (productUserExternalSubjectIndex.ContainsKey(lookupKey))
            {
                throw new InvalidOperationException("Product user external subject already exists for the customer organization and identity tenant.");
            }

            var productUser = new ProductUser(
                ProductUserId.NewId(),
                customerOrganizationId,
                identityTenantId,
                externalSubjectId,
                displayLabel,
                email,
                ProductUserStatus.Active,
                FirstSeenAtUtc: now,
                LastSeenAtUtc: null,
                CreatedAtUtc: now,
                UpdatedAtUtc: now);

            productUsers.Add(productUser.ProductUserId, productUser);
            productUserExternalSubjectIndex.Add(lookupKey, productUser.ProductUserId);

            return Task.FromResult(productUser);
        }
    }

    public Task<ProductUser?> FindProductUserByExternalSubjectAsync(
        CustomerOrganizationId customerOrganizationId,
        IdentityTenantId identityTenantId,
        string externalSubjectId)
    {
        var normalizedExternalSubjectId = NormalizeRequiredText(externalSubjectId, nameof(externalSubjectId));

        lock (gate)
        {
            var lookupKey = new ProductUserLookupKey(customerOrganizationId, identityTenantId, normalizedExternalSubjectId);

            return Task.FromResult(
                productUserExternalSubjectIndex.TryGetValue(lookupKey, out var productUserId)
                    ? productUsers[productUserId]
                    : null);
        }
    }

    public Task<IdentityTenant?> FindIdentityTenantForClaimsAsync(
        CustomerOrganizationId customerOrganizationId,
        AuthenticatedTokenClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        var issuer = NormalizeRequiredText(claims.Issuer, nameof(claims.Issuer));
        var externalTenantId = NormalizeRequiredText(claims.ExternalTenantId, nameof(claims.ExternalTenantId));
        var audience = NormalizeRequiredText(claims.Audience, nameof(claims.Audience));

        lock (gate)
        {
            RequireCustomerOrganization(customerOrganizationId);

            return Task.FromResult(identityTenants.Values.SingleOrDefault(identityTenant =>
                identityTenant.CustomerOrganizationId == customerOrganizationId &&
                identityTenant.Status == IdentityTenantStatus.Active &&
                StringComparer.Ordinal.Equals(identityTenant.Issuer, issuer) &&
                StringComparer.Ordinal.Equals(identityTenant.ExternalTenantId, externalTenantId) &&
                identityTenant.AllowedAudiences.Contains(audience, StringComparer.Ordinal)));
        }
    }

    internal Task<CustomerOrganization> SetCustomerOrganizationStatusAsync(
        CustomerOrganizationId customerOrganizationId,
        CustomerOrganizationStatus status)
    {
        lock (gate)
        {
            if (!customerOrganizations.TryGetValue(customerOrganizationId, out var organization))
            {
                throw new InvalidOperationException("Customer organization does not exist.");
            }

            var updatedOrganization = organization with
            {
                Status = status,
                UpdatedAtUtc = clock.UtcNow.ToUniversalTime()
            };

            customerOrganizations[customerOrganizationId] = updatedOrganization;

            return Task.FromResult(updatedOrganization);
        }
    }

    internal Task<ProductUser> SetProductUserStatusAsync(ProductUserId productUserId, ProductUserStatus status)
    {
        lock (gate)
        {
            if (!productUsers.TryGetValue(productUserId, out var productUser))
            {
                throw new InvalidOperationException("Product user does not exist.");
            }

            var updatedProductUser = productUser with
            {
                Status = status,
                UpdatedAtUtc = clock.UtcNow.ToUniversalTime()
            };

            productUsers[productUserId] = updatedProductUser;

            return Task.FromResult(updatedProductUser);
        }
    }

    public Task<ProductUser> ResolveProductUserFromAuthenticatedClaimsAsync(
        CustomerOrganizationId customerOrganizationId,
        IdentityTenantId identityTenantId,
        AuthenticatedTokenClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        var externalSubjectId = NormalizeRequiredText(claims.Subject, nameof(claims.Subject));
        var displayLabel = NormalizeRequiredText(claims.DisplayLabel, nameof(claims.DisplayLabel));
        var email = string.IsNullOrWhiteSpace(claims.Email) ? null : claims.Email.Trim();
        var now = clock.UtcNow.ToUniversalTime();

        lock (gate)
        {
            RequireCustomerOrganization(customerOrganizationId);
            RequireIdentityTenantForCustomerOrganization(customerOrganizationId, identityTenantId);
            RequireClaimsMatchIdentityTenant(identityTenantId, claims);

            var lookupKey = new ProductUserLookupKey(customerOrganizationId, identityTenantId, externalSubjectId);

            if (productUserExternalSubjectIndex.TryGetValue(lookupKey, out var existingProductUserId))
            {
                var existingUser = productUsers[existingProductUserId];
                var updatedUser = existingUser with
                {
                    DisplayLabel = displayLabel,
                    Email = email,
                    LastSeenAtUtc = now,
                    UpdatedAtUtc = now
                };

                productUsers[existingProductUserId] = updatedUser;

                return Task.FromResult(updatedUser);
            }

            var productUser = new ProductUser(
                ProductUserId.NewId(),
                customerOrganizationId,
                identityTenantId,
                externalSubjectId,
                displayLabel,
                email,
                ProductUserStatus.Active,
                FirstSeenAtUtc: now,
                LastSeenAtUtc: now,
                CreatedAtUtc: now,
                UpdatedAtUtc: now);

            productUsers.Add(productUser.ProductUserId, productUser);
            productUserExternalSubjectIndex.Add(lookupKey, productUser.ProductUserId);

            return Task.FromResult(productUser);
        }
    }

    public Task<ProductRoleMapping> CreateProductRoleMappingAsync(
        CustomerOrganizationId customerOrganizationId,
        CreateProductRoleMappingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ChangedByClaims);

        var externalPrincipalId = NormalizeRequiredText(request.ExternalPrincipalId, nameof(request.ExternalPrincipalId));
        var auditEventId = NormalizeRequiredText(request.AuditEventId, nameof(request.AuditEventId));
        var correlationId = NormalizeRequiredText(request.CorrelationId, nameof(request.CorrelationId));
        var scopeId = NormalizeScopeId(request.ScopeKind, request.ScopeId);
        var now = clock.UtcNow.ToUniversalTime();
        var effectiveFromUtc = request.EffectiveFromUtc.ToUniversalTime();
        var effectiveToUtc = request.EffectiveToUtc?.ToUniversalTime();

        RejectGrafanaAppRolePrincipal(request.ExternalPrincipalType, externalPrincipalId);

        if (effectiveToUtc is not null && effectiveToUtc <= effectiveFromUtc)
        {
            throw new ArgumentException("Effective end must be after effective start.", nameof(request));
        }

        lock (gate)
        {
            RequireCustomerOrganization(customerOrganizationId);
            RequireIdentityTenantForCustomerOrganization(customerOrganizationId, request.IdentityTenantId);

            try
            {
                RequireClaimsMatchIdentityTenant(request.IdentityTenantId, request.ChangedByClaims);
            }
            catch (InvalidOperationException ex)
            {
                RecordAuthorizationAuditEventUnderLock(
                    customerOrganizationId,
                    actorProductUserId: null,
                    effectiveRole: null,
                    ProductAuthorizationAction.IdentityManage,
                    new ProductScope(ProductScopeKind.Organization, ScopeId: null),
                    ProductAuthorizationDenialReason.InvalidTenant,
                    correlationId);

                throw new UnauthorizedAccessException("Product role mapping changes require claims from the configured identity tenant.", ex);
            }

            var actor = ResolveProductUserFromAuthenticatedClaimsUnderLock(
                customerOrganizationId,
                request.IdentityTenantId,
                request.ChangedByClaims);
            var matchedActorMappings = FindActivePrincipalMappingsUnderLock(
                customerOrganizationId,
                request.IdentityTenantId,
                request.ChangedByClaims,
                now);
            var organizationScopedActorMappings = matchedActorMappings
                .Where(mapping => ScopeMatches(mapping, new ProductScope(ProductScopeKind.Organization, ScopeId: null)))
                .ToArray();
            var identityManageActorMappings = organizationScopedActorMappings
                .Where(mapping => RoleAllowsAction(mapping.ProductRole, ProductAuthorizationAction.IdentityManage))
                .ToArray();

            if (identityManageActorMappings.Length == 0)
            {
                var denialReason = matchedActorMappings.Length == 0
                    ? ProductAuthorizationDenialReason.MissingRoleMapping
                    : organizationScopedActorMappings.Length == 0
                        ? ProductAuthorizationDenialReason.ScopeMismatch
                        : ProductAuthorizationDenialReason.InsufficientRole;
                var auditMappings = organizationScopedActorMappings.Length == 0
                    ? matchedActorMappings
                    : organizationScopedActorMappings;

                RecordAuthorizationAuditEventUnderLock(
                    customerOrganizationId,
                    actor.ProductUserId,
                    FirstEffectiveRoleOrNull(auditMappings),
                    ProductAuthorizationAction.IdentityManage,
                    new ProductScope(ProductScopeKind.Organization, ScopeId: null),
                    denialReason,
                    correlationId);

                throw new UnauthorizedAccessException("Product role mapping changes require identity.manage authorization.");
            }

            return Task.FromResult(CreateProductRoleMappingUnderLock(
                customerOrganizationId,
                request.IdentityTenantId,
                request.ExternalPrincipalType,
                externalPrincipalId,
                request.ProductRole,
                request.ScopeKind,
                scopeId,
                effectiveFromUtc,
                effectiveToUtc,
                actor.ProductUserId,
                identityManageActorMappings[0].ProductRole,
                auditEventId,
                correlationId,
                now,
                now));
        }
    }

    internal Task<ProductRoleMapping> SeedProductRoleMappingAsync(
        CustomerOrganizationId customerOrganizationId,
        SeedProductRoleMappingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var externalPrincipalId = NormalizeRequiredText(request.ExternalPrincipalId, nameof(request.ExternalPrincipalId));
        var auditEventId = NormalizeRequiredText(request.AuditEventId, nameof(request.AuditEventId));
        var correlationId = NormalizeRequiredText(request.CorrelationId, nameof(request.CorrelationId));
        var scopeId = NormalizeScopeId(request.ScopeKind, request.ScopeId);
        var now = clock.UtcNow.ToUniversalTime();
        var effectiveFromUtc = request.EffectiveFromUtc.ToUniversalTime();
        var effectiveToUtc = request.EffectiveToUtc?.ToUniversalTime();

        RejectGrafanaAppRolePrincipal(request.ExternalPrincipalType, externalPrincipalId);

        if (effectiveToUtc is not null && effectiveToUtc <= effectiveFromUtc)
        {
            throw new ArgumentException("Effective end must be after effective start.", nameof(request));
        }

        lock (gate)
        {
            RequireCustomerOrganization(customerOrganizationId);
            RequireIdentityTenantForCustomerOrganization(customerOrganizationId, request.IdentityTenantId);
            RequireProductUserForCustomerOrganization(customerOrganizationId, request.ChangedByProductUserId);

            return Task.FromResult(CreateProductRoleMappingUnderLock(
                customerOrganizationId,
                request.IdentityTenantId,
                request.ExternalPrincipalType,
                externalPrincipalId,
                request.ProductRole,
                request.ScopeKind,
                scopeId,
                effectiveFromUtc,
                effectiveToUtc,
                request.ChangedByProductUserId,
                request.ActorEffectiveRole,
                auditEventId,
                correlationId,
                now,
                now));
        }
    }

    public Task<GovernanceAuditEvent?> FindGovernanceAuditEventAsync(
        CustomerOrganizationId customerOrganizationId,
        string auditEventId)
    {
        var normalizedAuditEventId = NormalizeRequiredText(auditEventId, nameof(auditEventId));

        lock (gate)
        {
            return Task.FromResult(
                governanceAuditEvents.TryGetValue(new GovernanceAuditEventKey(customerOrganizationId, normalizedAuditEventId), out var auditEvent)
                    ? auditEvent
                    : null);
        }
    }

    public Task<IReadOnlyList<GovernanceAuditEvent>> ListGovernanceAuditEventsAsync(
        CustomerOrganizationId customerOrganizationId)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<GovernanceAuditEvent>>(
                governanceAuditEvents.Values
                    .Where(auditEvent => auditEvent.CustomerOrganizationId == customerOrganizationId)
                    .OrderBy(auditEvent => auditEvent.CreatedAtUtc)
                    .ThenBy(auditEvent => auditEvent.AuditEventId, StringComparer.Ordinal)
                    .ToArray());
        }
    }

    public Task<IngestionRejectionRecord> RecordIngestionRejectionAsync(
        CreateIngestionRejectionRecordRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var harnessSetupProfileId = string.IsNullOrWhiteSpace(request.HarnessSetupProfileId)
            ? null
            : NormalizeHarnessSetupProfileId(request.HarnessSetupProfileId, nameof(request.HarnessSetupProfileId));
        var declaredHarness = string.IsNullOrWhiteSpace(request.DeclaredHarness)
            ? null
            : NormalizeDeclaredHarness(request.DeclaredHarness, nameof(request.DeclaredHarness));
        var signalType = NormalizeRequiredText(request.SignalType, nameof(request.SignalType));
        var requestRoute = NormalizeRequiredText(request.RequestRoute, nameof(request.RequestRoute));
        var reasonCode = NormalizeRequiredText(request.ReasonCode, nameof(request.ReasonCode));
        var correlationId = NormalizeRequiredText(request.CorrelationId, nameof(request.CorrelationId));
        var auditEventId = string.IsNullOrWhiteSpace(request.AuditEventId)
            ? null
            : request.AuditEventId.Trim();
        var evidenceMetadata = NormalizeEvidenceMetadata(request.EvidenceMetadata);
        var now = clock.UtcNow.ToUniversalTime();

        ValidateIngestionRejectionShape(
            request.CustomerOrganizationId,
            request.ScopedIngestionCredentialId,
            signalType,
            requestRoute,
            reasonCode,
            request.HttpStatus,
            evidenceMetadata);

        lock (gate)
        {
            if (request.CustomerOrganizationId is { } customerOrganizationId)
            {
                RequireCustomerOrganizationExists(customerOrganizationId);
            }

            if (request.ScopedIngestionCredentialId is { } scopedIngestionCredentialId)
            {
                if (request.CustomerOrganizationId is not { } linkedCustomerOrganizationId)
                {
                    throw new InvalidOperationException("Scoped ingestion credential rejection links require a customer organization.");
                }

                if (!scopedIngestionCredentials.TryGetValue(scopedIngestionCredentialId, out var credential) ||
                    credential.CustomerOrganizationId != linkedCustomerOrganizationId)
                {
                    throw new InvalidOperationException("Scoped ingestion credential does not belong to the rejection customer organization.");
                }
            }

            if (!string.IsNullOrWhiteSpace(auditEventId))
            {
                if (request.CustomerOrganizationId is not { } linkedCustomerOrganizationId)
                {
                    throw new InvalidOperationException("Audit-linked ingestion rejections require a customer organization.");
                }

                if (!governanceAuditEvents.ContainsKey(new GovernanceAuditEventKey(linkedCustomerOrganizationId, auditEventId)))
                {
                    throw new InvalidOperationException("Governance audit event does not belong to the rejection customer organization.");
                }
            }

            var rejection = new IngestionRejectionRecord(
                IngestionRejectionId.NewId(),
                request.CustomerOrganizationId,
                harnessSetupProfileId,
                request.ScopedIngestionCredentialId,
                declaredHarness,
                signalType,
                requestRoute,
                reasonCode,
                request.HttpStatus,
                correlationId,
                auditEventId,
                evidenceMetadata,
                now);

            ingestionRejections.Add(rejection.IngestionRejectionId, rejection);

            return Task.FromResult(rejection);
        }
    }

    public Task<IReadOnlyList<IngestionRejectionRecord>> ListIngestionRejectionsAsync(
        CustomerOrganizationId customerOrganizationId)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<IngestionRejectionRecord>>(
                ingestionRejections.Values
                    .Where(rejection => rejection.CustomerOrganizationId == customerOrganizationId)
                    .OrderBy(rejection => rejection.ReceivedAtUtc)
                    .ThenBy(rejection => rejection.IngestionRejectionId.ToString(), StringComparer.Ordinal)
                    .ToArray());
        }
    }

    public Task<TelemetryEnvelopeRecord> RecordTelemetryEnvelopeAsync(
        CreateTelemetryEnvelopeRecordRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var harnessSetupProfileId = NormalizeHarnessSetupProfileId(request.HarnessSetupProfileId, nameof(request.HarnessSetupProfileId));
        var harness = NormalizeWireHarness(request.Harness, nameof(request.Harness));
        var schemaVersion = NormalizeDateVersion(request.SchemaVersion, nameof(request.SchemaVersion));
        var signalType = NormalizeSignalType(request.SignalType, nameof(request.SignalType));
        var sourceEventName = NormalizeSourceEventName(request.SourceEventName, nameof(request.SourceEventName));
        var sourceEventId = NormalizeOptionalMachineToken(request.SourceEventId, nameof(request.SourceEventId));
        var modelName = NormalizeOptionalModelName(request.ModelName, nameof(request.ModelName));
        var harnessVersion = NormalizeOptionalSafeLabel(request.HarnessVersion, nameof(request.HarnessVersion));
        var sandboxSetting = NormalizeOptionalSafeLabel(request.SandboxSetting, nameof(request.SandboxSetting));
        var approvalSetting = NormalizeOptionalSafeLabel(request.ApprovalSetting, nameof(request.ApprovalSetting));
        var repositoryEvidenceState = NormalizeEvidenceState(request.RepositoryEvidenceState, nameof(request.RepositoryEvidenceState));
        var contentPolicyDecision = NormalizeContentDecision(request.ContentPolicyDecision, nameof(request.ContentPolicyDecision));
        var contentCaptureState = NormalizeContentCaptureState(request.ContentCaptureState, nameof(request.ContentCaptureState));
        var redactionState = NormalizeRedactionState(request.RedactionState, nameof(request.RedactionState));
        var routingDecision = NormalizeRoutingDecision(request.RoutingDecision);
        var evidenceState = NormalizeEvidenceState(request.EvidenceState, nameof(request.EvidenceState));
        var metricState = NormalizeMetricState(request.MetricState, nameof(request.MetricState));
        ValidateMetricQuality(request.MetricStatus, request.MetricConfidence);
        var sourceEvidenceKind = NormalizeSourceEvidenceKind(request.SourceEvidenceKind, nameof(request.SourceEvidenceKind));
        var correlationId = NormalizeRequiredText(request.CorrelationId, nameof(request.CorrelationId));
        var dedupeKeyHash = NormalizeHash(request.DedupeKeyHash, nameof(request.DedupeKeyHash));
        var ingestionVersionMetadata = NormalizeIngestionVersionMetadata(request.IngestionVersionMetadata);
        var conversationIdHash = NormalizeOptionalHash(request.ConversationIdHash, nameof(request.ConversationIdHash));
        var turnIdHash = NormalizeOptionalHash(request.TurnIdHash, nameof(request.TurnIdHash));
        var traceIdHash = NormalizeOptionalHash(request.TraceIdHash, nameof(request.TraceIdHash));
        var spanIdHash = NormalizeOptionalHash(request.SpanIdHash, nameof(request.SpanIdHash));
        var now = clock.UtcNow.ToUniversalTime();

        if (request.CustomerOrganizationId == CustomerOrganizationId.Empty)
        {
            throw new ArgumentException("Customer organization identifier must not be empty.", nameof(request));
        }

        if (request.ScopedIngestionCredentialId == ScopedIngestionCredentialId.Empty)
        {
            throw new ArgumentException("Scoped ingestion credential identifier must not be empty.", nameof(request));
        }

        if (request.ProductUserId == ProductUserId.Empty)
        {
            throw new ArgumentException("Product user identifier must not be empty.", nameof(request));
        }

        lock (gate)
        {
            RequireCustomerOrganization(request.CustomerOrganizationId);
            RequireProductUserForCustomerOrganization(request.CustomerOrganizationId, request.ProductUserId);

            var credential = RequireScopedIngestionCredentialForCustomerOrganization(
                request.CustomerOrganizationId,
                request.ScopedIngestionCredentialId);

            if (!StringComparer.Ordinal.Equals(credential.HarnessSetupProfileId, harnessSetupProfileId) ||
                credential.ProductUserId != request.ProductUserId)
            {
                throw new InvalidOperationException("Accepted telemetry must match credential-derived setup profile and user.");
            }

            var dedupeLookupKey = new TelemetryEnvelopeDedupeLookupKey(request.CustomerOrganizationId, dedupeKeyHash);

            if (telemetryEnvelopeDedupeIndex.TryGetValue(dedupeLookupKey, out var existingEnvelopeId))
            {
                return Task.FromResult(telemetryEnvelopes[existingEnvelopeId]);
            }

            var telemetryEnvelopeId = $"telemetry-envelope-{Guid.NewGuid():N}";
            var envelope = new TelemetryEnvelopeRecord(
                telemetryEnvelopeId,
                request.CustomerOrganizationId,
                harnessSetupProfileId,
                request.ScopedIngestionCredentialId,
                request.ProductUserId,
                harness,
                schemaVersion,
                signalType,
                sourceEventName,
                request.SourceEventTimestampUtc?.ToUniversalTime(),
                now,
                conversationIdHash,
                turnIdHash,
                sourceEventId,
                traceIdHash,
                spanIdHash,
                modelName,
                harnessVersion,
                sandboxSetting,
                approvalSetting,
                contentPolicyDecision,
                contentCaptureState,
                redactionState,
                routingDecision,
                evidenceState,
                metricState,
                request.MetricStatus,
                request.MetricConfidence,
                sourceEvidenceKind,
                correlationId,
                dedupeKeyHash,
                ingestionVersionMetadata);

            telemetryEnvelopes.Add(telemetryEnvelopeId, envelope);
            telemetryEnvelopeDedupeIndex.Add(dedupeLookupKey, telemetryEnvelopeId);
            UpsertAgentSessionUnderLock(envelope, repositoryEvidenceState);

            return Task.FromResult(envelope);
        }
    }

    public Task<IReadOnlyList<TelemetryEnvelopeRecord>> ListTelemetryEnvelopesAsync(
        CustomerOrganizationId customerOrganizationId)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<TelemetryEnvelopeRecord>>(
                telemetryEnvelopes.Values
                    .Where(envelope => envelope.CustomerOrganizationId == customerOrganizationId)
                    .OrderBy(envelope => envelope.ReceivedAtUtc)
                    .ThenBy(envelope => envelope.TelemetryEnvelopeId, StringComparer.Ordinal)
                    .ToArray());
        }
    }

    public Task<ContentCandidateMetadata> RecordContentCandidateMetadataAsync(
        CreateContentCandidateMetadataRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.CustomerOrganizationId == CustomerOrganizationId.Empty)
        {
            throw new ArgumentException("Customer organization identifier must not be empty.", nameof(request));
        }

        if (request.ScopedIngestionCredentialId == ScopedIngestionCredentialId.Empty)
        {
            throw new ArgumentException("Scoped ingestion credential identifier must not be empty.", nameof(request));
        }

        if (request.OriginalLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Original content length must not be negative.");
        }

        var policyVersionId = NormalizeRequiredText(request.PolicyVersionId, nameof(request.PolicyVersionId));
        var harnessSetupProfileId = NormalizeHarnessSetupProfileId(
            request.HarnessSetupProfileId,
            nameof(request.HarnessSetupProfileId));
        var sessionId = NormalizeRequiredText(request.SessionId, nameof(request.SessionId));
        var telemetryReference = NormalizeRequiredText(request.TelemetryReference, nameof(request.TelemetryReference));
        var redactionDecisionReason = NormalizeOptionalSafeLabel(
            request.RedactionDecisionReason,
            nameof(request.RedactionDecisionReason));
        var redactionPipelineVersion = NormalizeOptionalSafeLabel(
            request.RedactionPipelineVersion,
            nameof(request.RedactionPipelineVersion));
        var productRuleVersion = NormalizeOptionalSafeLabel(
            request.ProductRuleVersion,
            nameof(request.ProductRuleVersion));
        var redactedContentHash = string.IsNullOrWhiteSpace(request.RedactedContentHash)
            ? null
            : NormalizeHash(request.RedactedContentHash, nameof(request.RedactedContentHash));
        var redactedContentBlobVersion = NormalizeOptionalSafeLabel(
            request.RedactedContentBlobVersion,
            nameof(request.RedactedContentBlobVersion));
        var redactionFindings = NormalizeRedactionFindings(request.RedactionFindings);
        RejectSensitiveText(policyVersionId);
        RejectSensitiveText(sessionId);
        RejectSensitiveText(telemetryReference);
        if (redactionDecisionReason is not null)
        {
            RejectSensitiveText(redactionDecisionReason);
        }

        var now = clock.UtcNow.ToUniversalTime();

        lock (gate)
        {
            RequireCustomerOrganization(request.CustomerOrganizationId);
            RequireScopedIngestionCredentialForCustomerOrganization(
                request.CustomerOrganizationId,
                request.ScopedIngestionCredentialId);

            var metadata = new ContentCandidateMetadata(
                ContentCandidateMetadataId: $"content-candidate-{Guid.NewGuid():N}",
                request.CustomerOrganizationId,
                policyVersionId,
                request.ScopedIngestionCredentialId,
                harnessSetupProfileId,
                sessionId,
                telemetryReference,
                request.ContentClass,
                request.OriginalLength,
                request.PolicyDecision,
                request.EvidenceState,
                request.RedactionStatus,
                request.RetentionClass,
                request.RecommendationUse,
                now,
                request.RedactionOutcome,
                redactionDecisionReason,
                redactionPipelineVersion,
                productRuleVersion,
                redactedContentHash,
                request.RedactedContentStored,
                redactedContentBlobVersion,
                redactionFindings);

            contentCandidateMetadata.Add(metadata.ContentCandidateMetadataId, metadata);
            CreateContentReferenceFromCandidateMetadataUnderLock(metadata, now);
            return Task.FromResult(metadata);
        }
    }

    public Task<IReadOnlyList<ContentCandidateMetadata>> ListContentCandidateMetadataAsync(
        CustomerOrganizationId customerOrganizationId)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<ContentCandidateMetadata>>(
                contentCandidateMetadata.Values
                    .Where(metadata => metadata.CustomerOrganizationId == customerOrganizationId)
                    .OrderBy(metadata => metadata.CreatedAtUtc)
                    .ThenBy(metadata => metadata.ContentCandidateMetadataId, StringComparer.Ordinal)
                    .ToArray());
        }
    }

    public Task<IReadOnlyList<ContentReferenceRecord>> ListContentReferencesForSessionAsync(
        CustomerOrganizationId customerOrganizationId,
        string agentSessionId)
    {
        var normalizedAgentSessionId = NormalizeRequiredText(agentSessionId, nameof(agentSessionId));

        lock (gate)
        {
            RequireCustomerOrganization(customerOrganizationId);
            return Task.FromResult<IReadOnlyList<ContentReferenceRecord>>(
                contentReferences.Values
                    .Where(reference =>
                        reference.CustomerOrganizationId == customerOrganizationId &&
                        StringComparer.Ordinal.Equals(reference.AgentSessionId, normalizedAgentSessionId))
                    .OrderBy(reference => reference.CreatedAtUtc)
                    .ThenBy(reference => reference.ContentReferenceId.Value)
                    .ToArray());
        }
    }

    public Task<IReadOnlyList<ContentReferenceRecord>> ListContentReviewItemsAsync(
        CustomerOrganizationId customerOrganizationId,
        ContentReferenceCaptureState? state = null)
    {
        lock (gate)
        {
            RequireCustomerOrganization(customerOrganizationId);
            return Task.FromResult<IReadOnlyList<ContentReferenceRecord>>(
                contentReferences.Values
                    .Where(reference =>
                        reference.CustomerOrganizationId == customerOrganizationId &&
                        IsContentReviewVisibleState(reference.CaptureState) &&
                        (state is null || reference.CaptureState == state.Value))
                    .OrderBy(reference => reference.CreatedAtUtc)
                    .ThenBy(reference => reference.ContentReferenceId.Value)
                    .ToArray());
        }
    }

    public Task<ContentReferenceRecord?> FindContentReferenceAsync(
        CustomerOrganizationId customerOrganizationId,
        ContentReferenceId contentReferenceId)
    {
        lock (gate)
        {
            RequireCustomerOrganization(customerOrganizationId);
            return Task.FromResult(
                contentReferences.TryGetValue(contentReferenceId, out var reference) &&
                    reference.CustomerOrganizationId == customerOrganizationId
                    ? reference
                    : null);
        }
    }

    public Task<RedactionReviewRecord> ReviewContentReferenceAsync(ReviewContentReferenceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.CustomerOrganizationId == CustomerOrganizationId.Empty)
        {
            throw new ArgumentException("Customer organization identifier must not be empty.", nameof(request));
        }

        if (request.ContentReferenceId == ContentReferenceId.Empty)
        {
            throw new ArgumentException("Content reference identifier must not be empty.", nameof(request));
        }

        if (request.ReviewerProductUserId == ProductUserId.Empty)
        {
            throw new ArgumentException("Reviewer product user identifier must not be empty.", nameof(request));
        }

        var auditEventId = NormalizeRequiredText(request.AuditEventId, nameof(request.AuditEventId));
        var correlationId = NormalizeRequiredText(request.CorrelationId, nameof(request.CorrelationId));
        var decisionReason = NormalizeOptionalSafeLabel(request.DecisionReason, nameof(request.DecisionReason));
        RejectSensitiveText(auditEventId);
        RejectSensitiveText(correlationId);
        if (decisionReason is not null)
        {
            RejectSensitiveText(decisionReason);
        }

        var now = clock.UtcNow.ToUniversalTime();

        lock (gate)
        {
            RequireCustomerOrganization(request.CustomerOrganizationId);
            RequireProductUserForCustomerOrganization(request.CustomerOrganizationId, request.ReviewerProductUserId);

            if (!contentReferences.TryGetValue(request.ContentReferenceId, out var existing) ||
                existing.CustomerOrganizationId != request.CustomerOrganizationId)
            {
                throw new InvalidOperationException("Content reference was not found for the customer organization.");
            }

            if (!IsReviewableState(existing.CaptureState))
            {
                throw new InvalidOperationException("Only review-required or redaction-failed content references can be reviewed.");
            }

            var approvedExcerpt = NormalizeApprovedExcerpt(request);
            var reviewId = new RedactionReviewId(Guid.NewGuid());
            var auditEvent = CreateGovernanceAuditEventUnderLock(
                auditEventId,
                request.CustomerOrganizationId,
                request.ReviewerProductUserId,
                request.EffectiveRole,
                ProductAuthorizationAction.ContentReviewDecide,
                ProductScopeKind.ContentReviewQueue.ToString(),
                request.ContentReferenceId.ToString(),
                "updated",
                denialReason: null,
                correlationId,
                CreateContentReviewAuditMetadata(
                    operation: $"content_review_{ToWireRedactionReviewDecision(request.Decision)}",
                    result: ToWireRedactionReviewDecision(request.Decision),
                    request.ContentReferenceId,
                    reviewId,
                    request.Decision,
                    existing),
                now);

            var review = new RedactionReviewRecord(
                reviewId,
                request.CustomerOrganizationId,
                request.ContentReferenceId,
                request.ReviewerProductUserId,
                request.Decision,
                decisionReason,
                auditEvent.AuditEventId,
                correlationId,
                now);

            redactionReviews.Add(reviewId, review);
            contentReferences[request.ContentReferenceId] = ApplyReviewDecision(existing, request.Decision, approvedExcerpt, now, auditEvent.AuditEventId);
            return Task.FromResult(review);
        }
    }

    public Task<ContentRetentionCleanupResult> CleanupExpiredContentReferencesAsync(
        CustomerOrganizationId customerOrganizationId,
        DateTimeOffset asOfUtc,
        ProductUserId? actorProductUserId,
        ProductRole? effectiveRole,
        string correlationId)
    {
        var normalizedCorrelationId = NormalizeRequiredText(correlationId, nameof(correlationId));

        lock (gate)
        {
            RequireCustomerOrganization(customerOrganizationId);
            if (actorProductUserId is not null)
            {
                RequireProductUserForCustomerOrganization(customerOrganizationId, actorProductUserId.Value);
            }

            var now = asOfUtc.ToUniversalTime();
            var expired = contentReferences.Values
                .Where(reference =>
                    reference.CustomerOrganizationId == customerOrganizationId &&
                    reference.ExpiresAtUtc is not null &&
                    reference.ExpiresAtUtc.Value <= now &&
                    reference.BlobPointer is not null)
                .OrderBy(reference => reference.ExpiresAtUtc)
                .ThenBy(reference => reference.ContentReferenceId.Value)
                .ToArray();

            foreach (var reference in expired)
            {
                CreateGovernanceAuditEventUnderLock(
                    $"audit-content-retention-{Guid.NewGuid():N}",
                    customerOrganizationId,
                    actorProductUserId,
                    effectiveRole,
                    ProductAuthorizationAction.ContentReviewDecide,
                    "content_reference",
                    reference.ContentReferenceId.ToString(),
                    "updated",
                    denialReason: null,
                    normalizedCorrelationId,
                    CreateContentReferenceAuditMetadata(
                        operation: "content_retention_cleanup",
                        result: "expired",
                        reference.ContentReferenceId,
                        reference.CaptureState,
                        reference.RedactionStatus,
                        reference.RetentionClass,
                        reference.RecommendationEligible),
                    now);

                contentReferences[reference.ContentReferenceId] = reference with
                {
                    CaptureState = ContentReferenceCaptureState.MetadataOnly,
                    BlobPointer = null,
                    ApprovedExcerpt = null,
                    RecommendationEligible = false,
                    UpdatedAtUtc = now
                };
            }

            return Task.FromResult(new ContentRetentionCleanupResult(
                expired.Length,
                expired.Select(static reference => reference.ContentReferenceId).ToArray()));
        }
    }

    public Task<ContentCandidateMetadata> RecordContentCandidateExtractionFailureAsync(
        CreateContentCandidateExtractionFailureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return RecordContentCandidateMetadataAsync(new CreateContentCandidateMetadataRequest(
            request.CustomerOrganizationId,
            request.PolicyVersionId,
            request.ScopedIngestionCredentialId,
            request.HarnessSetupProfileId,
            request.SessionId,
            request.TelemetryReference,
            request.ContentClass,
            OriginalLength: 0,
            ContentCapturePolicyDecision.PolicyDenied,
            ContentCandidateEvidenceState.RedactionFailed,
            ContentRedactionStatus.Failed,
            request.RetentionClass,
            request.RecommendationUse));
    }

    public Task<ContentCapturePolicy> SetActiveContentCapturePolicyAsync(SetActiveContentCapturePolicyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Policy);

        if (request.Policy.CustomerOrganizationId == CustomerOrganizationId.Empty)
        {
            throw new ArgumentException("Customer organization identifier must not be empty.", nameof(request));
        }

        if (request.ActorProductUserId == ProductUserId.Empty)
        {
            throw new ArgumentException("Actor product user identifier must not be empty.", nameof(request));
        }

        var policyVersionId = NormalizeRequiredText(request.Policy.PolicyVersionId, nameof(request.Policy.PolicyVersionId));
        var correlationId = NormalizeRequiredText(request.CorrelationId, nameof(request.CorrelationId));
        RejectSensitiveText(policyVersionId);
        RejectSensitiveText(correlationId);
        var normalizedPolicy = request.Policy with { PolicyVersionId = policyVersionId };
        var now = clock.UtcNow.ToUniversalTime();

        lock (gate)
        {
            RequireCustomerOrganization(request.Policy.CustomerOrganizationId);
            RequireProductUserForCustomerOrganization(request.Policy.CustomerOrganizationId, request.ActorProductUserId);
            activeContentCapturePolicies[request.Policy.CustomerOrganizationId] = normalizedPolicy;
            CreateGovernanceAuditEventUnderLock(
                $"audit-content-capture-policy-{Guid.NewGuid():N}",
                request.Policy.CustomerOrganizationId,
                request.ActorProductUserId,
                request.EffectiveRole,
                ProductAuthorizationAction.TenantUpdate,
                "content_capture_policy",
                policyVersionId,
                "updated",
                null,
                correlationId,
                new Dictionary<string, string>
                {
                    ["result"] = "updated",
                    ["policy_kind"] = "content_capture",
                    ["policy_version_id"] = policyVersionId,
                    ["change_kind"] = ToWirePolicyChangeKind(request.ChangeKind)
                },
                now);
            return Task.FromResult(normalizedPolicy);
        }
    }

    public Task<ContentCapturePolicy> GetActiveContentCapturePolicyAsync(
        CustomerOrganizationId customerOrganizationId,
        string harnessSetupProfileId)
    {
        _ = NormalizeHarnessSetupProfileId(harnessSetupProfileId, nameof(harnessSetupProfileId));

        lock (gate)
        {
            RequireCustomerOrganization(customerOrganizationId);

            return Task.FromResult(
                activeContentCapturePolicies.TryGetValue(customerOrganizationId, out var policy)
                    ? policy
                    : ContentCapturePolicy.Disabled(
                        customerOrganizationId,
                        "content-capture-default-disabled"));
        }
    }

    public Task<GovernanceAuditEvent> RecordContentCapturePolicyChangeAsync(
        CreateContentCapturePolicyChangeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.CustomerOrganizationId == CustomerOrganizationId.Empty)
        {
            throw new ArgumentException("Customer organization identifier must not be empty.", nameof(request));
        }

        if (request.ActorProductUserId == ProductUserId.Empty)
        {
            throw new ArgumentException("Actor product user identifier must not be empty.", nameof(request));
        }

        var policyVersionId = NormalizeRequiredText(request.PolicyVersionId, nameof(request.PolicyVersionId));
        var correlationId = NormalizeRequiredText(request.CorrelationId, nameof(request.CorrelationId));
        RejectSensitiveText(policyVersionId);
        RejectSensitiveText(correlationId);
        var now = clock.UtcNow.ToUniversalTime();

        lock (gate)
        {
            RequireCustomerOrganization(request.CustomerOrganizationId);
            RequireProductUserForCustomerOrganization(request.CustomerOrganizationId, request.ActorProductUserId);

            return Task.FromResult(CreateGovernanceAuditEventUnderLock(
                $"audit-content-capture-policy-{Guid.NewGuid():N}",
                request.CustomerOrganizationId,
                request.ActorProductUserId,
                request.EffectiveRole,
                ProductAuthorizationAction.TenantUpdate,
                "content_capture_policy",
                policyVersionId,
                "updated",
                null,
                correlationId,
                new Dictionary<string, string>
                {
                    ["result"] = "updated",
                    ["policy_kind"] = "content_capture",
                    ["policy_version_id"] = policyVersionId,
                    ["change_kind"] = ToWirePolicyChangeKind(request.ChangeKind)
                },
                now));
        }
    }

    public Task<AgentSessionRecord> UpsertAgentSessionAsync(
        CreateAgentSessionRecordRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var harnessSetupProfileId = NormalizeHarnessSetupProfileId(
            request.HarnessSetupProfileId,
            nameof(request.HarnessSetupProfileId));
        var providerSessionIdHash = NormalizeOptionalHash(request.ProviderSessionIdHash, nameof(request.ProviderSessionIdHash));
        var startedAtUtc = request.StartedAtUtc?.ToUniversalTime();
        var endedAtUtc = request.EndedAtUtc?.ToUniversalTime();
        var now = clock.UtcNow.ToUniversalTime();

        if (startedAtUtc is not null && endedAtUtc is not null && endedAtUtc < startedAtUtc)
        {
            throw new ArgumentException("Session end must not be before session start.", nameof(request));
        }

        ValidateMetricQuality(request.TokenMetricStatus, request.TokenMetricConfidence);

        lock (gate)
        {
            RequireCustomerOrganization(request.CustomerOrganizationId);
            RequireProductUserForCustomerOrganization(request.CustomerOrganizationId, request.ProductUserId);

            var lookupKey = new AgentSessionLookupKey(
                request.CustomerOrganizationId,
                harnessSetupProfileId,
                request.ProductUserId,
                providerSessionIdHash ?? "partial-session");

            if (agentSessionLookupIndex.TryGetValue(lookupKey, out var existingSessionId))
            {
                var existing = agentSessions[existingSessionId];
                var updated = existing with
                {
                    StartedAtUtc = MinDateTimeOffset(existing.StartedAtUtc, startedAtUtc),
                    EndedAtUtc = MaxDateTimeOffset(existing.EndedAtUtc, endedAtUtc),
                    SessionStatus = ToWireSessionStatus(request.SessionStatus),
                    RepositoryEvidenceState = ToWireRepositoryEvidenceState(request.RepositoryEvidenceState),
                    ContentCaptureSummary = ToWireContentCaptureSummary(request.ContentCaptureSummary),
                    RecommendationStatus = ToWireRecommendationStatus(request.RecommendationStatus),
                    TokenMetricStatus = request.TokenMetricStatus,
                    TokenMetricConfidence = request.TokenMetricConfidence,
                    UpdatedAtUtc = now
                };

                agentSessions[existingSessionId] = updated;
                return Task.FromResult(updated);
            }

            var sessionId = $"agent-session-{Guid.NewGuid():N}";
            var session = new AgentSessionRecord(
                sessionId,
                request.CustomerOrganizationId,
                request.ProductUserId,
                harnessSetupProfileId,
                ToWireHarness(request.Harness),
                providerSessionIdHash,
                startedAtUtc,
                endedAtUtc,
                ToWireSessionStatus(request.SessionStatus),
                Environment: null,
                SandboxSetting: null,
                ApprovalSetting: null,
                ToWireRepositoryEvidenceState(request.RepositoryEvidenceState),
                ToWireContentCaptureSummary(request.ContentCaptureSummary),
                ToWireRecommendationStatus(request.RecommendationStatus),
                request.TokenMetricStatus,
                request.TokenMetricConfidence,
                ModelNames: [],
                SourceTelemetryEnvelopeIds: [],
                now,
                now);

            agentSessions.Add(sessionId, session);
            agentSessionLookupIndex.Add(lookupKey, sessionId);

            return Task.FromResult(session);
        }
    }

    public Task<IReadOnlyList<AgentSessionRecord>> ListAgentSessionsAsync(
        CustomerOrganizationId customerOrganizationId)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<AgentSessionRecord>>(
                agentSessions.Values
                    .Where(session => session.CustomerOrganizationId == customerOrganizationId)
                    .OrderBy(session => session.StartedAtUtc ?? session.CreatedAtUtc)
                    .ThenBy(session => session.AgentSessionId, StringComparer.Ordinal)
                    .ToArray());
        }
    }

    public Task<TokenObservationRecord> RecordTokenObservationAsync(
        CreateTokenObservationRecordRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var agentSessionId = NormalizeRequiredText(request.AgentSessionId, nameof(request.AgentSessionId));
        var modelInvocationId = string.IsNullOrWhiteSpace(request.ModelInvocationId)
            ? null
            : NormalizeOptionalMachineToken(request.ModelInvocationId, nameof(request.ModelInvocationId));
        var sourceTelemetryEnvelopeId = string.IsNullOrWhiteSpace(request.SourceTelemetryEnvelopeId)
            ? null
            : NormalizeRequiredText(request.SourceTelemetryEnvelopeId, nameof(request.SourceTelemetryEnvelopeId));
        var now = clock.UtcNow.ToUniversalTime();

        ValidateTokenObservationShape(request.Value, request.MetricStatus, request.MetricConfidence, request.SourceKind);

        lock (gate)
        {
            RequireCustomerOrganization(request.CustomerOrganizationId);

            if (!agentSessions.TryGetValue(agentSessionId, out var session) ||
                session.CustomerOrganizationId != request.CustomerOrganizationId)
            {
                throw new InvalidOperationException("Token observation session does not belong to the customer organization.");
            }

            if (sourceTelemetryEnvelopeId is not null &&
                (!telemetryEnvelopes.TryGetValue(sourceTelemetryEnvelopeId, out var envelope) ||
                    envelope.CustomerOrganizationId != request.CustomerOrganizationId))
            {
                throw new InvalidOperationException("Token observation source envelope does not belong to the customer organization.");
            }

            var observation = new TokenObservationRecord(
                TokenObservationId.NewId(),
                request.CustomerOrganizationId,
                agentSessionId,
                modelInvocationId,
                request.MetricName,
                request.Value,
                request.MetricStatus,
                request.MetricConfidence,
                request.SourceKind,
                sourceTelemetryEnvelopeId,
                now);

            tokenObservations.Add(observation.TokenObservationId, observation);

            return Task.FromResult(observation);
        }
    }

    public Task<IReadOnlyList<TokenObservationRecord>> ListTokenObservationsAsync(
        CustomerOrganizationId customerOrganizationId,
        string agentSessionId)
    {
        var normalizedAgentSessionId = NormalizeRequiredText(agentSessionId, nameof(agentSessionId));

        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<TokenObservationRecord>>(
                tokenObservations.Values
                    .Where(observation =>
                        observation.CustomerOrganizationId == customerOrganizationId &&
                        StringComparer.Ordinal.Equals(observation.AgentSessionId, normalizedAgentSessionId))
                    .OrderBy(observation => TokenMetricSortOrder(observation.MetricName))
                    .ThenBy(observation => observation.CreatedAtUtc)
                    .ThenBy(observation => observation.TokenObservationId.ToString(), StringComparer.Ordinal)
                    .ToArray());
        }
    }

    public Task<PricingBasisRecord> CreatePricingBasisRecordAsync(
        CreatePricingBasisRecordRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalized = NormalizePricingBasisRequest(request);
        var now = clock.UtcNow.ToUniversalTime();

        lock (gate)
        {
            RequireCustomerOrganization(request.CustomerOrganizationId);
            RequireGovernanceAuditEvent(request.CustomerOrganizationId, normalized.AuditEventId);

            var pricingBasisId = $"pricing-basis-{Guid.NewGuid():N}";
            var record = new PricingBasisRecord(
                pricingBasisId,
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

            pricingBasisRecords.Add(record.PricingBasisId, record);
            return Task.FromResult(record);
        }
    }

    public Task<PricingBasisRecord> CreatePricingSeedCandidateRecordAsync(
        CreatePricingBasisRecordRequest request,
        string correlationId)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.SourceKind != PricingSourceKind.AutomatedSeed ||
            request.ReviewState != PricingReviewState.Candidate)
        {
            throw new ArgumentException("Pricing seed records must be automated seed candidates.", nameof(request));
        }

        var normalized = NormalizePricingBasisRequest(request);
        var now = clock.UtcNow.ToUniversalTime();
        var normalizedCorrelationId = NormalizeRequiredText(correlationId, nameof(correlationId));

        lock (gate)
        {
            RequireCustomerOrganization(request.CustomerOrganizationId);

            var pricingBasisId = $"pricing-basis-{Guid.NewGuid():N}";
            var auditEvent = CreateGovernanceAuditEventUnderLock(
                normalized.AuditEventId,
                request.CustomerOrganizationId,
                actorProductUserId: null,
                effectiveRole: null,
                ProductAuthorizationAction.PricingManage,
                ProductScopeKind.Pricing.ToString(),
                pricingBasisId,
                "created",
                denialReason: null,
                normalizedCorrelationId,
                CreatePricingAuditMetadata(
                    operation: "pricing_seed_refresh",
                    result: "candidate",
                    pricingBasisId,
                    normalized,
                    request.TokenType,
                    request.SourceKind,
                    request.ReviewState),
                now);

            var record = new PricingBasisRecord(
                pricingBasisId,
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
                auditEvent.AuditEventId,
                normalized.SourceMetadata,
                now,
                now);

            pricingBasisRecords.Add(record.PricingBasisId, record);
            return Task.FromResult(record);
        }
    }

    public Task<PricingBasisRecord> CreateCustomerPricingOverrideAsync(
        CreatePricingBasisRecordRequest request,
        ProductUserId actorProductUserId,
        ProductRole actorEffectiveRole,
        string correlationId)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.SourceKind != PricingSourceKind.AdminOverride)
        {
            throw new ArgumentException("Customer pricing overrides must use admin override source kind.", nameof(request));
        }

        var normalized = NormalizePricingBasisRequest(request);
        var now = clock.UtcNow.ToUniversalTime();

        lock (gate)
        {
            RequireCustomerOrganization(request.CustomerOrganizationId);
            RequireProductUserForCustomerOrganization(request.CustomerOrganizationId, actorProductUserId);

            var pricingBasisId = $"pricing-basis-{Guid.NewGuid():N}";
            var auditEvent = CreateGovernanceAuditEventUnderLock(
                normalized.AuditEventId,
                request.CustomerOrganizationId,
                actorProductUserId,
                actorEffectiveRole,
                ProductAuthorizationAction.PricingManage,
                ProductScopeKind.Pricing.ToString(),
                pricingBasisId,
                "created",
                denialReason: null,
                correlationId,
                CreatePricingAuditMetadata(
                    operation: "pricing_override_create",
                    result: "created",
                    pricingBasisId,
                    normalized,
                    request.TokenType,
                    request.SourceKind,
                    request.ReviewState),
                now);

            var record = new PricingBasisRecord(
                pricingBasisId,
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
                auditEvent.AuditEventId,
                normalized.SourceMetadata,
                now,
                now);

            pricingBasisRecords.Add(record.PricingBasisId, record);
            return Task.FromResult(record);
        }
    }

    public Task<IReadOnlyList<PricingBasisRecord>> ListPricingBasisRecordsAsync(
        CustomerOrganizationId customerOrganizationId)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<PricingBasisRecord>>(
                pricingBasisRecords.Values
                    .Where(record => record.CustomerOrganizationId == customerOrganizationId)
                    .OrderBy(record => record.ProviderName, StringComparer.Ordinal)
                    .ThenBy(record => record.ModelName, StringComparer.Ordinal)
                    .ThenBy(record => record.TokenType)
                    .ThenBy(record => record.BillingRoute, StringComparer.Ordinal)
                    .ThenByDescending(record => record.EffectiveFromUtc)
                    .ThenBy(record => record.PricingBasisId, StringComparer.Ordinal)
                    .ToArray());
        }
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

    public Task<CostEstimateRecord> RecordCostEstimateAsync(
        CreateCostEstimateRecordRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalized = NormalizeCostEstimateRequest(request);
        var now = clock.UtcNow.ToUniversalTime();

        lock (gate)
        {
            RequireCustomerOrganization(request.CustomerOrganizationId);

            if (!agentSessions.TryGetValue(normalized.AgentSessionId, out var session) ||
                session.CustomerOrganizationId != request.CustomerOrganizationId)
            {
                throw new InvalidOperationException("Cost estimate session does not belong to the customer organization.");
            }

            if (normalized.PricingBasisId is not null)
            {
                if (!pricingBasisRecords.TryGetValue(normalized.PricingBasisId, out var basis) ||
                    basis.CustomerOrganizationId != request.CustomerOrganizationId)
                {
                    throw new InvalidOperationException("Cost estimate pricing basis does not belong to the customer organization.");
                }
            }

            var estimate = new CostEstimateRecord(
                $"cost-estimate-{Guid.NewGuid():N}",
                request.CustomerOrganizationId,
                normalized.AgentSessionId,
                normalized.ModelInvocationId,
                normalized.PricingBasisId,
                normalized.PricingVersion,
                normalized.Currency,
                request.EstimatedCost,
                request.CostStatus,
                request.SourceKind,
                request.TokenMetricStatus,
                request.TokenMetricConfidence,
                normalized.ProviderName,
                normalized.ModelName,
                normalized.BillingRoute,
                request.TokenType,
                now);

            costEstimates.Add(estimate.CostEstimateId, estimate);
            return Task.FromResult(estimate);
        }
    }

    public Task<TokenHotspotRecord> RecordTokenHotspotAsync(
        CreateTokenHotspotRecordRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var agentSessionId = NormalizeRequiredText(request.AgentSessionId, nameof(request.AgentSessionId));
        var modelName = NormalizeOptionalModelName(request.ModelName, nameof(request.ModelName));
        var evidenceSummary = NormalizeHotspotEvidenceSummary(request.EvidenceSummary);
        var evidenceReferenceIds = NormalizeHotspotEvidenceReferences(request.EvidenceReferenceIds);
        var detectionKey = NormalizeOptionalHotspotDetectionKey(request.DetectionKey);
        ValidateMetricQuality(request.MetricStatus, request.MetricConfidence);
        ValidateTokenHotspotAuthority(request);

        if (request.TokenBurnScore is < 0 or > 1)
        {
            throw new ArgumentException("Token burn score must be between 0 and 1.", nameof(request));
        }

        if (request.EstimatedCostImpact < 0)
        {
            throw new ArgumentException("Estimated cost impact must not be negative.", nameof(request));
        }

        lock (gate)
        {
            RequireCustomerOrganization(request.CustomerOrganizationId);

            if (detectionKey is not null)
            {
                var logicalKey = new TokenHotspotDetectionKey(request.CustomerOrganizationId, detectionKey);
                if (tokenHotspotDetectionKeyIndex.TryGetValue(logicalKey, out var existingHotspotId) &&
                    tokenHotspots.TryGetValue(existingHotspotId, out var existingHotspot))
                {
                    return Task.FromResult(existingHotspot);
                }
            }

            if (!agentSessions.TryGetValue(agentSessionId, out var session) ||
                session.CustomerOrganizationId != request.CustomerOrganizationId)
            {
                throw new InvalidOperationException("Token hotspot session does not belong to the customer organization.");
            }

            var hotspot = new TokenHotspotRecord(
                TokenHotspotId.NewId(),
                request.CustomerOrganizationId,
                agentSessionId,
                session.Harness,
                modelName,
                request.HotspotType,
                request.FindingState,
                request.AttributionType,
                request.Confidence,
                request.MetricStatus,
                request.MetricConfidence,
                request.PromptCacheEvidenceState,
                evidenceSummary,
                evidenceReferenceIds,
                detectionKey,
                request.TokenBurnScore,
                request.EstimatedCostImpact,
                clock.UtcNow.ToUniversalTime());

            tokenHotspots.Add(hotspot.TokenHotspotId, hotspot);
            if (detectionKey is not null)
            {
                tokenHotspotDetectionKeyIndex.Add(
                    new TokenHotspotDetectionKey(request.CustomerOrganizationId, detectionKey),
                    hotspot.TokenHotspotId);
            }

            return Task.FromResult(hotspot);
        }
    }

    public Task<IReadOnlyList<TokenHotspotRecord>> ListTokenHotspotsAsync(
        CustomerOrganizationId customerOrganizationId,
        string agentSessionId)
    {
        var normalizedAgentSessionId = NormalizeRequiredText(agentSessionId, nameof(agentSessionId));

        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<TokenHotspotRecord>>(
                tokenHotspots.Values
                    .Where(hotspot =>
                        hotspot.CustomerOrganizationId == customerOrganizationId &&
                        StringComparer.Ordinal.Equals(hotspot.AgentSessionId, normalizedAgentSessionId))
                    .OrderBy(hotspot => hotspot.CreatedAtUtc)
                    .ThenBy(hotspot => hotspot.TokenHotspotId.ToString(), StringComparer.Ordinal)
                    .ToArray());
        }
    }

    public Task<RecommendationPromptTemplateRecord> CreateRecommendationPromptTemplateAsync(
        CreateRecommendationPromptTemplateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var promptTemplateVersion = NormalizeSafeResourceId(
            request.PromptTemplateVersion,
            nameof(request.PromptTemplateVersion));
        var promptTextHash = NormalizeHash(request.PromptTextHash, nameof(request.PromptTextHash));
        var structuredOutputSchemaVersion = NormalizeSafeResourceId(
            request.StructuredOutputSchemaVersion,
            nameof(request.StructuredOutputSchemaVersion));
        var policyConstraintsJson = NormalizeJsonObject(
            request.PolicyConstraintsJson,
            nameof(request.PolicyConstraintsJson));
        var auditEventId = NormalizeRequiredText(request.AuditEventId, nameof(request.AuditEventId));
        var correlationId = NormalizeRequiredText(request.CorrelationId, nameof(request.CorrelationId));

        lock (gate)
        {
            RequireCustomerOrganization(request.CustomerOrganizationId);
            RequireProductUserForCustomerOrganization(request.CustomerOrganizationId, request.ActorProductUserId);

            var key = new RecommendationPromptTemplateKey(request.CustomerOrganizationId, promptTemplateVersion);
            if (recommendationPromptTemplates.ContainsKey(key))
            {
                throw new InvalidOperationException("Recommendation prompt template versions are immutable.");
            }

            var now = clock.UtcNow.ToUniversalTime();
            var template = new RecommendationPromptTemplateRecord(
                request.CustomerOrganizationId,
                promptTemplateVersion,
                request.Purpose,
                request.State,
                promptTextHash,
                structuredOutputSchemaVersion,
                policyConstraintsJson,
                auditEventId,
                now,
                request.State == RecommendationPromptTemplateState.Active ? now : null);

            CreateGovernanceAuditEventUnderLock(
                auditEventId,
                request.CustomerOrganizationId,
                request.ActorProductUserId,
                request.EffectiveRole,
                ProductAuthorizationAction.TenantUpdate,
                targetResourceKind: "recommendation_prompt_template",
                targetResourceId: promptTemplateVersion,
                decision: "created",
                denialReason: null,
                correlationId,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["operation"] = "recommendation_prompt_template_change",
                    ["result"] = ToWireRecommendationPromptTemplateState(request.State),
                    ["prompt_template_version"] = promptTemplateVersion,
                    ["structured_output_schema_version"] = structuredOutputSchemaVersion
                },
                now);

            recommendationPromptTemplates.Add(key, template);
            return Task.FromResult(template);
        }
    }

    public Task<RecommendationPromptTemplateRecord?> FindRecommendationPromptTemplateAsync(
        CustomerOrganizationId customerOrganizationId,
        string promptTemplateVersion)
    {
        var normalizedVersion = NormalizeSafeResourceId(promptTemplateVersion, nameof(promptTemplateVersion));

        lock (gate)
        {
            return Task.FromResult(
                recommendationPromptTemplates.TryGetValue(
                    new RecommendationPromptTemplateKey(customerOrganizationId, normalizedVersion),
                    out var template)
                    ? template
                    : null);
        }
    }

    public Task<RecommendationModelPolicyRecord> CreateRecommendationModelPolicyAsync(
        CreateRecommendationModelPolicyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var policyVersionId = NormalizeSafeResourceId(request.PolicyVersionId, nameof(request.PolicyVersionId));
        var primaryDeploymentAlias = NormalizeDeploymentAlias(request.PrimaryDeploymentAlias, nameof(request.PrimaryDeploymentAlias));
        var fallbackDeploymentAlias = string.IsNullOrWhiteSpace(request.FallbackDeploymentAlias)
            ? null
            : NormalizeDeploymentAlias(request.FallbackDeploymentAlias, nameof(request.FallbackDeploymentAlias));
        var region = NormalizeRequiredText(request.Region, nameof(request.Region)).ToLowerInvariant();
        var modelFamilyOrSku = NormalizeSafeResourceId(request.ModelFamilyOrSku, nameof(request.ModelFamilyOrSku));
        var modelVersion = string.IsNullOrWhiteSpace(request.ModelVersion)
            ? null
            : NormalizeSafeResourceId(request.ModelVersion, nameof(request.ModelVersion));
        var promptTemplateVersion = NormalizeSafeResourceId(
            request.PromptTemplateVersion,
            nameof(request.PromptTemplateVersion));
        var structuredOutputSchemaVersion = NormalizeSafeResourceId(
            request.StructuredOutputSchemaVersion,
            nameof(request.StructuredOutputSchemaVersion));
        var auditEventId = NormalizeRequiredText(request.AuditEventId, nameof(request.AuditEventId));
        var correlationId = NormalizeRequiredText(request.CorrelationId, nameof(request.CorrelationId));

        if (request.AllowedEvidenceClasses.Count == 0)
        {
            throw new ArgumentException("Recommendation model policy requires allowed evidence classes.", nameof(request));
        }

        if (request.FallbackBehavior == RecommendationModelFallbackBehavior.FallbackDeploymentThenDeterministic &&
            fallbackDeploymentAlias is null)
        {
            throw new ArgumentException("Fallback deployment behavior requires a fallback deployment alias.", nameof(request));
        }

        lock (gate)
        {
            RequireCustomerOrganization(request.CustomerOrganizationId);
            RequireProductUserForCustomerOrganization(request.CustomerOrganizationId, request.ActorProductUserId);
            if (!recommendationPromptTemplates.TryGetValue(
                    new RecommendationPromptTemplateKey(request.CustomerOrganizationId, promptTemplateVersion),
                    out var promptTemplate) ||
                promptTemplate.State != RecommendationPromptTemplateState.Active)
            {
                throw new InvalidOperationException("Recommendation model policy requires an active prompt template version.");
            }

            var key = new RecommendationModelPolicyKey(request.CustomerOrganizationId, policyVersionId);
            if (recommendationModelPolicies.ContainsKey(key))
            {
                throw new InvalidOperationException("Recommendation model policy versions are immutable.");
            }

            if (request.State == RecommendationModelPolicyState.Active &&
                activeRecommendationModelPolicyIndex.TryGetValue(request.CustomerOrganizationId, out var activePolicyVersionId) &&
                !StringComparer.Ordinal.Equals(activePolicyVersionId, policyVersionId))
            {
                throw new InvalidOperationException("Only one active recommendation model policy is allowed per customer organization.");
            }

            var now = clock.UtcNow.ToUniversalTime();
            var policy = new RecommendationModelPolicyRecord(
                request.CustomerOrganizationId,
                policyVersionId,
                request.State,
                request.Provider,
                primaryDeploymentAlias,
                fallbackDeploymentAlias,
                region,
                modelFamilyOrSku,
                modelVersion,
                promptTemplateVersion,
                structuredOutputSchemaVersion,
                request.AllowedEvidenceClasses.Distinct().ToArray(),
                request.FallbackBehavior,
                request.LlmAssistedEnabled,
                auditEventId,
                now,
                request.State == RecommendationModelPolicyState.Active ? now : null);

            CreateGovernanceAuditEventUnderLock(
                auditEventId,
                request.CustomerOrganizationId,
                request.ActorProductUserId,
                request.EffectiveRole,
                ProductAuthorizationAction.TenantUpdate,
                targetResourceKind: "recommendation_model_policy",
                targetResourceId: policyVersionId,
                decision: "created",
                denialReason: null,
                correlationId,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["operation"] = "recommendation_model_policy_change",
                    ["result"] = ToWireRecommendationModelPolicyState(request.State),
                    ["provider_name"] = ToWireRecommendationModelProvider(request.Provider),
                    ["deployment_alias"] = primaryDeploymentAlias,
                    ["fallback_behavior"] = ToWireRecommendationModelFallbackBehavior(request.FallbackBehavior),
                    ["recommendation_model_policy_version"] = policyVersionId,
                    ["prompt_template_version"] = promptTemplateVersion,
                    ["structured_output_schema_version"] = structuredOutputSchemaVersion
                },
                now);

            recommendationModelPolicies.Add(key, policy);
            if (policy.State == RecommendationModelPolicyState.Active)
            {
                activeRecommendationModelPolicyIndex[request.CustomerOrganizationId] = policyVersionId;
            }

            return Task.FromResult(policy);
        }
    }

    public Task<RecommendationModelPolicyRecord?> GetActiveRecommendationModelPolicyAsync(
        CustomerOrganizationId customerOrganizationId)
    {
        lock (gate)
        {
            if (!activeRecommendationModelPolicyIndex.TryGetValue(customerOrganizationId, out var policyVersionId))
            {
                return Task.FromResult<RecommendationModelPolicyRecord?>(null);
            }

            return Task.FromResult(
                recommendationModelPolicies.TryGetValue(
                    new RecommendationModelPolicyKey(customerOrganizationId, policyVersionId),
                    out var policy)
                    ? policy
                    : null);
        }
    }

    public Task<RecommendationLlmGenerationFailureRecord> RecordRecommendationLlmGenerationFailureAsync(
        CreateRecommendationLlmGenerationFailureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var agentSessionId = NormalizeRequiredText(request.AgentSessionId, nameof(request.AgentSessionId));
        var failureCode = NormalizeSafeResourceId(request.FailureCode, nameof(request.FailureCode));
        var provider = NormalizeSafeResourceId(request.Provider, nameof(request.Provider));
        var deploymentAlias = NormalizeDeploymentAlias(request.DeploymentAlias, nameof(request.DeploymentAlias));
        var policyVersionId = NormalizeSafeResourceId(request.PolicyVersionId, nameof(request.PolicyVersionId));
        var promptTemplateVersion = NormalizeSafeResourceId(
            request.PromptTemplateVersion,
            nameof(request.PromptTemplateVersion));
        var evidencePacketHash = NormalizeSha256Hex(request.EvidencePacketHash, nameof(request.EvidencePacketHash));
        var structuredOutputSchemaVersion = NormalizeSafeResourceId(
            request.StructuredOutputSchemaVersion,
            nameof(request.StructuredOutputSchemaVersion));
        var auditEventId = NormalizeRequiredText(request.AuditEventId, nameof(request.AuditEventId));
        var correlationId = NormalizeRequiredText(request.CorrelationId, nameof(request.CorrelationId));

        lock (gate)
        {
            RequireCustomerOrganization(request.CustomerOrganizationId);
            if (!agentSessions.TryGetValue(agentSessionId, out var session) ||
                session.CustomerOrganizationId != request.CustomerOrganizationId)
            {
                throw new InvalidOperationException("LLM generation failure session does not belong to the customer organization.");
            }

            if (request.TokenHotspotId is { } tokenHotspotId &&
                (!tokenHotspots.TryGetValue(tokenHotspotId, out var hotspot) ||
                    hotspot.CustomerOrganizationId != request.CustomerOrganizationId ||
                    !StringComparer.Ordinal.Equals(hotspot.AgentSessionId, agentSessionId)))
            {
                throw new InvalidOperationException("LLM generation failure hotspot does not belong to the customer organization and session.");
            }

            var now = clock.UtcNow.ToUniversalTime();
            var failure = new RecommendationLlmGenerationFailureRecord(
                RecommendationLlmGenerationFailureId.NewId(),
                request.CustomerOrganizationId,
                agentSessionId,
                request.TokenHotspotId,
                failureCode,
                provider,
                deploymentAlias,
                policyVersionId,
                promptTemplateVersion,
                evidencePacketHash,
                structuredOutputSchemaVersion,
                auditEventId,
                now);

            CreateGovernanceAuditEventUnderLock(
                auditEventId,
                request.CustomerOrganizationId,
                actorProductUserId: null,
                effectiveRole: null,
                ProductAuthorizationAction.RecommendationRegenerate,
                targetResourceKind: "recommendation_llm_generation_failure",
                targetResourceId: failure.RecommendationLlmGenerationFailureId.ToString(),
                decision: "created",
                denialReason: null,
                correlationId,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["operation"] = "recommendation_generation_rejection",
                    ["result"] = "rejected",
                    ["failure_code"] = failureCode,
                    ["provider_name"] = provider,
                    ["deployment_alias"] = deploymentAlias,
                    ["recommendation_model_policy_version"] = policyVersionId,
                    ["prompt_template_version"] = promptTemplateVersion,
                    ["structured_output_schema_version"] = structuredOutputSchemaVersion,
                    ["evidence_packet_version"] = "recommendation.evidence.v1"
                },
                now);

            recommendationLlmGenerationFailures.Add(failure.RecommendationLlmGenerationFailureId, failure);
            return Task.FromResult(failure);
        }
    }

    public Task<IReadOnlyList<RecommendationLlmGenerationFailureRecord>> ListRecommendationLlmGenerationFailuresAsync(
        CustomerOrganizationId customerOrganizationId)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<RecommendationLlmGenerationFailureRecord>>(
                recommendationLlmGenerationFailures.Values
                    .Where(failure => failure.CustomerOrganizationId == customerOrganizationId)
                    .OrderBy(failure => failure.CreatedAtUtc)
                    .ThenBy(failure => failure.RecommendationLlmGenerationFailureId.ToString(), StringComparer.Ordinal)
                    .ToArray());
        }
    }

    public Task<RecommendationRecord> CreateRecommendationAsync(
        CreateRecommendationRecordRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var agentSessionId = NormalizeRequiredText(request.AgentSessionId, nameof(request.AgentSessionId));
        var evidencePacketVersion = NormalizeRequiredText(request.EvidencePacketVersion, nameof(request.EvidencePacketVersion));
        var evidencePacketJson = NormalizeJsonObject(request.EvidencePacketJson, nameof(request.EvidencePacketJson));
        var evidencePacketHash = NormalizeSha256Hex(request.EvidencePacketHash, nameof(request.EvidencePacketHash));
        var summary = NormalizeRecommendationText(request.Summary, nameof(request.Summary));
        var rationale = NormalizeRecommendationText(request.Rationale, nameof(request.Rationale));
        var recommendedAction = NormalizeRecommendationText(request.RecommendedAction, nameof(request.RecommendedAction));
        var expectedBenefit = NormalizeRecommendationText(request.ExpectedBenefit, nameof(request.ExpectedBenefit));
        var evidenceReferenceIds = NormalizeRecommendationEvidenceReferences(request.EvidenceReferenceIds);
        var policyMetadata = NormalizeEvidenceMetadata(request.PolicyMetadata);
        var auditEventId = NormalizeRequiredText(request.AuditEventId, nameof(request.AuditEventId));
        var correlationId = NormalizeRequiredText(request.CorrelationId, nameof(request.CorrelationId));
        var ruleId = string.IsNullOrWhiteSpace(request.RuleId)
            ? null
            : NormalizeRequiredText(request.RuleId, nameof(request.RuleId));
        var generationKey = string.IsNullOrWhiteSpace(request.GenerationKey)
            ? null
            : NormalizeRequiredText(request.GenerationKey, nameof(request.GenerationKey));

        ValidateRecommendationAuthority(request);
        ValidateSafeRecommendationText(summary, rationale, recommendedAction, expectedBenefit);

        lock (gate)
        {
            RequireCustomerOrganization(request.CustomerOrganizationId);
            if (!agentSessions.TryGetValue(agentSessionId, out var session) ||
                session.CustomerOrganizationId != request.CustomerOrganizationId)
            {
                throw new InvalidOperationException("Recommendation session does not belong to the customer organization.");
            }

            if (request.TokenHotspotId is { } tokenHotspotId)
            {
                if (!tokenHotspots.TryGetValue(tokenHotspotId, out var hotspot) ||
                    hotspot.CustomerOrganizationId != request.CustomerOrganizationId ||
                    !StringComparer.Ordinal.Equals(hotspot.AgentSessionId, agentSessionId))
                {
                    throw new InvalidOperationException("Recommendation hotspot does not belong to the customer organization and session.");
                }
            }

            if (generationKey is not null)
            {
                var key = new RecommendationGenerationKey(request.CustomerOrganizationId, generationKey);
                if (recommendationGenerationKeyIndex.TryGetValue(key, out var existingRecommendationId) &&
                    recommendations.TryGetValue(existingRecommendationId, out var existingRecommendation))
                {
                    return Task.FromResult(existingRecommendation);
                }
            }

            var now = clock.UtcNow.ToUniversalTime();
            CreateGovernanceAuditEventUnderLock(
                auditEventId,
                request.CustomerOrganizationId,
                actorProductUserId: null,
                effectiveRole: null,
                ProductAuthorizationAction.RecommendationRegenerate,
                targetResourceKind: "recommendation",
                targetResourceId: generationKey ?? agentSessionId,
                decision: "created",
                denialReason: null,
                correlationId,
                new Dictionary<string, string>(policyMetadata, StringComparer.Ordinal)
                {
                    ["operation"] = "recommendation_generation",
                    ["result"] = ToWireRecommendationState(request.State),
                    ["recommendation_kind"] = ToWireRecommendationKind(request.Kind),
                    ["authority_state"] = ToWireRecommendationAuthorityState(request.AuthorityState),
                    ["validation_state"] = ToWireRecommendationValidationState(request.ValidationState),
                    ["evidence_packet_version"] = evidencePacketVersion
                },
                now);

            var recommendation = new RecommendationRecord(
                RecommendationId.NewId(),
                request.CustomerOrganizationId,
                agentSessionId,
                request.TokenHotspotId,
                ruleId,
                request.Kind,
                request.State,
                request.AuthorityState,
                request.Confidence,
                request.ValidationState,
                request.VisibilityScope,
                evidencePacketVersion,
                evidencePacketJson,
                evidencePacketHash,
                summary,
                rationale,
                recommendedAction,
                expectedBenefit,
                string.IsNullOrWhiteSpace(request.ModelPolicyVersionId) ? null : request.ModelPolicyVersionId.Trim(),
                string.IsNullOrWhiteSpace(request.PromptTemplateVersion) ? null : request.PromptTemplateVersion.Trim(),
                evidenceReferenceIds,
                policyMetadata,
                auditEventId,
                generationKey,
                now);

            recommendations.Add(recommendation.RecommendationId, recommendation);
            if (generationKey is not null)
            {
                recommendationGenerationKeyIndex.Add(
                    new RecommendationGenerationKey(request.CustomerOrganizationId, generationKey),
                    recommendation.RecommendationId);
            }

            foreach (var evidenceReferenceId in evidenceReferenceIds)
            {
                var evidence = new RecommendationEvidenceRecord(
                    RecommendationEvidenceId.NewId(),
                    request.CustomerOrganizationId,
                    recommendation.RecommendationId,
                    request.TokenHotspotId?.ToString() == evidenceReferenceId
                        ? RecommendationEvidenceKind.TokenHotspot
                        : RecommendationEvidenceKind.TokenObservation,
                    evidenceReferenceId,
                    request.AuthorityState == RecommendationAuthorityState.LlmInferredCandidate
                        ? RecommendationEvidenceState.LlmInferred
                        : RecommendationEvidenceState.Observed,
                    now);
                recommendationEvidenceRecords.Add(evidence.RecommendationEvidenceId, evidence);
            }

            return Task.FromResult(recommendation);
        }
    }

    public Task<IReadOnlyList<RecommendationRecord>> ListRecommendationsForSessionAsync(
        CustomerOrganizationId customerOrganizationId,
        string agentSessionId)
    {
        var normalizedAgentSessionId = NormalizeRequiredText(agentSessionId, nameof(agentSessionId));

        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<RecommendationRecord>>(
                recommendations.Values
                    .Where(recommendation =>
                        recommendation.CustomerOrganizationId == customerOrganizationId &&
                        StringComparer.Ordinal.Equals(recommendation.AgentSessionId, normalizedAgentSessionId))
                    .OrderBy(recommendation => recommendation.CreatedAtUtc)
                    .ThenBy(recommendation => recommendation.RecommendationId.ToString(), StringComparer.Ordinal)
                    .ToArray());
        }
    }

    public Task<RecommendationRecord?> FindRecommendationAsync(
        CustomerOrganizationId customerOrganizationId,
        RecommendationId recommendationId)
    {
        lock (gate)
        {
            return Task.FromResult(
                recommendations.TryGetValue(recommendationId, out var recommendation) &&
                recommendation.CustomerOrganizationId == customerOrganizationId
                    ? recommendation
                    : null);
        }
    }

    public Task<RecommendationRegenerationRequest> CreateRecommendationRegenerationRequestAsync(
        CreateRecommendationRegenerationRequest request,
        ProductUserId actorProductUserId,
        ProductRole actorEffectiveRole)
    {
        ArgumentNullException.ThrowIfNull(request);

        var reason = NormalizeRecommendationRegenerationReason(request.Reason, nameof(request.Reason));
        var auditEventId = NormalizeRequiredText(request.AuditEventId, nameof(request.AuditEventId));
        var correlationId = NormalizeRequiredText(request.CorrelationId, nameof(request.CorrelationId));
        if (request.AgentSessionId is null && request.TokenHotspotId is null)
        {
            throw new ArgumentException("Recommendation regeneration requires a session or hotspot target.", nameof(request));
        }

        lock (gate)
        {
            RequireCustomerOrganization(request.CustomerOrganizationId);
            RequireProductUserForCustomerOrganization(request.CustomerOrganizationId, actorProductUserId);

            var normalizedAgentSessionId = string.IsNullOrWhiteSpace(request.AgentSessionId)
                ? null
                : NormalizeRequiredText(request.AgentSessionId, nameof(request.AgentSessionId));
            if (normalizedAgentSessionId is not null &&
                (!agentSessions.TryGetValue(normalizedAgentSessionId, out var session) ||
                    session.CustomerOrganizationId != request.CustomerOrganizationId))
            {
                throw new InvalidOperationException("Recommendation regeneration session does not belong to the customer organization.");
            }

            if (request.TokenHotspotId is { } tokenHotspotId &&
                (!tokenHotspots.TryGetValue(tokenHotspotId, out var hotspot) ||
                    hotspot.CustomerOrganizationId != request.CustomerOrganizationId))
            {
                throw new InvalidOperationException("Recommendation regeneration hotspot does not belong to the customer organization.");
            }

            var now = clock.UtcNow.ToUniversalTime();
            var regenerationRequest = new RecommendationRegenerationRequest(
                RecommendationRegenerationRequestId.NewId(),
                request.CustomerOrganizationId,
                normalizedAgentSessionId,
                request.TokenHotspotId,
                reason,
                RecommendationRegenerationState.Queued,
                auditEventId,
                correlationId,
                now);

            CreateGovernanceAuditEventUnderLock(
                auditEventId,
                request.CustomerOrganizationId,
                actorProductUserId,
                actorEffectiveRole,
                ProductAuthorizationAction.RecommendationRegenerate,
                targetResourceKind: "recommendation_regeneration_request",
                targetResourceId: regenerationRequest.RecommendationRegenerationRequestId.ToString(),
                decision: "created",
                denialReason: null,
                correlationId,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["operation"] = "recommendation_regeneration_request",
                    ["result"] = "queued",
                    ["reason"] = reason,
                    ["agent_session_id"] = normalizedAgentSessionId ?? "not_provided",
                    ["token_hotspot_id"] = request.TokenHotspotId?.ToString() ?? "not_provided"
                },
                now);

            recommendationRegenerationRequests.Add(
                regenerationRequest.RecommendationRegenerationRequestId,
                regenerationRequest);

            return Task.FromResult(regenerationRequest);
        }
    }

    public Task<IReadOnlyList<RecommendationRegenerationRequest>> ListRecommendationRegenerationRequestsAsync(
        CustomerOrganizationId customerOrganizationId)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<RecommendationRegenerationRequest>>(
                recommendationRegenerationRequests.Values
                    .Where(request => request.CustomerOrganizationId == customerOrganizationId)
                    .OrderBy(request => request.CreatedAtUtc)
                    .ThenBy(request => request.RecommendationRegenerationRequestId.ToString(), StringComparer.Ordinal)
                    .ToArray());
        }
    }

    public Task<ProductApiIdempotencyReservation> ReserveProductApiIdempotencyRecordAsync(
        ReserveProductApiIdempotencyRecordRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRoute = NormalizeRequiredText(request.Route, nameof(request.Route));
        var normalizedIdempotencyKey = NormalizeRequiredText(request.IdempotencyKey, nameof(request.IdempotencyKey));
        var normalizedRequestHash = NormalizeRequiredText(request.RequestHash, nameof(request.RequestHash));
        var now = clock.UtcNow.ToUniversalTime();

        if (request.ExpiresAtUtc.ToUniversalTime() <= now)
        {
            throw new ArgumentException("Idempotency expiry must be in the future.", nameof(request));
        }

        lock (gate)
        {
            RequireCustomerOrganization(request.CustomerOrganizationId);
            RequireProductUserForCustomerOrganization(request.CustomerOrganizationId, request.ProductUserId);

            var key = new ProductApiIdempotencyKey(
                request.CustomerOrganizationId,
                request.ProductUserId,
                normalizedRoute,
                normalizedIdempotencyKey);
            if (productApiIdempotencyRecords.TryGetValue(key, out var existing))
            {
                if (existing.ExpiresAtUtc <= now)
                {
                    productApiIdempotencyRecords.Remove(key);
                }
                else if (!StringComparer.Ordinal.Equals(existing.RequestHash, normalizedRequestHash))
                {
                    return Task.FromResult(new ProductApiIdempotencyReservation(ProductApiIdempotencyReservationState.Conflict, existing));
                }
                else if (existing.IsCompleted)
                {
                    return Task.FromResult(new ProductApiIdempotencyReservation(ProductApiIdempotencyReservationState.Replay, existing));
                }
                else
                {
                    return Task.FromResult(new ProductApiIdempotencyReservation(ProductApiIdempotencyReservationState.InProgress, existing));
                }
            }

            var reserved = new ProductApiIdempotencyRecord(
                request.CustomerOrganizationId,
                request.ProductUserId,
                normalizedRoute,
                normalizedIdempotencyKey,
                normalizedRequestHash,
                OperationId: null,
                ResponseStatusCode: null,
                ResponseLocation: null,
                ResponseJson: null,
                CreatedAtUtc: now,
                ExpiresAtUtc: request.ExpiresAtUtc.ToUniversalTime(),
                CompletedAtUtc: null);
            productApiIdempotencyRecords[key] = reserved;
            return Task.FromResult(new ProductApiIdempotencyReservation(ProductApiIdempotencyReservationState.Reserved, reserved));
        }
    }

    public Task<ProductApiIdempotencyRecord> CompleteProductApiIdempotencyRecordAsync(
        CompleteProductApiIdempotencyRecordRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRoute = NormalizeRequiredText(request.Route, nameof(request.Route));
        var normalizedIdempotencyKey = NormalizeRequiredText(request.IdempotencyKey, nameof(request.IdempotencyKey));
        var normalizedRequestHash = NormalizeRequiredText(request.RequestHash, nameof(request.RequestHash));
        var normalizedOperationId = NormalizeRequiredText(request.OperationId, nameof(request.OperationId));
        var normalizedResponseJson = NormalizeRequiredText(request.ResponseJson, nameof(request.ResponseJson));
        var normalizedLocation = string.IsNullOrWhiteSpace(request.ResponseLocation)
            ? null
            : NormalizeRequiredText(request.ResponseLocation, nameof(request.ResponseLocation));
        var now = clock.UtcNow.ToUniversalTime();

        if (request.ResponseStatusCode < 200 || request.ResponseStatusCode > 299)
        {
            throw new ArgumentException("Idempotency response status code must be a successful status code.", nameof(request));
        }

        lock (gate)
        {
            var key = new ProductApiIdempotencyKey(
                request.CustomerOrganizationId,
                request.ProductUserId,
                normalizedRoute,
                normalizedIdempotencyKey);
            if (!productApiIdempotencyRecords.TryGetValue(key, out var existing) ||
                !StringComparer.Ordinal.Equals(existing.RequestHash, normalizedRequestHash))
            {
                throw new InvalidOperationException("Product API idempotency reservation was not found.");
            }

            var completed = existing with
            {
                OperationId = normalizedOperationId,
                ResponseStatusCode = request.ResponseStatusCode,
                ResponseLocation = normalizedLocation,
                ResponseJson = normalizedResponseJson,
                CompletedAtUtc = now
            };
            productApiIdempotencyRecords[key] = completed;
            return Task.FromResult(completed);
        }
    }

    public Task<IReadOnlyList<CostEstimateRecord>> ListCostEstimatesAsync(
        CustomerOrganizationId customerOrganizationId)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<CostEstimateRecord>>(
                costEstimates.Values
                    .Where(estimate => estimate.CustomerOrganizationId == customerOrganizationId)
                    .OrderBy(estimate => estimate.CreatedAtUtc)
                    .ThenBy(estimate => estimate.CostEstimateId, StringComparer.Ordinal)
                    .ToArray());
        }
    }

    public Task<IReadOnlyList<CostMixBucket>> ListCostMixAsync(
        CustomerOrganizationId customerOrganizationId)
    {
        lock (gate)
        {
            var buckets = costEstimates.Values
                .Where(estimate => estimate.CustomerOrganizationId == customerOrganizationId)
                .GroupBy(
                    estimate => new
                    {
                        estimate.ProviderName,
                        estimate.ModelName,
                        estimate.BillingRoute,
                        estimate.TokenType,
                        estimate.CostStatus,
                        estimate.Currency
                    })
                .Select(group => new CostMixBucket(
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
                    AggregateCostMixMetricStatus(group.Select(estimate => estimate.TokenMetricStatus).ToArray())))
                .OrderBy(bucket => bucket.ProviderName, StringComparer.Ordinal)
                .ThenBy(bucket => bucket.ModelName, StringComparer.Ordinal)
                .ThenBy(bucket => bucket.TokenType)
                .ThenBy(bucket => bucket.BillingRoute, StringComparer.Ordinal)
                .ThenBy(bucket => bucket.CostStatus)
                .ToArray();

            return Task.FromResult<IReadOnlyList<CostMixBucket>>(buckets);
        }
    }

    public async Task<CostEstimateRecord> EstimateAndRecordTokenObservationCostAsync(
        EstimateTokenObservationCostRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        IReadOnlyList<PricingBasisRecord> basisRecords;
        lock (gate)
        {
            RequireCustomerOrganization(request.CustomerOrganizationId);
            basisRecords = pricingBasisRecords.Values
                .Where(record => record.CustomerOrganizationId == request.CustomerOrganizationId)
                .ToArray();
        }

        var estimateRequest = PricingCostCalculator.EstimateTokenObservationCost(request, basisRecords);
        return await RecordCostEstimateAsync(estimateRequest);
    }

    public Task<BudgetPolicyRecord> CreateBudgetPolicyAsync(
        CreateBudgetPolicyRequest request,
        ProductUserId actorProductUserId,
        ProductRole actorEffectiveRole)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = clock.UtcNow.ToUniversalTime();
        var normalized = NormalizeBudgetPolicyRequest(
            request.ScopeKind,
            request.ScopeId,
            request.MetricKind,
            request.ThresholdJson,
            request.Status);
        var auditEventId = NormalizeRequiredText(request.AuditEventId, nameof(request.AuditEventId));
        var correlationId = NormalizeRequiredText(request.CorrelationId, nameof(request.CorrelationId));

        lock (gate)
        {
            RequireCustomerOrganization(request.CustomerOrganizationId);
            RequireProductUserForCustomerOrganization(request.CustomerOrganizationId, actorProductUserId);

            var record = new BudgetPolicyRecord(
                $"budget-policy-{Guid.NewGuid():N}",
                request.CustomerOrganizationId,
                normalized.ScopeKind,
                normalized.ScopeId,
                normalized.MetricKind,
                normalized.ThresholdJson,
                normalized.Status,
                auditEventId,
                now,
                now);

            CreateGovernanceAuditEventUnderLock(
                auditEventId,
                request.CustomerOrganizationId,
                actorProductUserId,
                actorEffectiveRole,
                ProductAuthorizationAction.BudgetManage,
                ToWireBudgetScopeKind(record.ScopeKind),
                record.BudgetPolicyId,
                "created",
                denialReason: null,
                correlationId,
                CreateBudgetAuditMetadata("budget_policy_created", record),
                now);

            budgetPolicies.Add(record.BudgetPolicyId, record);
            return Task.FromResult(record);
        }
    }

    public Task<IReadOnlyList<BudgetPolicyRecord>> ListBudgetPoliciesAsync(
        CustomerOrganizationId customerOrganizationId)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<BudgetPolicyRecord>>(
                budgetPolicies.Values
                    .Where(policy => policy.CustomerOrganizationId == customerOrganizationId)
                    .OrderBy(policy => policy.ScopeKind)
                    .ThenBy(policy => policy.ScopeId, StringComparer.Ordinal)
                    .ThenBy(policy => policy.MetricKind)
                    .ThenBy(policy => policy.BudgetPolicyId, StringComparer.Ordinal)
                    .ToArray());
        }
    }

    public Task<BudgetPolicyRecord> UpdateBudgetPolicyAsync(
        UpdateBudgetPolicyRequest request,
        ProductUserId actorProductUserId,
        ProductRole actorEffectiveRole)
    {
        ArgumentNullException.ThrowIfNull(request);

        var budgetPolicyId = NormalizeRequiredText(request.BudgetPolicyId, nameof(request.BudgetPolicyId));
        var auditEventId = NormalizeRequiredText(request.AuditEventId, nameof(request.AuditEventId));
        var correlationId = NormalizeRequiredText(request.CorrelationId, nameof(request.CorrelationId));
        var now = clock.UtcNow.ToUniversalTime();

        lock (gate)
        {
            RequireCustomerOrganization(request.CustomerOrganizationId);
            RequireProductUserForCustomerOrganization(request.CustomerOrganizationId, actorProductUserId);

            if (!budgetPolicies.TryGetValue(budgetPolicyId, out var existing) ||
                existing.CustomerOrganizationId != request.CustomerOrganizationId)
            {
                throw new InvalidOperationException("Budget policy does not belong to the customer organization.");
            }

            var normalized = NormalizeBudgetPolicyRequest(
                request.ScopeKind ?? existing.ScopeKind,
                request.ScopeKind is null && request.ScopeId is null ? existing.ScopeId : request.ScopeId,
                request.MetricKind ?? existing.MetricKind,
                request.ThresholdJson ?? existing.ThresholdJson,
                request.Status ?? existing.Status);

            var updated = existing with
            {
                ScopeKind = normalized.ScopeKind,
                ScopeId = normalized.ScopeId,
                MetricKind = normalized.MetricKind,
                ThresholdJson = normalized.ThresholdJson,
                Status = normalized.Status,
                AuditEventId = auditEventId,
                UpdatedAtUtc = now
            };

            CreateGovernanceAuditEventUnderLock(
                auditEventId,
                request.CustomerOrganizationId,
                actorProductUserId,
                actorEffectiveRole,
                ProductAuthorizationAction.BudgetManage,
                ToWireBudgetScopeKind(updated.ScopeKind),
                updated.BudgetPolicyId,
                "updated",
                denialReason: null,
                correlationId,
                CreateBudgetAuditMetadata("budget_policy_updated", updated),
                now);

            budgetPolicies[budgetPolicyId] = updated;
            return Task.FromResult(updated);
        }
    }

    public Task<AggregateMetricPointRecord> RecordAggregateMetricPointAsync(
        CreateAggregateMetricPointRecordRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (agentSessionId, name, unit, labels) = NormalizeAggregateMetricPointRequest(request);
        var now = clock.UtcNow.ToUniversalTime();

        lock (gate)
        {
            RequireCustomerOrganization(request.CustomerOrganizationId);

            if (!agentSessions.TryGetValue(agentSessionId, out var session) ||
                session.CustomerOrganizationId != request.CustomerOrganizationId)
            {
                throw new InvalidOperationException("Aggregate metric session does not belong to the customer organization.");
            }

            var point = new AggregateMetricPointRecord(
                AggregateMetricPointId.NewId(),
                request.CustomerOrganizationId,
                agentSessionId,
                name,
                request.Value,
                unit,
                labels,
                now);

            aggregateMetricPoints.Add(point.AggregateMetricPointId, point);

            return Task.FromResult(point);
        }
    }

    public Task ValidateAggregateMetricPointAsync(
        CreateAggregateMetricPointRecordRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (agentSessionId, _, _, _) = NormalizeAggregateMetricPointRequest(request);

        lock (gate)
        {
            RequireCustomerOrganization(request.CustomerOrganizationId);

            if (!agentSessions.TryGetValue(agentSessionId, out var session) ||
                session.CustomerOrganizationId != request.CustomerOrganizationId)
            {
                throw new InvalidOperationException("Aggregate metric session does not belong to the customer organization.");
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AggregateMetricPointRecord>> ListAggregateMetricPointsAsync(
        CustomerOrganizationId customerOrganizationId)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<AggregateMetricPointRecord>>(
                aggregateMetricPoints.Values
                    .Where(point => point.CustomerOrganizationId == customerOrganizationId)
                    .OrderBy(point => point.ExportedAtUtc)
                    .ThenBy(point => point.AggregateMetricPointId.ToString(), StringComparer.Ordinal)
                    .ToArray());
        }
    }

    public Task RecordAggregateTokenTimelineBucketsAsync(
        CustomerOrganizationId customerOrganizationId,
        IReadOnlyList<AggregateTokenTimelineBucket> buckets)
    {
        ArgumentNullException.ThrowIfNull(buckets);

        var normalizedBuckets = buckets
            .Select(bucket => NormalizeAggregateTokenTimelineBucket(customerOrganizationId, bucket))
            .ToArray();

        lock (gate)
        {
            RequireCustomerOrganization(customerOrganizationId);
            var organization = customerOrganizations[customerOrganizationId];

            foreach (var bucket in normalizedBuckets)
            {
                if (!StringComparer.Ordinal.Equals(bucket.CustomerOrganizationSlug, organization.Slug) ||
                    !StringComparer.Ordinal.Equals(bucket.Region, organization.DataResidencyRegion))
                {
                    throw new ArgumentException("Aggregate token timeline bucket tenant labels do not match the organization.", nameof(buckets));
                }
            }

            var dates = normalizedBuckets
                .Select(static bucket => bucket.BucketDateUtc)
                .ToHashSet();
            var windows = normalizedBuckets
                .Select(static bucket => bucket.MovingAverageWindowDays)
                .ToHashSet();

            aggregateTokenTimelineBuckets.RemoveAll(bucket =>
                bucket.CustomerOrganizationId == customerOrganizationId &&
                dates.Contains(bucket.BucketDateUtc) &&
                windows.Contains(bucket.MovingAverageWindowDays));
            aggregateTokenTimelineBuckets.AddRange(normalizedBuckets);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AggregateTokenTimelineBucket>> ListAggregateTokenTimelineBucketsAsync(
        CustomerOrganizationId customerOrganizationId,
        AggregateTokenTimelineQuery query)
    {
        ValidateAggregateTokenTimelineQuery(query);

        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<AggregateTokenTimelineBucket>>(
                aggregateTokenTimelineBuckets
                    .Where(bucket =>
                        bucket.CustomerOrganizationId == customerOrganizationId &&
                        bucket.BucketDateUtc >= query.StartDateUtc &&
                        bucket.BucketDateUtc <= query.EndDateUtc &&
                        bucket.MovingAverageWindowDays == query.MovingAverageWindowDays)
                    .OrderBy(static bucket => bucket.BucketDateUtc)
                    .ToArray());
        }
    }

    public Task<AggregateMetricExportFailureRecord> RecordAggregateMetricExportFailureAsync(
        CreateAggregateMetricExportFailureRecordRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var agentSessionId = NormalizeRequiredText(request.AgentSessionId, nameof(request.AgentSessionId));
        var failureReason = NormalizeAggregateMetricExportFailureReason(request.FailureReason, nameof(request.FailureReason));
        var correlationId = NormalizeRequiredText(request.CorrelationId, nameof(request.CorrelationId));
        var now = clock.UtcNow.ToUniversalTime();

        lock (gate)
        {
            RequireCustomerOrganization(request.CustomerOrganizationId);

            if (!agentSessions.TryGetValue(agentSessionId, out var session) ||
                session.CustomerOrganizationId != request.CustomerOrganizationId)
            {
                throw new InvalidOperationException("Aggregate metric export failure session does not belong to the customer organization.");
            }

            var failure = new AggregateMetricExportFailureRecord(
                AggregateMetricExportFailureId.NewId(),
                request.CustomerOrganizationId,
                agentSessionId,
                failureReason,
                correlationId,
                now);

            aggregateMetricExportFailures.Add(failure.AggregateMetricExportFailureId, failure);

            return Task.FromResult(failure);
        }
    }

    public Task<IReadOnlyList<AggregateMetricExportFailureRecord>> ListAggregateMetricExportFailuresAsync(
        CustomerOrganizationId customerOrganizationId)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<AggregateMetricExportFailureRecord>>(
                aggregateMetricExportFailures.Values
                    .Where(failure => failure.CustomerOrganizationId == customerOrganizationId)
                    .OrderBy(failure => failure.CreatedAtUtc)
                    .ThenBy(failure => failure.AggregateMetricExportFailureId.ToString(), StringComparer.Ordinal)
                    .ToArray());
        }
    }

    public Task<ScopedIngestionCredential?> FindScopedIngestionCredentialAsync(
        CustomerOrganizationId customerOrganizationId,
        ScopedIngestionCredentialId scopedIngestionCredentialId)
    {
        lock (gate)
        {
            return Task.FromResult(
                scopedIngestionCredentials.TryGetValue(scopedIngestionCredentialId, out var credential) &&
                    credential.CustomerOrganizationId == customerOrganizationId
                    ? credential
                    : null);
        }
    }

    public Task<IReadOnlyList<ScopedIngestionCredential>> ListScopedIngestionCredentialsAsync(
        CustomerOrganizationId customerOrganizationId)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<ScopedIngestionCredential>>(
                scopedIngestionCredentials.Values
                    .Where(credential => credential.CustomerOrganizationId == customerOrganizationId)
                    .OrderBy(credential => credential.CreatedAtUtc)
                    .ThenBy(credential => credential.ScopedIngestionCredentialId.ToString(), StringComparer.Ordinal)
                    .ToArray());
        }
    }

    public Task<IReadOnlyList<ScopedIngestionCredential>> ListScopedIngestionCredentialsForValidationAsync()
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<ScopedIngestionCredential>>(
                scopedIngestionCredentials.Values
                    .OrderBy(credential => credential.CreatedAtUtc)
                    .ThenBy(credential => credential.ScopedIngestionCredentialId.ToString(), StringComparer.Ordinal)
                    .ToArray());
        }
    }

    public Task<ScopedIngestionCredential> CreateScopedIngestionCredentialAsync(
        CustomerOrganizationId customerOrganizationId,
        CreateScopedIngestionCredentialRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var harnessSetupProfileId = NormalizeRequiredText(request.HarnessSetupProfileId, nameof(request.HarnessSetupProfileId));
        var credentialPrefix = string.IsNullOrWhiteSpace(request.CredentialPrefix) ? null : request.CredentialPrefix.Trim();
        var credentialHash = NormalizeCredentialHash(request.CredentialHash, credentialPrefix, nameof(request.CredentialHash));
        var allowedScopes = NormalizeCredentialScopes(request.AllowedScopes);
        var expiresAtUtc = request.ExpiresAtUtc.ToUniversalTime();
        var now = clock.UtcNow.ToUniversalTime();

        if (expiresAtUtc <= now)
        {
            throw new ArgumentException("Scoped ingestion credential expiry must be in the future.", nameof(request));
        }

        lock (gate)
        {
            RequireCustomerOrganization(customerOrganizationId);
            RequireProductUserForCustomerOrganization(customerOrganizationId, request.ProductUserId);
            RequireProductUserForCustomerOrganization(customerOrganizationId, request.CreatedByProductUserId);

            if (scopedIngestionCredentials.Values.Any(credential =>
                    credential.Status == ScopedIngestionCredentialStatus.Active &&
                    StringComparer.Ordinal.Equals(credential.CredentialHash, credentialHash)))
            {
                throw new InvalidOperationException("Active scoped ingestion credential hash already exists.");
            }

            var credential = new ScopedIngestionCredential(
                ScopedIngestionCredentialId.NewId(),
                customerOrganizationId,
                harnessSetupProfileId,
                request.ProductUserId,
                credentialHash,
                credentialPrefix,
                request.AllowedHarness,
                allowedScopes,
                ScopedIngestionCredentialStatus.Active,
                expiresAtUtc,
                LastUsedAtUtc: null,
                RotatedAtUtc: null,
                RevokedAtUtc: null,
                request.CreatedByProductUserId,
                request.CreatedByProductUserId,
                new ReadOnlyCollection<string>([NormalizeRequiredText(request.AuditEventId, nameof(request.AuditEventId))]),
                now,
                now);

            RecordScopedIngestionCredentialAuditEventUnderLock(
                credential,
                request.CreatedByProductUserId,
                request.ActorEffectiveRole,
                "scoped_ingestion_credential_create",
                "created",
                denialReason: null,
                request.CorrelationId,
                request.AuditEventId,
                now);
            scopedIngestionCredentials.Add(credential.ScopedIngestionCredentialId, credential);

            return Task.FromResult(credential);
        }
    }

    public Task<ScopedIngestionCredential> MarkScopedIngestionCredentialPendingRotationAsync(
        CustomerOrganizationId customerOrganizationId,
        ScopedIngestionCredentialId scopedIngestionCredentialId,
        ScopedIngestionCredentialLifecycleRequest request)
    {
        return UpdateScopedIngestionCredentialLifecycleAsync(
            customerOrganizationId,
            scopedIngestionCredentialId,
            request,
            ScopedIngestionCredentialStatus.PendingRotation,
            "scoped_ingestion_credential_pending_rotation");
    }

    public Task<ScopedIngestionCredential> DisableScopedIngestionCredentialAsync(
        CustomerOrganizationId customerOrganizationId,
        ScopedIngestionCredentialId scopedIngestionCredentialId,
        ScopedIngestionCredentialLifecycleRequest request)
    {
        return UpdateScopedIngestionCredentialLifecycleAsync(
            customerOrganizationId,
            scopedIngestionCredentialId,
            request,
            ScopedIngestionCredentialStatus.Disabled,
            "scoped_ingestion_credential_disable");
    }

    public Task<ScopedIngestionCredential> RevokeScopedIngestionCredentialAsync(
        CustomerOrganizationId customerOrganizationId,
        ScopedIngestionCredentialId scopedIngestionCredentialId,
        ScopedIngestionCredentialLifecycleRequest request)
    {
        return UpdateScopedIngestionCredentialLifecycleAsync(
            customerOrganizationId,
            scopedIngestionCredentialId,
            request,
            ScopedIngestionCredentialStatus.Revoked,
            "scoped_ingestion_credential_revoke",
            revokedAtUtc: clock.UtcNow.ToUniversalTime());
    }

    public Task<ScopedIngestionCredential> MarkScopedIngestionCredentialExpiredAsync(
        CustomerOrganizationId customerOrganizationId,
        ScopedIngestionCredentialId scopedIngestionCredentialId,
        ScopedIngestionCredentialLifecycleRequest request)
    {
        return UpdateScopedIngestionCredentialLifecycleAsync(
            customerOrganizationId,
            scopedIngestionCredentialId,
            request,
            ScopedIngestionCredentialStatus.Expired,
            "scoped_ingestion_credential_expire");
    }

    public Task<ScopedIngestionCredential> RotateScopedIngestionCredentialAsync(
        CustomerOrganizationId customerOrganizationId,
        ScopedIngestionCredentialId scopedIngestionCredentialId,
        RotateScopedIngestionCredentialRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var credentialPrefix = string.IsNullOrWhiteSpace(request.CredentialPrefix) ? null : request.CredentialPrefix.Trim();
        var credentialHash = NormalizeCredentialHash(request.CredentialHash, credentialPrefix, nameof(request.CredentialHash));
        var expiresAtUtc = request.ExpiresAtUtc.ToUniversalTime();
        var now = clock.UtcNow.ToUniversalTime();

        if (expiresAtUtc <= now)
        {
            throw new ArgumentException("Rotated scoped ingestion credential expiry must be in the future.", nameof(request));
        }

        lock (gate)
        {
            var credential = RequireScopedIngestionCredentialForCustomerOrganization(customerOrganizationId, scopedIngestionCredentialId);
            RequireProductUserForCustomerOrganization(customerOrganizationId, request.ChangedByProductUserId);

            if (credential.Status is not (ScopedIngestionCredentialStatus.Active or ScopedIngestionCredentialStatus.PendingRotation))
            {
                throw new InvalidOperationException("Only active or pending-rotation scoped ingestion credentials can be rotated.");
            }

            if (scopedIngestionCredentials.Values.Any(existingCredential =>
                    existingCredential.ScopedIngestionCredentialId != scopedIngestionCredentialId &&
                    existingCredential.Status == ScopedIngestionCredentialStatus.Active &&
                    StringComparer.Ordinal.Equals(existingCredential.CredentialHash, credentialHash)))
            {
                throw new InvalidOperationException("Active scoped ingestion credential hash already exists.");
            }

            var auditEventId = NormalizeRequiredText(request.AuditEventId, nameof(request.AuditEventId));
            var updatedCredential = credential with
            {
                CredentialHash = credentialHash,
                CredentialPrefix = credentialPrefix,
                Status = ScopedIngestionCredentialStatus.Active,
                ExpiresAtUtc = expiresAtUtc,
                RotatedAtUtc = now,
                ChangedByProductUserId = request.ChangedByProductUserId,
                AuditEventIds = AppendAuditEventId(credential.AuditEventIds, auditEventId),
                UpdatedAtUtc = now
            };

            RecordScopedIngestionCredentialAuditEventUnderLock(
                updatedCredential,
                request.ChangedByProductUserId,
                request.ActorEffectiveRole,
                "scoped_ingestion_credential_rotate",
                "updated",
                denialReason: null,
                request.CorrelationId,
                auditEventId,
                now);
            scopedIngestionCredentials[scopedIngestionCredentialId] = updatedCredential;

            return Task.FromResult(updatedCredential);
        }
    }

    public Task RecordScopedIngestionCredentialFailedAccessAsync(
        CustomerOrganizationId customerOrganizationId,
        ScopedIngestionCredentialId scopedIngestionCredentialId,
        string reasonCode,
        string correlationId)
    {
        var normalizedReasonCode = NormalizeRequiredText(reasonCode, nameof(reasonCode));
        var normalizedCorrelationId = NormalizeRequiredText(correlationId, nameof(correlationId));
        var now = clock.UtcNow.ToUniversalTime();

        lock (gate)
        {
            var credential = RequireScopedIngestionCredentialForCustomerOrganization(customerOrganizationId, scopedIngestionCredentialId);

            var auditEventId = $"ingestion-credential-denied-{Guid.NewGuid():N}";
            var evidenceMetadata = NormalizeEvidenceMetadata(new Dictionary<string, string>
            {
                ["evidence_kind"] = "authorization_decision",
                ["operation"] = "scoped_ingestion_credential_failed_access",
                ["result"] = normalizedReasonCode,
                ["scope_kind"] = ProductScopeKind.HarnessProfile.ToString(),
                ["scope_id"] = credential.HarnessSetupProfileId
            });

            CreateGovernanceAuditEventUnderLock(
                auditEventId,
                customerOrganizationId,
                actorProductUserId: null,
                effectiveRole: null,
                ProductAuthorizationAction.TelemetryIngest,
                "scoped_ingestion_credential",
                credential.ScopedIngestionCredentialId.ToString(),
                "denied",
                ProductAuthorizationDenialReason.ScopeMismatch,
                normalizedCorrelationId,
                evidenceMetadata,
                now);

            return Task.CompletedTask;
        }
    }

    public Task<GovernanceAuditEvent> RecordGovernanceAuditEventAsync(
        CustomerOrganizationId customerOrganizationId,
        CreateGovernanceAuditEventRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.TargetScope);

        var auditEventId = NormalizeRequiredText(request.AuditEventId, nameof(request.AuditEventId));
        var correlationId = NormalizeRequiredText(request.CorrelationId, nameof(request.CorrelationId));
        var decision = NormalizeDecision(request.Decision);
        var evidenceMetadata = NormalizeEvidenceMetadata(request.EvidenceMetadata);
        var targetResourceId = GetTargetResourceId(customerOrganizationId, request.TargetScope);
        var now = clock.UtcNow.ToUniversalTime();

        if (evidenceMetadata.Count == 0)
        {
            throw new ArgumentException("Governance audit events require evidence metadata.", nameof(request));
        }

        ValidateDenialShape(decision, request.DenialReason);

        lock (gate)
        {
            RequireCustomerOrganization(customerOrganizationId);

            if (request.ActorProductUserId is not null)
            {
                RequireProductUserForCustomerOrganization(customerOrganizationId, request.ActorProductUserId.Value);
            }

            return Task.FromResult(CreateGovernanceAuditEventUnderLock(
                auditEventId,
                customerOrganizationId,
                request.ActorProductUserId,
                request.EffectiveRole,
                request.Action,
                request.TargetScope.Kind.ToString(),
                targetResourceId,
                decision,
                request.DenialReason,
                correlationId,
                evidenceMetadata,
                now));
        }
    }

    public Task<ProductAuthorizationDecision> AuthorizeProductActionAsync(
        CustomerOrganizationId customerOrganizationId,
        IdentityTenantId identityTenantId,
        AuthenticatedTokenClaims claims,
        ProductAuthorizationAction action,
        ProductScope requestedScope,
        string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(requestedScope);

        lock (gate)
        {
            RequireCustomerOrganization(customerOrganizationId);
            RequireIdentityTenantForCustomerOrganization(customerOrganizationId, identityTenantId);

            try
            {
                RequireClaimsMatchIdentityTenant(identityTenantId, claims);
            }
            catch (InvalidOperationException)
            {
                RecordAuthorizationAuditEventUnderLock(
                    customerOrganizationId,
                    actorProductUserId: null,
                    effectiveRole: null,
                    action,
                    requestedScope,
                    ProductAuthorizationDenialReason.InvalidTenant,
                    correlationId);

                return Task.FromResult(new ProductAuthorizationDecision(
                    IsAllowed: false,
                    ProductAuthorizationDenialReason.InvalidTenant,
                    ProductUser: null,
                    EffectiveRoles: [],
                    MatchedMappings: []));
            }

            ProductUser productUser;
            try
            {
                productUser = ResolveProductUserFromAuthenticatedClaimsUnderLock(customerOrganizationId, identityTenantId, claims);
            }
            catch (InvalidOperationException)
            {
                RecordAuthorizationAuditEventUnderLock(
                    customerOrganizationId,
                    actorProductUserId: null,
                    effectiveRole: null,
                    action,
                    requestedScope,
                    ProductAuthorizationDenialReason.InvalidTenant,
                    correlationId);

                return Task.FromResult(new ProductAuthorizationDecision(
                    IsAllowed: false,
                    ProductAuthorizationDenialReason.InvalidTenant,
                    ProductUser: null,
                    EffectiveRoles: [],
                    MatchedMappings: []));
            }

            var now = clock.UtcNow.ToUniversalTime();
            var matchedMappings = productRoleMappings.Values
                .Where(mapping =>
                    mapping.CustomerOrganizationId == customerOrganizationId &&
                    mapping.IdentityTenantId == identityTenantId &&
                    mapping.Status == ProductRoleMappingStatus.Active &&
                    mapping.EffectiveFromUtc <= now &&
                    (mapping.EffectiveToUtc is null || mapping.EffectiveToUtc > now) &&
                    PrincipalMatches(mapping, claims))
                .ToArray();

            if (matchedMappings.Length == 0)
            {
                return Task.FromResult(Denied(
                    ProductAuthorizationDenialReason.MissingRoleMapping,
                    productUser,
                    action,
                    requestedScope));
            }

            var scopedMappings = matchedMappings
                .Where(mapping => ScopeMatches(mapping, requestedScope))
                .ToArray();

            if (scopedMappings.Length == 0)
            {
                return Task.FromResult(Denied(
                    ProductAuthorizationDenialReason.ScopeMismatch,
                    productUser,
                    action,
                    requestedScope,
                    matchedMappings));
            }

            if (!scopedMappings.Any(mapping => RoleAllowsAction(mapping.ProductRole, action)))
            {
                return Task.FromResult(Denied(
                    ProductAuthorizationDenialReason.InsufficientRole,
                    productUser,
                    action,
                    requestedScope,
                    scopedMappings));
            }

            return Task.FromResult(new ProductAuthorizationDecision(
                IsAllowed: true,
                ProductAuthorizationDenialReason.None,
                productUser,
                scopedMappings.Select(mapping => mapping.ProductRole).Distinct().ToArray(),
                scopedMappings));
        }

        ProductAuthorizationDecision Denied(
            ProductAuthorizationDenialReason denialReason,
            ProductUser productUser,
            ProductAuthorizationAction deniedAction,
            ProductScope deniedScope,
            IReadOnlyList<ProductRoleMapping>? matchedMappings = null)
        {
            var mappings = matchedMappings ?? [];
            var effectiveRole = mappings
                .Select(mapping => mapping.ProductRole)
                .Distinct()
                .FirstOrDefault();

            RecordAuthorizationAuditEventUnderLock(
                customerOrganizationId,
                productUser.ProductUserId,
                mappings.Count == 0 ? null : effectiveRole,
                deniedAction,
                deniedScope,
                denialReason,
                correlationId);

            return new ProductAuthorizationDecision(
                IsAllowed: false,
                denialReason,
                productUser,
                mappings.Select(mapping => mapping.ProductRole).Distinct().ToArray(),
                mappings.ToArray());
        }
    }

    public Task RecordAuthorizationDenialAsync(
        CustomerOrganizationId customerOrganizationId,
        ProductAuthorizationAction action,
        ProductScope requestedScope,
        ProductAuthorizationDenialReason denialReason,
        string correlationId)
    {
        ArgumentNullException.ThrowIfNull(requestedScope);

        lock (gate)
        {
            RequireCustomerOrganization(customerOrganizationId);
            RecordAuthorizationAuditEventUnderLock(
                customerOrganizationId,
                actorProductUserId: null,
                effectiveRole: null,
                action,
                requestedScope,
                denialReason,
                correlationId);

            return Task.CompletedTask;
        }
    }

    private Task<ScopedIngestionCredential> UpdateScopedIngestionCredentialLifecycleAsync(
        CustomerOrganizationId customerOrganizationId,
        ScopedIngestionCredentialId scopedIngestionCredentialId,
        ScopedIngestionCredentialLifecycleRequest request,
        ScopedIngestionCredentialStatus status,
        string operation,
        DateTimeOffset? revokedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var auditEventId = NormalizeRequiredText(request.AuditEventId, nameof(request.AuditEventId));
        var now = clock.UtcNow.ToUniversalTime();

        lock (gate)
        {
            var credential = RequireScopedIngestionCredentialForCustomerOrganization(customerOrganizationId, scopedIngestionCredentialId);
            RequireProductUserForCustomerOrganization(customerOrganizationId, request.ChangedByProductUserId);

            var updatedCredential = credential with
            {
                Status = status,
                RevokedAtUtc = revokedAtUtc ?? credential.RevokedAtUtc,
                ChangedByProductUserId = request.ChangedByProductUserId,
                AuditEventIds = AppendAuditEventId(credential.AuditEventIds, auditEventId),
                UpdatedAtUtc = now
            };

            RecordScopedIngestionCredentialAuditEventUnderLock(
                updatedCredential,
                request.ChangedByProductUserId,
                request.ActorEffectiveRole,
                operation,
                status == ScopedIngestionCredentialStatus.Disabled ? "disabled" : "updated",
                denialReason: null,
                request.CorrelationId,
                auditEventId,
                now);
            scopedIngestionCredentials[scopedIngestionCredentialId] = updatedCredential;

            return Task.FromResult(updatedCredential);
        }
    }

    private ScopedIngestionCredential RequireScopedIngestionCredentialForCustomerOrganization(
        CustomerOrganizationId customerOrganizationId,
        ScopedIngestionCredentialId scopedIngestionCredentialId)
    {
        if (scopedIngestionCredentialId == ScopedIngestionCredentialId.Empty ||
            !scopedIngestionCredentials.TryGetValue(scopedIngestionCredentialId, out var credential) ||
            credential.CustomerOrganizationId != customerOrganizationId)
        {
            throw new InvalidOperationException("Scoped ingestion credential does not belong to the customer organization.");
        }

        return credential;
    }

    private static IReadOnlyList<ProductScope> NormalizeCredentialScopes(IReadOnlyList<ProductScope> allowedScopes)
    {
        ArgumentNullException.ThrowIfNull(allowedScopes);

        if (allowedScopes.Count == 0)
        {
            throw new ArgumentException("At least one scoped ingestion credential scope is required.", nameof(allowedScopes));
        }

        return new ReadOnlyCollection<ProductScope>(
            allowedScopes
                .Select(NormalizeCredentialScope)
                .ToArray());
    }

    private static ProductScope NormalizeCredentialScope(ProductScope scope)
    {
        return scope.Kind switch
        {
            ProductScopeKind.Organization => string.IsNullOrWhiteSpace(scope.ScopeId)
                ? new ProductScope(ProductScopeKind.Organization, ScopeId: null)
                : throw new ArgumentException("Organization scoped ingestion credentials must not include a scope identifier.", nameof(scope)),
            ProductScopeKind.Team or ProductScopeKind.Repository or ProductScopeKind.HarnessProfile => NormalizeResourceCredentialScope(scope),
            _ => throw new ArgumentException("Scoped ingestion credential scope kind is not supported.", nameof(scope))
        };
    }

    private static ProductScope NormalizeResourceCredentialScope(ProductScope scope)
    {
        var scopeId = NormalizeRequiredText(scope.ScopeId ?? string.Empty, nameof(scope));

        if (!IsSafeResourceId(scopeId))
        {
            throw new ArgumentException("Scoped ingestion credential scope identifier is not supported.", nameof(scope));
        }

        return new ProductScope(scope.Kind, scopeId);
    }

    private static string NormalizeCredentialHash(
        string credentialHash,
        string? credentialPrefix,
        string parameterName)
    {
        var normalizedCredentialHash = NormalizeRequiredText(credentialHash, parameterName);

        if (!normalizedCredentialHash.StartsWith("sha256:", StringComparison.Ordinal) ||
            normalizedCredentialHash.Length <= "sha256:".Length)
        {
            throw new ArgumentException("Scoped ingestion credential verifier must use the sha256: prefix.", parameterName);
        }

        if (!IsSafeMachineToken(normalizedCredentialHash) ||
            normalizedCredentialHash.StartsWith("aito_", StringComparison.OrdinalIgnoreCase) ||
            normalizedCredentialHash.Contains("bearer", StringComparison.OrdinalIgnoreCase) ||
            SensitiveEvidenceValueFragments.Any(fragment =>
                normalizedCredentialHash.Contains(fragment, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(credentialPrefix) &&
                StringComparer.Ordinal.Equals(normalizedCredentialHash, credentialPrefix.Trim())))
        {
            throw new ArgumentException("Scoped ingestion credential verifier must not contain credential secret material.", parameterName);
        }

        return normalizedCredentialHash;
    }

    private static IReadOnlyList<string> AppendAuditEventId(
        IReadOnlyList<string> existingAuditEventIds,
        string auditEventId)
    {
        return new ReadOnlyCollection<string>(
            existingAuditEventIds
                .Concat([NormalizeRequiredText(auditEventId, nameof(auditEventId))])
                .ToArray());
    }

    private void RecordScopedIngestionCredentialAuditEventUnderLock(
        ScopedIngestionCredential credential,
        ProductUserId actorProductUserId,
        ProductRole actorEffectiveRole,
        string operation,
        string decision,
        ProductAuthorizationDenialReason? denialReason,
        string correlationId,
        string auditEventId,
        DateTimeOffset now)
    {
        var evidenceMetadata = NormalizeEvidenceMetadata(new Dictionary<string, string>
        {
            ["evidence_kind"] = "admin_operation",
            ["operation"] = operation,
            ["scope_kind"] = ProductScopeKind.HarnessProfile.ToString(),
            ["scope_id"] = credential.HarnessSetupProfileId
        });

        CreateGovernanceAuditEventUnderLock(
            NormalizeRequiredText(auditEventId, nameof(auditEventId)),
            credential.CustomerOrganizationId,
            actorProductUserId,
            actorEffectiveRole,
            ProductAuthorizationAction.IngestionCredentialManage,
            "scoped_ingestion_credential",
            credential.ScopedIngestionCredentialId.ToString(),
            decision,
            denialReason,
            NormalizeRequiredText(correlationId, nameof(correlationId)),
            evidenceMetadata,
            now);
    }

    private static string NormalizeRequiredText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        return value.Trim();
    }

    private static string NormalizeHotspotEvidenceSummary(string value)
    {
        var normalized = NormalizeRequiredText(value, nameof(value));
        if (normalized.Length > 512 ||
            ContainsSensitiveFragment(normalized) ||
            UnsupportedHotspotSummaryFragments.Any(fragment => normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Token hotspot evidence summary must be bounded, non-sensitive, and non-punitive.", nameof(value));
        }

        return normalized;
    }

    private static IReadOnlyList<string> NormalizeHotspotEvidenceReferences(IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count == 0 || values.Count > 32)
        {
            throw new ArgumentException("Token hotspots require between 1 and 32 evidence references.", nameof(values));
        }

        var normalized = values
            .Select(value => NormalizeRequiredText(value, nameof(values)))
            .Select(value =>
            {
                if (value.Length > 128 ||
                    !IsSafeResourceId(value) ||
                    ContainsSensitiveFragment(value))
                {
                    throw new ArgumentException("Token hotspot evidence references must be safe identifiers.", nameof(values));
                }

                return value;
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalized.Length != values.Count)
        {
            throw new ArgumentException("Token hotspot evidence references must be unique.", nameof(values));
        }

        return normalized;
    }

    private static string? NormalizeOptionalHotspotDetectionKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= 128 &&
            IsSafeResourceId(normalized) &&
            !ContainsSensitiveFragment(normalized)
            ? normalized
            : throw new ArgumentException("Token hotspot detection key must be a bounded safe identifier.", nameof(value));
    }

    private static void ValidateTokenHotspotAuthority(CreateTokenHotspotRecordRequest request)
    {
        if (request.HotspotType != TokenHotspotType.PromptCacheBreakage &&
            request.PromptCacheEvidenceState != PromptCacheEvidenceState.NotApplicable)
        {
            throw new ArgumentException("Only prompt cache breakage hotspots may carry prompt cache evidence states.", nameof(request));
        }

        if (request.HotspotType == TokenHotspotType.PromptCacheBreakage &&
            request.PromptCacheEvidenceState == PromptCacheEvidenceState.NotApplicable)
        {
            throw new ArgumentException("Prompt cache breakage hotspots require a cache evidence state.", nameof(request));
        }

        if (request.FindingState == TokenHotspotFindingState.Confirmed &&
            request.AttributionType == TokenHotspotAttributionType.LlmInferred)
        {
            throw new ArgumentException("Confirmed token hotspots must not rely on LLM-inferred attribution.", nameof(request));
        }

        if (request.FindingState == TokenHotspotFindingState.Confirmed &&
            request.MetricConfidence is TokenMetricConfidence.LlmInferred or TokenMetricConfidence.Unavailable)
        {
            throw new ArgumentException("Confirmed token hotspots require non-LLM metric confidence.", nameof(request));
        }

        if (request.FindingState == TokenHotspotFindingState.CandidateLlmInferred &&
            request.AttributionType != TokenHotspotAttributionType.LlmInferred)
        {
            throw new ArgumentException("LLM-inferred candidate hotspots must use LLM-inferred attribution.", nameof(request));
        }

        if (request.AttributionType == TokenHotspotAttributionType.LlmInferred &&
            request.FindingState != TokenHotspotFindingState.CandidateLlmInferred)
        {
            throw new ArgumentException("LLM-inferred attribution is allowed only for candidate hotspots.", nameof(request));
        }
    }

    private void UpsertAgentSessionUnderLock(
        TelemetryEnvelopeRecord envelope,
        string repositoryEvidenceState)
    {
        var lookupKey = new AgentSessionLookupKey(
            envelope.CustomerOrganizationId,
            envelope.HarnessSetupProfileId,
            envelope.ProductUserId,
            GetProviderSessionKey(envelope));

        if (!agentSessionLookupIndex.TryGetValue(lookupKey, out var existingSessionId))
        {
            var sessionId = $"agent-session-{Guid.NewGuid():N}";
            var eventTime = envelope.SourceEventTimestampUtc ?? envelope.ReceivedAtUtc;
            var session = new AgentSessionRecord(
                sessionId,
                envelope.CustomerOrganizationId,
                envelope.ProductUserId,
                envelope.HarnessSetupProfileId,
                envelope.Harness,
                envelope.ConversationIdHash,
                eventTime,
                eventTime,
                "active",
                Environment: null,
                envelope.SandboxSetting,
                envelope.ApprovalSetting,
                repositoryEvidenceState,
                envelope.ContentCaptureState,
                "not_started",
                envelope.MetricStatus,
                envelope.MetricConfidence,
                envelope.ModelName is null ? [] : [envelope.ModelName],
                [envelope.TelemetryEnvelopeId],
                envelope.ReceivedAtUtc,
                envelope.ReceivedAtUtc);

            agentSessions.Add(sessionId, session);
            agentSessionLookupIndex.Add(lookupKey, sessionId);
            return;
        }

        var existing = agentSessions[existingSessionId];
        var eventTimestamp = envelope.SourceEventTimestampUtc ?? envelope.ReceivedAtUtc;
        var startedAtUtc = existing.StartedAtUtc is null || eventTimestamp < existing.StartedAtUtc
            ? eventTimestamp
            : existing.StartedAtUtc;
        var endedAtUtc = existing.EndedAtUtc is null || eventTimestamp > existing.EndedAtUtc
            ? eventTimestamp
            : existing.EndedAtUtc;
        var modelNames = envelope.ModelName is null || existing.ModelNames.Contains(envelope.ModelName, StringComparer.Ordinal)
            ? existing.ModelNames
            : existing.ModelNames.Concat([envelope.ModelName]).ToArray();
        var sourceTelemetryEnvelopeIds = existing.SourceTelemetryEnvelopeIds.Contains(envelope.TelemetryEnvelopeId, StringComparer.Ordinal)
            ? existing.SourceTelemetryEnvelopeIds
            : existing.SourceTelemetryEnvelopeIds.Concat([envelope.TelemetryEnvelopeId]).ToArray();

        agentSessions[existingSessionId] = existing with
        {
            StartedAtUtc = startedAtUtc,
            EndedAtUtc = endedAtUtc,
            SandboxSetting = envelope.SandboxSetting ?? existing.SandboxSetting,
            ApprovalSetting = envelope.ApprovalSetting ?? existing.ApprovalSetting,
            RepositoryEvidenceState = MergeEvidenceState(existing.RepositoryEvidenceState, repositoryEvidenceState),
            ContentCaptureSummary = MergeContentCaptureSummary(existing.ContentCaptureSummary, envelope.ContentCaptureState),
            TokenMetricStatus = MergeMetricStatus(existing.TokenMetricStatus, envelope.MetricStatus),
            TokenMetricConfidence = MergeMetricConfidence(existing.TokenMetricConfidence, envelope.MetricConfidence),
            ModelNames = modelNames,
            SourceTelemetryEnvelopeIds = sourceTelemetryEnvelopeIds,
            UpdatedAtUtc = envelope.ReceivedAtUtc
        };
    }

    private static string GetProviderSessionKey(TelemetryEnvelopeRecord envelope)
    {
        return envelope.ConversationIdHash ?? "partial-session";
    }

    private static DateTimeOffset? MinDateTimeOffset(DateTimeOffset? first, DateTimeOffset? second)
    {
        if (first is null)
        {
            return second;
        }

        if (second is null)
        {
            return first;
        }

        return first <= second ? first : second;
    }

    private static DateTimeOffset? MaxDateTimeOffset(DateTimeOffset? first, DateTimeOffset? second)
    {
        if (first is null)
        {
            return second;
        }

        if (second is null)
        {
            return first;
        }

        return first >= second ? first : second;
    }

    private static string ToWireHarness(CodingAgentHarness harness)
    {
        return harness switch
        {
            CodingAgentHarness.CodexCli => "codex-cli",
            _ => throw new ArgumentOutOfRangeException(nameof(harness), harness, null)
        };
    }

    private static string ToWireSessionStatus(AgentSessionStatus status)
    {
        return status switch
        {
            AgentSessionStatus.Active => "active",
            AgentSessionStatus.Completed => "completed",
            AgentSessionStatus.Failed => "failed",
            AgentSessionStatus.Partial => "partial",
            AgentSessionStatus.Expired => "expired",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
    }

    private static string ToWireRepositoryEvidenceState(RepositoryEvidenceState state)
    {
        return state switch
        {
            RepositoryEvidenceState.Observed => "observed",
            RepositoryEvidenceState.Correlated => "correlated",
            RepositoryEvidenceState.Inferred => "inferred",
            RepositoryEvidenceState.Unavailable => "unavailable",
            RepositoryEvidenceState.Mixed => "mixed",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };
    }

    private static string ToWireContentCaptureSummary(ContentCaptureSummary state)
    {
        return state switch
        {
            ContentCaptureSummary.None => "none",
            ContentCaptureSummary.MetadataOnly => "metadata_only",
            ContentCaptureSummary.Captured => "captured",
            ContentCaptureSummary.ReviewRequired => "review_required",
            ContentCaptureSummary.RedactionFailed => "redaction_failed",
            ContentCaptureSummary.Mixed => "mixed",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };
    }

    private static string ToWireRecommendationStatus(RecommendationStatus status)
    {
        return status switch
        {
            RecommendationStatus.NotStarted => "not_started",
            RecommendationStatus.Queued => "queued",
            RecommendationStatus.Generated => "generated",
            RecommendationStatus.Failed => "failed",
            RecommendationStatus.Disabled => "disabled",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
    }

    private static string MergeEvidenceState(string existing, string incoming)
    {
        return StringComparer.Ordinal.Equals(existing, incoming) ? existing : "mixed";
    }

    private static string MergeContentCaptureSummary(string existing, string incoming)
    {
        return StringComparer.Ordinal.Equals(existing, incoming) ? existing : "mixed";
    }

    private static TokenMetricStatus MergeMetricStatus(TokenMetricStatus existing, TokenMetricStatus incoming)
    {
        return existing == incoming ? existing : TokenMetricStatus.Mixed;
    }

    private static TokenMetricConfidence MergeMetricConfidence(TokenMetricConfidence existing, TokenMetricConfidence incoming)
    {
        return existing == incoming ? existing : TokenMetricConfidence.Estimated;
    }

    private static string NormalizeWireHarness(string value, string parameterName)
    {
        var normalized = NormalizeRequiredText(value, parameterName);
        return normalized is "codex-cli"
            ? normalized
            : throw new ArgumentException("Harness is not supported.", parameterName);
    }

    private static string NormalizeSignalType(string value, string parameterName)
    {
        var normalized = NormalizeRequiredText(value, parameterName);
        return normalized is "log" or "trace" or "metric"
            ? normalized
            : throw new ArgumentException("Telemetry signal type is not supported.", parameterName);
    }

    private static string NormalizeDateVersion(string value, string parameterName)
    {
        var normalized = NormalizeRequiredText(value, parameterName);
        return normalized.Length == 10 &&
            normalized[4] == '-' &&
            normalized[7] == '-' &&
            DateOnly.TryParseExact(normalized, "yyyy-MM-dd", out _)
            ? normalized
            : throw new ArgumentException("Schema version must use YYYY-MM-DD format.", parameterName);
    }

    private static string NormalizeSourceEventName(string value, string parameterName)
    {
        var normalized = NormalizeRequiredText(value, parameterName);
        return normalized.Length <= 128 && IsSafeMachineToken(normalized)
            ? normalized
            : throw new ArgumentException("Source event name is not allowed.", parameterName);
    }

    private static string? NormalizeOptionalMachineToken(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= 128 && IsSafeMachineToken(normalized)
            ? normalized
            : throw new ArgumentException("Optional machine token is not allowed.", parameterName);
    }

    private static string? NormalizeOptionalSafeLabel(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= 128 &&
            IsSafeResourceId(normalized) &&
            !ContainsSensitiveFragment(normalized)
            ? normalized
            : throw new ArgumentException("Optional metadata label is not allowed.", parameterName);
    }

    private static string? NormalizeOptionalModelName(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= 128 &&
            normalized.All(static character =>
                char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_' or '.' or ':' or '/')
            ? normalized
            : throw new ArgumentException("Model name is not allowed.", parameterName);
    }

    private static IReadOnlyList<ContentRedactionFinding> NormalizeRedactionFindings(
        IReadOnlyList<ContentRedactionFinding>? findings)
    {
        if (findings is null || findings.Count == 0)
        {
            return [];
        }

        return findings
            .Select(finding =>
            {
                if (finding.ConfidenceScore is < 0 or > 1)
                {
                    throw new ArgumentException("Redaction finding confidence is out of range.", nameof(findings));
                }

                return new ContentRedactionFinding(
                    NormalizeRequiredSafeMetadataLabel(finding.Stage, nameof(findings)),
                    NormalizeRequiredSafeMetadataLabel(finding.Kind, nameof(findings)),
                    NormalizeRequiredSafeMetadataLabel(finding.Category, nameof(findings)),
                    finding.ConfidenceScore,
                    NormalizeOptionalSafeLabel(finding.ApiVersion, nameof(findings)),
                    NormalizeOptionalSafeLabel(finding.ModelVersion, nameof(findings)));
            })
            .ToArray();
    }

    private static string NormalizeRequiredSafeMetadataLabel(string value, string parameterName)
    {
        var normalized = NormalizeRequiredText(value, parameterName);
        return normalized.Length <= 128 &&
            IsSafeResourceId(normalized)
            ? normalized
            : throw new ArgumentException("Required metadata label is not allowed.", parameterName);
    }

    private static string NormalizeEvidenceState(string value, string parameterName)
    {
        var normalized = NormalizeRequiredText(value, parameterName);
        return normalized is "observed" or "derived" or "estimated" or "unavailable" or "not_applicable" or "mixed" or "correlated" or "inferred"
            ? normalized
            : throw new ArgumentException("Evidence state is not supported.", parameterName);
    }

    private static string NormalizeMetricState(string value, string parameterName)
    {
        var normalized = NormalizeRequiredText(value, parameterName);
        return normalized is "observed" or "derived" or "estimated" or "unavailable" or "not_applicable" or "mixed"
            ? normalized
            : throw new ArgumentException("Metric state is not supported.", parameterName);
    }

    private static void ValidateMetricQuality(
        TokenMetricStatus metricStatus,
        TokenMetricConfidence metricConfidence)
    {
        if (metricStatus is TokenMetricStatus.Unavailable or TokenMetricStatus.NotApplicable &&
            metricConfidence != TokenMetricConfidence.Unavailable)
        {
            throw new ArgumentException("Unavailable or not applicable metrics require unavailable confidence.", nameof(metricConfidence));
        }
    }

    private static void ValidateTokenObservationShape(
        long? value,
        TokenMetricStatus metricStatus,
        TokenMetricConfidence metricConfidence,
        TokenObservationSourceKind sourceKind)
    {
        ValidateMetricQuality(metricStatus, metricConfidence);

        if (value < 0)
        {
            throw new ArgumentException("Token metric values must not be negative.", nameof(value));
        }

        if (metricStatus is TokenMetricStatus.Unavailable or TokenMetricStatus.NotApplicable && value is not null)
        {
            throw new ArgumentException("Unavailable and not applicable token metrics must use null values.", nameof(value));
        }

        if (metricStatus is TokenMetricStatus.Observed or TokenMetricStatus.Derived or TokenMetricStatus.Estimated or TokenMetricStatus.Mixed &&
            value is null)
        {
            throw new ArgumentException("Available token metric states require a value.", nameof(value));
        }

        if (value == 0 &&
            metricStatus is not (TokenMetricStatus.Observed or TokenMetricStatus.Derived))
        {
            throw new ArgumentException("Zero token values require observed or deterministically derived evidence.", nameof(value));
        }

        if (value == 0 &&
            metricConfidence is not (TokenMetricConfidence.Observed or TokenMetricConfidence.Deterministic))
        {
            throw new ArgumentException("Zero token values require observed or deterministic confidence.", nameof(metricConfidence));
        }

        if (sourceKind == TokenObservationSourceKind.Missing &&
            metricStatus is not (TokenMetricStatus.Unavailable or TokenMetricStatus.NotApplicable))
        {
            throw new ArgumentException("Missing token observation sources require unavailable or not applicable status.", nameof(sourceKind));
        }
    }

    private static AggregateTokenTimelineBucket NormalizeAggregateTokenTimelineBucket(
        CustomerOrganizationId customerOrganizationId,
        AggregateTokenTimelineBucket bucket)
    {
        ArgumentNullException.ThrowIfNull(bucket);
        ValidateAggregateTokenTimelineQuery(new AggregateTokenTimelineQuery(
            bucket.BucketDateUtc,
            bucket.BucketDateUtc,
            bucket.MovingAverageWindowDays));

        if (bucket.CustomerOrganizationId != customerOrganizationId)
        {
            throw new ArgumentException("Aggregate token timeline bucket tenant does not match the request.", nameof(bucket));
        }

        var slug = NormalizeRequiredText(bucket.CustomerOrganizationSlug, nameof(bucket.CustomerOrganizationSlug)).ToLowerInvariant();
        var environment = NormalizeAggregateEnvironment(bucket.Environment, nameof(bucket.Environment));
        var region = NormalizeRequiredText(bucket.Region, nameof(bucket.Region)).ToLowerInvariant();
        var period = NormalizeRequiredText(bucket.Period, nameof(bucket.Period)).ToLowerInvariant();

        if (period != "day")
        {
            throw new ArgumentException("Aggregate token timeline bucket period is not supported.", nameof(bucket));
        }

        if (bucket.TokenBurn is < 0)
        {
            throw new ArgumentException("Aggregate token timeline bucket token burn must be non-negative.", nameof(bucket));
        }

        if (bucket.MovingAverageTokenBurn.HasValue &&
            (double.IsNaN(bucket.MovingAverageTokenBurn.Value) ||
                double.IsInfinity(bucket.MovingAverageTokenBurn.Value) ||
                bucket.MovingAverageTokenBurn.Value < 0))
        {
            throw new ArgumentException("Aggregate token timeline bucket moving average must be finite and non-negative.", nameof(bucket));
        }

        if (ContainsSensitiveFragment(slug) ||
            ContainsSensitiveFragment(environment) ||
            ContainsSensitiveFragment(region) ||
            !IsSafeResourceId(slug) ||
            !IsSafeResourceId(environment) ||
            !IsSafeResourceId(region))
        {
            throw new ArgumentException("Aggregate token timeline bucket labels are not allowed.", nameof(bucket));
        }

        return bucket with
        {
            CustomerOrganizationSlug = slug,
            Environment = environment,
            Region = region,
            Period = period
        };
    }

    private static void ValidateAggregateTokenTimelineQuery(AggregateTokenTimelineQuery query)
    {
        if (query.EndDateUtc < query.StartDateUtc)
        {
            throw new ArgumentException("Aggregate token timeline end date must be on or after start date.", nameof(query));
        }

        if (query.MovingAverageWindowDays is < 1 or > 90)
        {
            throw new ArgumentException("Moving average window must be between 1 and 90 days.", nameof(query));
        }
    }

    private static string NormalizeAggregateEnvironment(string value, string parameterName)
    {
        var normalized = NormalizeRequiredText(value, parameterName).ToLowerInvariant();

        return normalized is "dv" or "qa" or "pp" or "pd"
            ? normalized
            : throw new ArgumentException("Aggregate environment is not supported.", parameterName);
    }

    private static string NormalizeAggregateMetricName(string value, string parameterName)
    {
        var normalized = NormalizeRequiredText(value, parameterName);

        return normalized is "tokenobs_tokens_total" or
                "tokenobs_sessions_started_total" or
                "tokenobs_token_metric_states_total" or
                "tokenobs_model_invocations_total" or
                "tokenobs_turns_total" or
                "tokenobs_hotspots_open" or
                "tokenobs_ingestion_requests_total"
            ? normalized
            : throw new ArgumentException("Aggregate metric name is not supported.", parameterName);
    }

    private static string NormalizeAggregateMetricUnit(string value, string parameterName)
    {
        var normalized = NormalizeRequiredText(value, parameterName);

        return normalized is "tokens" or "sessions" or "observations" or "invocations" or "turns" or "hotspots" or "requests"
            ? normalized
            : throw new ArgumentException("Aggregate metric unit is not supported.", parameterName);
    }

    private static (
        string AgentSessionId,
        string Name,
        string Unit,
        IReadOnlyDictionary<string, string> Labels) NormalizeAggregateMetricPointRequest(
            CreateAggregateMetricPointRecordRequest request)
    {
        var agentSessionId = NormalizeRequiredText(request.AgentSessionId, nameof(request.AgentSessionId));
        var name = NormalizeAggregateMetricName(request.Name, nameof(request.Name));
        var unit = NormalizeAggregateMetricUnit(request.Unit, nameof(request.Unit));
        var labels = NormalizeAggregateMetricLabels(name, request.Labels);

        if (double.IsNaN(request.Value) || double.IsInfinity(request.Value) || request.Value < 0)
        {
            throw new ArgumentException("Aggregate metric values must be finite and non-negative.", nameof(request));
        }

        return (agentSessionId, name, unit, labels);
    }

    private PricingBasisRecord ReviewPricingBasisUnderLock(
        PricingBasisReviewRequest request,
        ProductUserId actorProductUserId,
        ProductRole actorEffectiveRole,
        PricingReviewState reviewState,
        string auditResult)
    {
        var pricingBasisId = NormalizeRequiredText(request.PricingBasisId, nameof(request.PricingBasisId));
        var auditEventId = NormalizeRequiredText(request.AuditEventId, nameof(request.AuditEventId));
        var correlationId = NormalizeRequiredText(request.CorrelationId, nameof(request.CorrelationId));

        RequireCustomerOrganization(request.CustomerOrganizationId);
        RequireProductUserForCustomerOrganization(request.CustomerOrganizationId, actorProductUserId);

        if (!pricingBasisRecords.TryGetValue(pricingBasisId, out var existing) ||
            existing.CustomerOrganizationId != request.CustomerOrganizationId)
        {
            throw new InvalidOperationException("Pricing basis does not belong to the customer organization.");
        }

        if (existing.ReviewState != PricingReviewState.Candidate &&
            (reviewState != PricingReviewState.Superseded || existing.ReviewState != PricingReviewState.Approved))
        {
            throw new InvalidOperationException("Only candidate pricing basis records, or approved records being superseded, can be reviewed.");
        }

        var now = clock.UtcNow.ToUniversalTime();
        CreateGovernanceAuditEventUnderLock(
            auditEventId,
            request.CustomerOrganizationId,
            actorProductUserId,
            actorEffectiveRole,
            ProductAuthorizationAction.PricingManage,
            ProductScopeKind.Pricing.ToString(),
            pricingBasisId,
            "updated",
            denialReason: null,
            correlationId,
            CreatePricingAuditMetadata(
                operation: $"pricing_basis_{auditResult}",
                result: auditResult,
                pricingBasisId,
                existing,
                reviewState),
            now);

        if (reviewState == PricingReviewState.Approved)
        {
            foreach (var approved in pricingBasisRecords.Values
                .Where(record =>
                    record.CustomerOrganizationId == existing.CustomerOrganizationId &&
                    record.ReviewState == PricingReviewState.Approved &&
                    StringComparer.Ordinal.Equals(record.Harness, existing.Harness) &&
                    StringComparer.Ordinal.Equals(record.ProviderName, existing.ProviderName) &&
                    StringComparer.Ordinal.Equals(record.ModelName, existing.ModelName) &&
                    StringComparer.Ordinal.Equals(record.BillingRoute, existing.BillingRoute) &&
                    record.EffectiveFromUtc <= existing.EffectiveFromUtc &&
                    (record.EffectiveToUtc is null || record.EffectiveToUtc > existing.EffectiveFromUtc) &&
                    record.TokenType == existing.TokenType &&
                    record.SourceKind == existing.SourceKind)
                .ToArray())
            {
                pricingBasisRecords[approved.PricingBasisId] = approved with
                {
                    ReviewState = PricingReviewState.Superseded,
                    EffectiveToUtc = ClosePricingEffectiveWindow(approved, existing.EffectiveFromUtc),
                    UpdatedAtUtc = now
                };
            }
        }

        var effectiveToUtc = reviewState == PricingReviewState.Superseded &&
            existing.ReviewState == PricingReviewState.Approved
            ? ClosePricingEffectiveWindow(existing, now)
            : existing.EffectiveToUtc;

        var updated = existing with
        {
            ReviewState = reviewState,
            EffectiveToUtc = effectiveToUtc,
            AuditEventId = auditEventId,
            UpdatedAtUtc = now
        };

        pricingBasisRecords[pricingBasisId] = updated;
        return updated;
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

    private Task<PricingBasisRecord> ReviewPricingBasisAsync(
        PricingBasisReviewRequest request,
        ProductUserId actorProductUserId,
        ProductRole actorEffectiveRole,
        PricingReviewState reviewState,
        string auditResult)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (gate)
        {
            return Task.FromResult(ReviewPricingBasisUnderLock(
                request,
                actorProductUserId,
                actorEffectiveRole,
                reviewState,
                auditResult));
        }
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

    private static NormalizedCostEstimateRequest NormalizeCostEstimateRequest(
        CreateCostEstimateRecordRequest request)
    {
        var agentSessionId = NormalizeRequiredText(request.AgentSessionId, nameof(request.AgentSessionId));
        var modelInvocationId = NormalizeOptionalMachineToken(request.ModelInvocationId, nameof(request.ModelInvocationId));
        var pricingBasisId = string.IsNullOrWhiteSpace(request.PricingBasisId)
            ? null
            : NormalizeRequiredText(request.PricingBasisId, nameof(request.PricingBasisId));
        var pricingVersion = string.IsNullOrWhiteSpace(request.PricingVersion)
            ? null
            : NormalizePricingVersion(request.PricingVersion, nameof(request.PricingVersion));
        var currency = NormalizeCurrency(request.Currency, nameof(request.Currency));
        var providerName = NormalizePricingLabel(request.ProviderName, nameof(request.ProviderName));
        var modelName = NormalizePricingLabel(request.ModelName, nameof(request.ModelName));
        var billingRoute = NormalizePricingLabel(request.BillingRoute, nameof(request.BillingRoute));

        ValidateMetricQuality(request.TokenMetricStatus, request.TokenMetricConfidence);

        if (request.EstimatedCost < 0)
        {
            throw new ArgumentException("Estimated cost must not be negative.", nameof(request.EstimatedCost));
        }

        if (request.CostStatus is CostEstimateStatus.Unavailable or CostEstimateStatus.NotApplicable &&
            (request.EstimatedCost is not null || pricingBasisId is not null || pricingVersion is not null))
        {
            throw new ArgumentException("Unavailable and not applicable cost estimates must not include cost or pricing basis.", nameof(request));
        }

        if (request.CostStatus is CostEstimateStatus.Estimated or CostEstimateStatus.Mixed &&
            (request.EstimatedCost is null || pricingBasisId is null || pricingVersion is null))
        {
            throw new ArgumentException("Available cost estimates require cost and pricing basis.", nameof(request));
        }

        return new NormalizedCostEstimateRequest(
            agentSessionId,
            modelInvocationId,
            pricingBasisId,
            pricingVersion,
            currency,
            providerName,
            modelName,
            billingRoute);
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

    private static IReadOnlyDictionary<string, string> CreatePricingAuditMetadata(
        string operation,
        string result,
        string pricingBasisId,
        NormalizedPricingBasisRequest request,
        PricingTokenType tokenType,
        PricingSourceKind sourceKind,
        PricingReviewState reviewState)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["evidence_kind"] = "admin_operation",
            ["operation"] = operation,
            ["result"] = result,
            ["pricing_basis_id"] = pricingBasisId,
            ["pricing_version"] = request.PricingVersion,
            ["provider_name"] = request.ProviderName,
            ["model_name"] = request.ModelName,
            ["token_type"] = ToWirePricingTokenType(tokenType),
            ["billing_route"] = request.BillingRoute,
            ["source_kind"] = ToWirePricingSourceKind(sourceKind),
            ["review_state"] = ToWirePricingReviewState(reviewState)
        };
    }

    private static IReadOnlyDictionary<string, string> CreatePricingAuditMetadata(
        string operation,
        string result,
        string pricingBasisId,
        PricingBasisRecord record,
        PricingReviewState reviewState)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["evidence_kind"] = "admin_operation",
            ["operation"] = operation,
            ["result"] = result,
            ["pricing_basis_id"] = pricingBasisId,
            ["pricing_version"] = record.PricingVersion,
            ["provider_name"] = record.ProviderName,
            ["model_name"] = record.ModelName,
            ["token_type"] = ToWirePricingTokenType(record.TokenType),
            ["billing_route"] = record.BillingRoute,
            ["source_kind"] = ToWirePricingSourceKind(record.SourceKind),
            ["review_state"] = ToWirePricingReviewState(reviewState)
        };
    }

    private static TokenMetricStatus AggregateCostMixMetricStatus(IReadOnlyList<TokenMetricStatus> statuses)
    {
        var distinct = statuses.Distinct().ToArray();
        return distinct.Length == 1 ? distinct[0] : TokenMetricStatus.Mixed;
    }

    private static IReadOnlyDictionary<string, string> NormalizeAggregateMetricLabels(
        string metricName,
        IReadOnlyDictionary<string, string> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);

        var normalizedLabels = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var label in labels.OrderBy(static label => label.Key, StringComparer.Ordinal))
        {
            var key = NormalizeRequiredText(label.Key, nameof(labels));
            var value = NormalizeRequiredText(label.Value, nameof(labels));

            if (!IsAllowedAggregateMetricLabel(metricName, key))
            {
                throw new ArgumentException("Aggregate metric label is not supported.", nameof(labels));
            }

            if (ContainsSensitiveFragment(key) || ContainsSensitiveFragment(value))
            {
                throw new ArgumentException("Aggregate metric labels must not contain sensitive fragments.", nameof(labels));
            }

            if (value.Length > 128 || !IsSafeResourceId(value))
            {
                throw new ArgumentException("Aggregate metric label value is not allowed.", nameof(labels));
            }

            normalizedLabels.Add(key, value);
        }

        RequireAggregateLabel(normalizedLabels, "customer_organization_slug");
        RequireAggregateLabel(normalizedLabels, "environment");
        RequireAggregateLabel(normalizedLabels, "region");

        return normalizedLabels;
    }

    private static bool IsAllowedAggregateMetricLabel(string metricName, string label)
    {
        if (label is "customer_organization_slug" or "environment" or "region" or "harness")
        {
            return true;
        }

        return metricName switch
        {
            "tokenobs_tokens_total" or "tokenobs_token_metric_states_total" => label is "model_provider" or "model" or "token_type" or "metric_status" or "metric_confidence",
            "tokenobs_model_invocations_total" => label is "model_provider" or "model" or "result",
            "tokenobs_turns_total" => label is "result",
            "tokenobs_hotspots_open" => label is "hotspot_type" or "finding_state",
            "tokenobs_ingestion_requests_total" => label is "signal_type" or "result" or "rejection_reason" or "schema_version",
            "tokenobs_sessions_started_total" => false,
            _ => false
        };
    }

    private static void RequireAggregateLabel(
        IReadOnlyDictionary<string, string> labels,
        string labelName)
    {
        if (!labels.ContainsKey(labelName))
        {
            throw new ArgumentException($"Aggregate metric label '{labelName}' is required.", nameof(labels));
        }
    }

    private static string NormalizeAggregateMetricExportFailureReason(string value, string parameterName)
    {
        var normalized = NormalizeRequiredText(value, parameterName);

        return normalized is "sink_failure" or "invalid_metric_shape"
            ? normalized
            : throw new ArgumentException("Aggregate metric export failure reason is not supported.", parameterName);
    }

    private static int TokenMetricSortOrder(TokenMetricName metricName)
    {
        return metricName switch
        {
            TokenMetricName.InputTokens => 0,
            TokenMetricName.OutputTokens => 1,
            TokenMetricName.CachedInputTokens => 2,
            TokenMetricName.ReasoningOutputTokens => 3,
            TokenMetricName.TotalTokens => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(metricName), metricName, null)
        };
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
        return normalized.Length == 3 && normalized.All(static chararacter => char.IsAsciiLetterUpper(chararacter))
            ? normalized
            : throw new ArgumentException("Currency must use a three-letter code.", parameterName);
    }

    private static string NormalizePricingVersion(string value, string parameterName)
    {
        var normalized = NormalizeRequiredText(value, parameterName);
        return normalized.Length <= 128 && IsSafeResourceId(normalized)
            ? normalized
            : throw new ArgumentException("Pricing version is not allowed.", parameterName);
    }

    public static string ToWirePricingTokenType(PricingTokenType tokenType)
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

    public static string ToWirePricingSourceKind(PricingSourceKind sourceKind)
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

    public static string ToWirePricingReviewState(PricingReviewState reviewState)
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

    public static string ToWireCostStatus(CostEstimateStatus costStatus)
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

    private static string NormalizeContentDecision(string value, string parameterName)
    {
        var normalized = NormalizeRequiredText(value, parameterName);
        return normalized is "metadata_only" or "capture_candidate" or "blocked" or "redaction_required"
            ? normalized
            : throw new ArgumentException("Content policy decision is not supported.", parameterName);
    }

    private static string NormalizeContentCaptureState(string value, string parameterName)
    {
        var normalized = NormalizeRequiredText(value, parameterName);
        return normalized is "none" or "metadata_only" or "captured" or "review_required" or "redaction_failed" or "mixed"
            ? normalized
            : throw new ArgumentException("Content capture state is not supported.", parameterName);
    }

    private static string NormalizeRedactionState(string value, string parameterName)
    {
        var normalized = NormalizeRequiredText(value, parameterName);
        return normalized is "not_required" or "passed" or "failed" or "review_required"
            ? normalized
            : throw new ArgumentException("Redaction state is not supported.", parameterName);
    }

    private static string NormalizeSourceEvidenceKind(string value, string parameterName)
    {
        var normalized = NormalizeRequiredText(value, parameterName);
        return normalized is "harness_emitted" or "product_derived" or "scanner" or "manual_review"
            ? normalized
            : throw new ArgumentException("Source evidence kind is not supported.", parameterName);
    }

    private static IReadOnlyDictionary<string, string> NormalizeRoutingDecision(
        IReadOnlyDictionary<string, string> values)
    {
        return NormalizeSafeDictionary(
            values,
            allowedKeys: ["result", "metadata_store", "diagnostic_store", "metrics_store", "content_capture"]);
    }

    private static IReadOnlyDictionary<string, string> NormalizeIngestionVersionMetadata(
        IReadOnlyDictionary<string, string> values)
    {
        return NormalizeSafeDictionary(
            values,
            allowedKeys: ["schema_version", "harness_version", "contract_version"]);
    }

    private static IReadOnlyDictionary<string, string> NormalizeSafeDictionary(
        IReadOnlyDictionary<string, string> values,
        IReadOnlyCollection<string> allowedKeys)
    {
        if (values.Count == 0)
        {
            throw new ArgumentException("Accepted telemetry metadata dictionaries must not be empty.", nameof(values));
        }

        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, value) in values)
        {
            var normalizedKey = NormalizeRequiredText(key, nameof(values));
            var normalizedValue = NormalizeRequiredText(value, nameof(values));

            if (!allowedKeys.Contains(normalizedKey) ||
                normalizedKey.Length > 128 ||
                normalizedValue.Length > 512 ||
                ContainsSensitiveFragment(normalizedKey) ||
                ContainsSensitiveFragment(normalizedValue))
            {
                throw new ArgumentException("Accepted telemetry metadata contains unsupported or sensitive values.", nameof(values));
            }

            normalized.Add(normalizedKey, normalizedValue);
        }

        return new ReadOnlyDictionary<string, string>(normalized);
    }

    private static bool ContainsSensitiveFragment(string value)
    {
        return SensitiveEvidenceValueFragments.Any(fragment =>
                value.Contains(fragment, StringComparison.OrdinalIgnoreCase)) ||
            SensitiveEvidenceKeyFragments.Any(fragment =>
                value.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static void RejectSensitiveText(string value)
    {
        if (ContainsSensitiveFragment(value))
        {
            throw new ArgumentException("Metadata must not store captured content or secrets.", nameof(value));
        }
    }

    private void CreateContentReferenceFromCandidateMetadataUnderLock(
        ContentCandidateMetadata metadata,
        DateTimeOffset now)
    {
        var telemetryEnvelopeId = ExtractTelemetryEnvelopeId(metadata.TelemetryReference);
        if (!telemetryEnvelopes.TryGetValue(telemetryEnvelopeId, out var envelope) ||
            envelope.CustomerOrganizationId != metadata.CustomerOrganizationId)
        {
            return;
        }

        if (!agentSessions.Values.Any(session =>
                session.CustomerOrganizationId == metadata.CustomerOrganizationId &&
                StringComparer.Ordinal.Equals(session.AgentSessionId, metadata.SessionId)))
        {
            return;
        }

        var contentReferenceId = new ContentReferenceId(Guid.NewGuid());
        var captureState = ToContentReferenceCaptureState(metadata);
        var redactionStatus = ToContentReferenceRedactionStatus(metadata);
        var contentHash = captureState == ContentReferenceCaptureState.Captured ||
            (metadata.RedactionOutcome == ContentRedactionOutcome.Captured && metadata.RedactionStatus == ContentRedactionStatus.Passed)
            ? metadata.RedactedContentHash
            : null;
        var expiresAtUtc = GetContentReferenceExpiresAtUtc(captureState, metadata.RetentionClass, now);
        var blobPointer = captureState == ContentReferenceCaptureState.Captured
            ? BuildCapturedContentBlobPointer(metadata.CustomerOrganizationId, metadata.SessionId, contentReferenceId, now, metadata.RedactedContentBlobVersion)
            : null;
        var recommendationEligible = captureState == ContentReferenceCaptureState.Captured &&
            metadata.RecommendationUse == RecommendationUse.ApprovedEvidence;
        var auditEventId = $"audit-content-reference-{Guid.NewGuid():N}";

        var auditEvent = CreateGovernanceAuditEventUnderLock(
            auditEventId,
            metadata.CustomerOrganizationId,
            scopedIngestionCredentials.TryGetValue(metadata.ScopedIngestionCredentialId, out var credential)
                ? credential.ProductUserId
                : null,
            effectiveRole: null,
            ProductAuthorizationAction.TelemetryIngest,
            "content_reference",
            contentReferenceId.ToString(),
            "created",
            denialReason: null,
            $"content-reference-{Guid.NewGuid():N}",
            CreateContentReferenceAuditMetadata(
                operation: "content_reference_create",
                result: ToWireContentReferenceCaptureState(captureState),
                contentReferenceId,
                captureState,
                redactionStatus,
                metadata.RetentionClass,
                recommendationEligible),
            now);

        contentReferences.Add(contentReferenceId, new ContentReferenceRecord(
            contentReferenceId,
            metadata.CustomerOrganizationId,
            metadata.SessionId,
            telemetryEnvelopeId,
            metadata.ContentClass,
            captureState,
            redactionStatus,
            contentHash,
            blobPointer,
            metadata.PolicyVersionId,
            metadata.RedactionPipelineVersion,
            metadata.ProductRuleVersion,
            metadata.RetentionClass,
            expiresAtUtc,
            recommendationEligible,
            auditEvent.AuditEventId,
            ApprovedExcerpt: null,
            now,
            now));
    }

    private static string ExtractTelemetryEnvelopeId(string telemetryReference)
    {
        var separatorIndex = telemetryReference.IndexOf(':', StringComparison.Ordinal);
        return separatorIndex <= 0 ? telemetryReference : telemetryReference[..separatorIndex];
    }

    private static ContentReferenceCaptureState ToContentReferenceCaptureState(ContentCandidateMetadata metadata)
    {
        if (metadata.RedactionOutcome == ContentRedactionOutcome.Captured &&
            metadata.RedactionStatus == ContentRedactionStatus.Passed &&
            metadata.RedactedContentStored)
        {
            return ContentReferenceCaptureState.Captured;
        }

        if (metadata.RedactionOutcome == ContentRedactionOutcome.ReviewRequired ||
            metadata.RedactionStatus == ContentRedactionStatus.ReviewRequired ||
            metadata.EvidenceState == ContentCandidateEvidenceState.ReviewRequired)
        {
            return ContentReferenceCaptureState.ReviewRequired;
        }

        if (metadata.RedactionOutcome == ContentRedactionOutcome.RedactionFailed ||
            metadata.RedactionStatus == ContentRedactionStatus.Failed ||
            metadata.EvidenceState == ContentCandidateEvidenceState.RedactionFailed)
        {
            return ContentReferenceCaptureState.RedactionFailed;
        }

        if (metadata.PolicyDecision == ContentCapturePolicyDecision.PolicyDenied)
        {
            return ContentReferenceCaptureState.NotAllowed;
        }

        return ContentReferenceCaptureState.MetadataOnly;
    }

    private static ContentReferenceRedactionStatus ToContentReferenceRedactionStatus(ContentCandidateMetadata metadata)
    {
        return metadata.RedactionStatus switch
        {
            ContentRedactionStatus.NotRequired => ContentReferenceRedactionStatus.NotRequired,
            ContentRedactionStatus.Pending => ContentReferenceRedactionStatus.ReviewRequired,
            ContentRedactionStatus.ReviewRequired => ContentReferenceRedactionStatus.ReviewRequired,
            ContentRedactionStatus.Failed => ContentReferenceRedactionStatus.Failed,
            ContentRedactionStatus.Passed => ContentReferenceRedactionStatus.Passed,
            _ => throw new ArgumentOutOfRangeException(nameof(metadata), metadata.RedactionStatus, null)
        };
    }

    private static DateTimeOffset? GetContentReferenceExpiresAtUtc(
        ContentReferenceCaptureState captureState,
        ContentRetentionClass retentionClass,
        DateTimeOffset createdAtUtc)
    {
        if (captureState is ContentReferenceCaptureState.Captured or ContentReferenceCaptureState.ApprovedExcerpt)
        {
            return createdAtUtc.AddDays(30);
        }

        if (retentionClass is ContentRetentionClass.Review or ContentRetentionClass.Blocked)
        {
            return createdAtUtc.AddDays(180);
        }

        return null;
    }

    private static CapturedContentBlobPointer BuildCapturedContentBlobPointer(
        CustomerOrganizationId customerOrganizationId,
        string agentSessionId,
        ContentReferenceId contentReferenceId,
        DateTimeOffset decisionTimeUtc,
        string? blobVersion)
    {
        var date = decisionTimeUtc.ToUniversalTime();
        var blobName =
            $"customer-organization-id={customerOrganizationId}/" +
            $"yyyy={date:yyyy}/mm={date:MM}/dd={date:dd}/" +
            $"session-id={agentSessionId}/" +
            $"content-reference-id={contentReferenceId}/" +
            "redacted.txt";

        return new CapturedContentBlobPointer(
            "captured-content",
            blobName,
            $"azblob://captured-content/{blobName}",
            blobVersion);
    }

    private static CapturedContentBlobPointer BuildApprovedExcerptBlobPointer(
        CustomerOrganizationId customerOrganizationId,
        ContentReferenceId contentReferenceId,
        DateTimeOffset decisionTimeUtc)
    {
        var date = decisionTimeUtc.ToUniversalTime();
        var blobName =
            $"customer-organization-id={customerOrganizationId}/" +
            $"yyyy={date:yyyy}/mm={date:MM}/dd={date:dd}/" +
            $"content-reference-id={contentReferenceId}/" +
            "approved-excerpt.txt";

        return new CapturedContentBlobPointer(
            "content-review-artifacts",
            blobName,
            $"azblob://content-review-artifacts/{blobName}",
            BlobVersion: null);
    }

    private static bool IsContentReviewVisibleState(ContentReferenceCaptureState state)
    {
        return state is ContentReferenceCaptureState.ReviewRequired or
            ContentReferenceCaptureState.RedactionFailed or
            ContentReferenceCaptureState.Discarded or
            ContentReferenceCaptureState.ApprovedExcerpt;
    }

    private static bool IsReviewableState(ContentReferenceCaptureState state)
    {
        return state is ContentReferenceCaptureState.ReviewRequired or ContentReferenceCaptureState.RedactionFailed;
    }

    private static string? NormalizeApprovedExcerpt(ReviewContentReferenceRequest request)
    {
        if (request.Decision != RedactionReviewDecision.ApproveExcerpt)
        {
            if (!string.IsNullOrWhiteSpace(request.ApprovedExcerpt))
            {
                throw new ArgumentException("Approved excerpt is only allowed for approve-excerpt decisions.", nameof(request));
            }

            return null;
        }

        var excerpt = NormalizeRequiredText(request.ApprovedExcerpt ?? string.Empty, nameof(request.ApprovedExcerpt));
        RejectSensitiveText(excerpt);
        if (Encoding.UTF8.GetByteCount(excerpt) > ContentRedactionLimits.MaxApprovedExcerptUtf8Bytes)
        {
            throw new ArgumentException("Approved excerpt exceeds the bounded excerpt size limit.", nameof(request));
        }

        return excerpt;
    }

    private static ContentReferenceRecord ApplyReviewDecision(
        ContentReferenceRecord existing,
        RedactionReviewDecision decision,
        string? approvedExcerpt,
        DateTimeOffset now,
        string auditEventId)
    {
        return decision switch
        {
            RedactionReviewDecision.Retry => existing with
            {
                AuditEventId = auditEventId,
                UpdatedAtUtc = now
            },
            RedactionReviewDecision.Discard => existing with
            {
                CaptureState = ContentReferenceCaptureState.Discarded,
                RedactionStatus = existing.RedactionStatus,
                BlobPointer = null,
                ApprovedExcerpt = null,
                RecommendationEligible = false,
                AuditEventId = auditEventId,
                UpdatedAtUtc = now
            },
            RedactionReviewDecision.ApproveExcerpt => existing with
            {
                CaptureState = ContentReferenceCaptureState.ApprovedExcerpt,
                RedactionStatus = ContentReferenceRedactionStatus.ManuallyApproved,
                ContentHash = ComputeSha256Hex(approvedExcerpt ?? string.Empty),
                BlobPointer = BuildApprovedExcerptBlobPointer(existing.CustomerOrganizationId, existing.ContentReferenceId, now),
                ApprovedExcerpt = approvedExcerpt,
                RecommendationEligible = true,
                ExpiresAtUtc = now.AddDays(30),
                AuditEventId = auditEventId,
                UpdatedAtUtc = now
            },
            RedactionReviewDecision.RejectExcerpt => existing with
            {
                AuditEventId = auditEventId,
                UpdatedAtUtc = now
            },
            RedactionReviewDecision.MarkRecommendationIneligible => existing with
            {
                RecommendationEligible = false,
                AuditEventId = auditEventId,
                UpdatedAtUtc = now
            },
            _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, null)
        };
    }

    private static IReadOnlyDictionary<string, string> CreateContentReferenceAuditMetadata(
        string operation,
        string result,
        ContentReferenceId contentReferenceId,
        ContentReferenceCaptureState captureState,
        ContentReferenceRedactionStatus redactionStatus,
        ContentRetentionClass retentionClass,
        bool recommendationEligible)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["evidence_kind"] = "ingestion_decision",
            ["operation"] = operation,
            ["result"] = result,
            ["content_reference_id"] = contentReferenceId.ToString(),
            ["capture_state"] = ToWireContentReferenceCaptureState(captureState),
            ["redaction_status"] = ToWireContentReferenceRedactionStatus(redactionStatus),
            ["retention_class"] = ToWireContentRetentionClass(retentionClass),
            ["recommendation_eligible"] = recommendationEligible ? "true" : "false"
        };
    }

    private static IReadOnlyDictionary<string, string> CreateContentReviewAuditMetadata(
        string operation,
        string result,
        ContentReferenceId contentReferenceId,
        RedactionReviewId redactionReviewId,
        RedactionReviewDecision decision,
        ContentReferenceRecord existing)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["evidence_kind"] = "admin_operation",
            ["operation"] = operation,
            ["result"] = result,
            ["content_reference_id"] = contentReferenceId.ToString(),
            ["redaction_review_id"] = redactionReviewId.ToString(),
            ["review_decision"] = ToWireRedactionReviewDecision(decision),
            ["capture_state"] = ToWireContentReferenceCaptureState(existing.CaptureState),
            ["redaction_status"] = ToWireContentReferenceRedactionStatus(existing.RedactionStatus),
            ["retention_class"] = ToWireContentRetentionClass(existing.RetentionClass),
            ["recommendation_eligible"] = existing.RecommendationEligible ? "true" : "false"
        };
    }

    public static string ToWireContentReferenceCaptureState(ContentReferenceCaptureState state)
    {
        return state switch
        {
            ContentReferenceCaptureState.NotAllowed => "not_allowed",
            ContentReferenceCaptureState.MetadataOnly => "metadata_only",
            ContentReferenceCaptureState.Captured => "captured",
            ContentReferenceCaptureState.RedactionFailed => "redaction_failed",
            ContentReferenceCaptureState.ReviewRequired => "review_required",
            ContentReferenceCaptureState.Discarded => "discarded",
            ContentReferenceCaptureState.ApprovedExcerpt => "approved_excerpt",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };
    }

    public static string ToWireContentReferenceRedactionStatus(ContentReferenceRedactionStatus status)
    {
        return status switch
        {
            ContentReferenceRedactionStatus.NotRequired => "not_required",
            ContentReferenceRedactionStatus.Passed => "passed",
            ContentReferenceRedactionStatus.Failed => "failed",
            ContentReferenceRedactionStatus.ReviewRequired => "review_required",
            ContentReferenceRedactionStatus.ManuallyApproved => "manually_approved",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
    }

    public static string ToWireRedactionReviewDecision(RedactionReviewDecision decision)
    {
        return decision switch
        {
            RedactionReviewDecision.Retry => "retry",
            RedactionReviewDecision.Discard => "discard",
            RedactionReviewDecision.ApproveExcerpt => "approve_excerpt",
            RedactionReviewDecision.RejectExcerpt => "reject_excerpt",
            RedactionReviewDecision.MarkRecommendationIneligible => "mark_recommendation_ineligible",
            _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, null)
        };
    }

    public static string ToWireContentClass(ContentClass contentClass)
    {
        return contentClass switch
        {
            ContentClass.PromptSnippet => "prompt_snippet",
            ContentClass.ToolInputExcerpt => "tool_input_excerpt",
            ContentClass.ToolOutputExcerpt => "tool_output_excerpt",
            ContentClass.ModelResponseExcerpt => "model_response_excerpt",
            ContentClass.CommandSummary => "command_summary",
            ContentClass.FileContentExcerpt => "file_content_excerpt",
            ContentClass.MetadataOnly => "metadata_only",
            _ => throw new ArgumentOutOfRangeException(nameof(contentClass), contentClass, null)
        };
    }

    public static string ToWireContentRetentionClass(ContentRetentionClass retentionClass)
    {
        return retentionClass switch
        {
            ContentRetentionClass.MetadataOnly => "metadata_only",
            ContentRetentionClass.Short => "short",
            ContentRetentionClass.Review => "review",
            ContentRetentionClass.Blocked => "blocked",
            _ => throw new ArgumentOutOfRangeException(nameof(retentionClass), retentionClass, null)
        };
    }

    private static string ComputeSha256Hex(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static string ToWirePolicyChangeKind(ContentCapturePolicyChangeKind changeKind)
    {
        return changeKind switch
        {
            ContentCapturePolicyChangeKind.Created => "created",
            ContentCapturePolicyChangeKind.Activated => "activated",
            ContentCapturePolicyChangeKind.Updated => "updated",
            ContentCapturePolicyChangeKind.Disabled => "disabled",
            _ => throw new ArgumentOutOfRangeException(nameof(changeKind), changeKind, "Policy change kind is not supported.")
        };
    }

    private static string NormalizeHash(string value, string parameterName)
    {
        var normalized = NormalizeRequiredText(value, parameterName);
        return normalized.Length == 64 &&
            normalized.All(static character =>
                char.IsAsciiHexDigit(character) &&
                !char.IsUpper(character))
            ? normalized
            : throw new ArgumentException("Hash values must be lowercase sha256 hex.", parameterName);
    }

    private static string? NormalizeOptionalHash(string? value, string parameterName)
    {
        return string.IsNullOrWhiteSpace(value) ? null : NormalizeHash(value, parameterName);
    }

    private static string NormalizeSha256Hex(string value, string parameterName)
    {
        var normalized = NormalizeRequiredText(value, parameterName).ToUpperInvariant();
        return normalized.Length == 64 && normalized.All(char.IsAsciiHexDigit)
            ? normalized
            : throw new ArgumentException("Evidence packet hash must be sha256 hex.", parameterName);
    }

    private static string NormalizeJsonObject(string value, string parameterName)
    {
        var normalized = NormalizeRequiredText(value, parameterName);
        using var document = System.Text.Json.JsonDocument.Parse(normalized);
        if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            throw new ArgumentException("JSON value must be an object.", parameterName);
        }

        return normalized;
    }

    private static string NormalizeSafeResourceId(string value, string parameterName)
    {
        var normalized = NormalizeRequiredText(value, parameterName);
        if (normalized.Length > 128 || !IsSafeResourceId(normalized))
        {
            throw new ArgumentException("Value must be a bounded safe resource identifier.", parameterName);
        }

        return normalized;
    }

    private static string NormalizeDeploymentAlias(string value, string parameterName)
    {
        var normalized = NormalizeSafeResourceId(value, parameterName);
        return normalized is "recommendation-writer-primary" or "recommendation-writer-fallback"
            ? normalized
            : throw new ArgumentException("Recommendation deployment alias is not supported.", parameterName);
    }

    private static string NormalizeRecommendationText(string value, string parameterName)
    {
        var normalized = NormalizeRequiredText(value, parameterName);
        if (normalized.Length > 1024)
        {
            throw new ArgumentException("Recommendation text must be 1024 characters or less.", parameterName);
        }

        return normalized;
    }

    private static IReadOnlyList<string> NormalizeRecommendationEvidenceReferences(
        IReadOnlyList<string> evidenceReferenceIds)
    {
        if (evidenceReferenceIds.Count is 0 or > 64)
        {
            throw new ArgumentException("Recommendation evidence references require between 1 and 64 references.", nameof(evidenceReferenceIds));
        }

        return evidenceReferenceIds
            .Select(static reference => NormalizeRequiredText(reference, nameof(evidenceReferenceIds)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string NormalizeRecommendationRegenerationReason(string value, string parameterName)
    {
        var normalized = NormalizeRequiredText(value, parameterName);
        if (normalized.Length > 128 || !IsSafeResourceId(normalized))
        {
            throw new ArgumentException("Recommendation regeneration reason must be a bounded reason code.", parameterName);
        }

        if (SensitiveEvidenceValueFragments.Any(fragment => normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Recommendation regeneration reason must not store captured content or secrets.", parameterName);
        }

        return normalized;
    }

    private static void ValidateSafeRecommendationText(params string[] values)
    {
        foreach (var value in values)
        {
            if (UnsupportedHotspotSummaryFragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("Recommendation text must not contain blame, ranking, or unsupported user-error claims.", nameof(values));
            }

            if (SensitiveEvidenceValueFragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("Recommendation text must not store captured content or secrets.", nameof(values));
            }
        }
    }

    private static void ValidateRecommendationAuthority(CreateRecommendationRecordRequest request)
    {
        if (request.Kind == RecommendationKind.Deterministic &&
            request.AuthorityState != RecommendationAuthorityState.Deterministic)
        {
            throw new ArgumentException("Deterministic recommendations require deterministic authority.", nameof(request));
        }

        if (request.AuthorityState == RecommendationAuthorityState.LlmInferredCandidate &&
            request.State == RecommendationState.Accepted)
        {
            throw new ArgumentException("LLM-inferred recommendations must remain candidates until product validation.", nameof(request));
        }

        if (request.ValidationState == RecommendationValidationState.Rejected &&
            request.State is not RecommendationState.Rejected)
        {
            throw new ArgumentException("Rejected validation state requires rejected recommendation state.", nameof(request));
        }
    }

    public static string ToWireRecommendationKind(RecommendationKind kind)
    {
        return kind switch
        {
            RecommendationKind.Deterministic => "deterministic",
            RecommendationKind.LlmAssisted => "llm_assisted",
            RecommendationKind.Mixed => "mixed",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    public static string ToWireRecommendationState(RecommendationState state)
    {
        return state switch
        {
            RecommendationState.Candidate => "candidate",
            RecommendationState.Accepted => "accepted",
            RecommendationState.Rejected => "rejected",
            RecommendationState.Expired => "expired",
            RecommendationState.Superseded => "superseded",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };
    }

    public static string ToWireRecommendationAuthorityState(RecommendationAuthorityState authorityState)
    {
        return authorityState switch
        {
            RecommendationAuthorityState.Deterministic => "deterministic",
            RecommendationAuthorityState.LlmAssisted => "llm_assisted",
            RecommendationAuthorityState.LlmInferredCandidate => "llm_inferred_candidate",
            RecommendationAuthorityState.Rejected => "rejected",
            _ => throw new ArgumentOutOfRangeException(nameof(authorityState), authorityState, null)
        };
    }

    public static string ToWireRecommendationConfidence(RecommendationConfidence confidence)
    {
        return confidence switch
        {
            RecommendationConfidence.Low => "low",
            RecommendationConfidence.Medium => "medium",
            RecommendationConfidence.High => "high",
            _ => throw new ArgumentOutOfRangeException(nameof(confidence), confidence, null)
        };
    }

    public static string ToWireRecommendationValidationState(RecommendationValidationState validationState)
    {
        return validationState switch
        {
            RecommendationValidationState.Pending => "pending",
            RecommendationValidationState.Validated => "validated",
            RecommendationValidationState.Rejected => "rejected",
            _ => throw new ArgumentOutOfRangeException(nameof(validationState), validationState, null)
        };
    }

    public static string ToWireRecommendationVisibilityScope(RecommendationVisibilityScope visibilityScope)
    {
        return visibilityScope switch
        {
            RecommendationVisibilityScope.Self => "self",
            RecommendationVisibilityScope.TeamScoped => "team_scoped",
            RecommendationVisibilityScope.SecurityReview => "security_review",
            RecommendationVisibilityScope.Admin => "admin",
            RecommendationVisibilityScope.AggregateOnly => "aggregate_only",
            _ => throw new ArgumentOutOfRangeException(nameof(visibilityScope), visibilityScope, null)
        };
    }

    public static string ToWireRecommendationRegenerationState(RecommendationRegenerationState state)
    {
        return state switch
        {
            RecommendationRegenerationState.Queued => "queued",
            RecommendationRegenerationState.Completed => "completed",
            RecommendationRegenerationState.Failed => "failed",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };
    }

    public static string ToWireRecommendationModelPolicyState(RecommendationModelPolicyState state)
    {
        return state switch
        {
            RecommendationModelPolicyState.Draft => "draft",
            RecommendationModelPolicyState.Active => "active",
            RecommendationModelPolicyState.Superseded => "superseded",
            RecommendationModelPolicyState.Disabled => "disabled",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };
    }

    public static string ToWireRecommendationModelProvider(RecommendationModelProvider provider)
    {
        return provider switch
        {
            RecommendationModelProvider.AzureOpenAi => "azure_openai",
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
        };
    }

    public static string ToWireRecommendationModelFallbackBehavior(RecommendationModelFallbackBehavior behavior)
    {
        return behavior switch
        {
            RecommendationModelFallbackBehavior.DeterministicOnly => "deterministic_only",
            RecommendationModelFallbackBehavior.FallbackDeploymentThenDeterministic => "fallback_deployment_then_deterministic",
            _ => throw new ArgumentOutOfRangeException(nameof(behavior), behavior, null)
        };
    }

    public static string ToWireRecommendationPromptTemplateState(RecommendationPromptTemplateState state)
    {
        return state switch
        {
            RecommendationPromptTemplateState.Draft => "draft",
            RecommendationPromptTemplateState.Active => "active",
            RecommendationPromptTemplateState.Superseded => "superseded",
            RecommendationPromptTemplateState.Disabled => "disabled",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };
    }

    public static string ToWireRecommendationPromptTemplatePurpose(RecommendationPromptTemplatePurpose purpose)
    {
        return purpose switch
        {
            RecommendationPromptTemplatePurpose.RecommendationDrafter => "recommendation_drafter",
            RecommendationPromptTemplatePurpose.CandidateHotspotGenerator => "candidate_hotspot_generator",
            RecommendationPromptTemplatePurpose.CacheBreakageExplainer => "cache_breakage_reasoner",
            _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null)
        };
    }

    private static string NormalizeDecision(string decision)
    {
        var normalizedDecision = NormalizeRequiredText(decision, nameof(decision)).ToLowerInvariant();
        return normalizedDecision is "created" or "updated" or "disabled" or "denied"
            ? normalizedDecision
            : throw new ArgumentException("Governance audit event decision is not supported.", nameof(decision));
    }

    private static void ValidateDenialShape(string decision, ProductAuthorizationDenialReason? denialReason)
    {
        if (decision == "denied" && denialReason is null)
        {
            throw new ArgumentException("Denied audit events require a denial reason.", nameof(denialReason));
        }

        if (decision != "denied" && denialReason is not null)
        {
            throw new ArgumentException("Only denied audit events can include a denial reason.", nameof(denialReason));
        }
    }

    private static void ValidateIngestionRejectionShape(
        CustomerOrganizationId? customerOrganizationId,
        ScopedIngestionCredentialId? scopedIngestionCredentialId,
        string signalType,
        string requestRoute,
        string reasonCode,
        int httpStatus,
        IReadOnlyDictionary<string, string> evidenceMetadata)
    {
        if (customerOrganizationId == CustomerOrganizationId.Empty)
        {
            throw new ArgumentException("Customer organization identifier must not be empty.", nameof(customerOrganizationId));
        }

        if (scopedIngestionCredentialId == ScopedIngestionCredentialId.Empty)
        {
            throw new ArgumentException("Scoped ingestion credential identifier must not be empty.", nameof(scopedIngestionCredentialId));
        }

        if (!IsSafeMachineToken(signalType))
        {
            throw new ArgumentException("Ingestion rejection signal type is not allowed.", nameof(signalType));
        }

        if (!IsSafeRoute(requestRoute))
        {
            throw new ArgumentException("Ingestion rejection request route is not allowed.", nameof(requestRoute));
        }

        if (!IsSafeMachineToken(reasonCode))
        {
            throw new ArgumentException("Ingestion rejection reason code is not allowed.", nameof(reasonCode));
        }

        if (httpStatus is < 400 or > 599)
        {
            throw new ArgumentException("Ingestion rejection HTTP status must be an error status.", nameof(httpStatus));
        }

        if (evidenceMetadata.Count == 0)
        {
            throw new ArgumentException("Ingestion rejection records require safe evidence metadata.", nameof(evidenceMetadata));
        }
    }

    private static IReadOnlyDictionary<string, string> NormalizeEvidenceMetadata(
        IReadOnlyDictionary<string, string>? evidenceMetadata)
    {
        if (evidenceMetadata is null || evidenceMetadata.Count == 0)
        {
            return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));
        }

        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, value) in evidenceMetadata)
        {
            var normalizedKey = NormalizeRequiredText(key, nameof(evidenceMetadata));
            var normalizedValue = NormalizeRequiredText(value, nameof(evidenceMetadata));

            if (!AllowedEvidenceMetadataKeys.Contains(normalizedKey))
            {
                throw new ArgumentException("Governance audit evidence metadata key is not allowed.", nameof(evidenceMetadata));
            }

            if (SensitiveEvidenceKeyFragments.Any(fragment =>
                    normalizedKey.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("Governance audit evidence metadata must not store captured content or secrets.", nameof(evidenceMetadata));
            }

            if (SensitiveEvidenceValueFragments.Any(fragment =>
                    normalizedValue.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("Governance audit evidence metadata must not store captured content or secrets.", nameof(evidenceMetadata));
            }

            if (normalizedKey.Length > 128)
            {
                throw new ArgumentException("Governance audit evidence metadata keys must be 128 characters or fewer.", nameof(evidenceMetadata));
            }

            if (normalizedValue.Length > 512)
            {
                throw new ArgumentException("Governance audit evidence metadata values must be 512 characters or fewer.", nameof(evidenceMetadata));
            }

            ValidateAllowedEvidenceMetadataValue(normalizedKey, normalizedValue, nameof(evidenceMetadata));
            normalized.Add(normalizedKey, normalizedValue);
        }

        return new ReadOnlyDictionary<string, string>(normalized);
    }

    private static void ValidateAllowedEvidenceMetadataValue(string key, string value, string parameterName)
    {
        var isAllowed = key switch
        {
            "evidence_kind" => value is "admin_operation" or "authorization_decision" or "ingestion_decision",
            "operation" => IsSafeMachineToken(value),
            "request_route" => (value.StartsWith("/api/v1/", StringComparison.Ordinal) ||
                    value is "/v1/logs" or "/v1/traces" or "/v1/metrics") &&
                IsSafeRoute(value),
            "result" => IsSafeMachineToken(value),
            "external_principal_type" => Enum.TryParse<ExternalPrincipalType>(value, ignoreCase: false, out _),
            "product_role" => Enum.TryParse<ProductRole>(value, ignoreCase: false, out _),
            "scope_kind" or "requested_scope_kind" => Enum.TryParse<ProductScopeKind>(value, ignoreCase: false, out _),
            "scope_id" or "requested_scope_id" => IsSafeResourceId(value),
            "denial_reason" => Enum.TryParse<ProductAuthorizationDenialReason>(value, ignoreCase: false, out _),
            "policy_kind" => value is "content_capture",
            "policy_version_id" => IsSafeResourceId(value),
            "change_kind" => value is "created" or "activated" or "updated" or "disabled",
            "pricing_basis_id" => IsSafeResourceId(value),
            "pricing_version" => IsSafeResourceId(value),
            "provider_name" or "model_name" or "billing_route" => IsSafeResourceId(value),
            "token_type" => value is "input" or "output" or "cached_input" or "reasoning_output",
            "source_kind" => value is "automated_seed" or "admin_override" or "provider_docs" or "enterprise_contract",
            "review_state" => value is "candidate" or "approved" or "rejected" or "superseded",
            "recommendation_kind" => value is "deterministic" or "llm_assisted" or "mixed",
            "authority_state" => value is "deterministic" or "llm_assisted" or "llm_inferred_candidate" or "rejected",
            "validation_state" => value is "pending" or "validated" or "rejected",
            "evidence_packet_version" => IsSafeResourceId(value),
            "prompt_template_version" => IsSafeResourceId(value),
            "deployment_alias" => value is "recommendation-writer-primary" or "recommendation-writer-fallback",
            "fallback_behavior" => value is "deterministic_only" or "fallback_deployment_then_deterministic",
            "structured_output_schema_version" => IsSafeResourceId(value),
            "failure_code" => IsSafeResourceId(value),
            "content_capture_policy_version" => IsSafeResourceId(value),
            "recommendation_model_policy_version" => IsSafeResourceId(value),
            "pricing_basis_version" => IsSafeResourceId(value),
            "reason" => IsSafeResourceId(value),
            "agent_session_id" => IsSafeResourceId(value),
            "token_hotspot_id" => IsSafeResourceId(value),
            "content_reference_id" => Guid.TryParse(value, out var contentReferenceGuid) && contentReferenceGuid != Guid.Empty,
            "redaction_review_id" => Guid.TryParse(value, out var redactionReviewGuid) && redactionReviewGuid != Guid.Empty,
            "capture_state" => value is "not_allowed" or "metadata_only" or "captured" or "redaction_failed" or "review_required" or "discarded" or "approved_excerpt",
            "redaction_status" => value is "not_required" or "passed" or "failed" or "review_required" or "manually_approved",
            "retention_class" => value is "metadata_only" or "short" or "review" or "blocked",
            "review_decision" => value is "retry" or "discard" or "approve_excerpt" or "reject_excerpt" or "mark_recommendation_ineligible",
            "recommendation_eligible" => value is "true" or "false",
            "budget_policy_id" => IsSafeResourceId(value),
            "budget_scope_kind" => value is "customer_organization" or "team" or "repository" or "workflow" or "harness" or "model",
            "budget_metric_kind" => value is "tokens" or "estimated_cost" or "cache_miss_rate" or "error_rework",
            "budget_policy_status" => value is "active" or "disabled",
            _ => false
        };

        if (!isAllowed)
        {
            throw new ArgumentException("Governance audit evidence metadata value is not allowed.", parameterName);
        }
    }

    private static string NormalizeHarnessSetupProfileId(string value, string parameterName)
    {
        var normalized = NormalizeRequiredText(value, parameterName);

        if (!IsSafeResourceId(normalized) ||
            SensitiveEvidenceValueFragments.Any(fragment =>
                normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase)) ||
            normalized.Contains("prompt", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("command", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("tool", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Ingestion rejection harness setup profile identifier is not allowed.", parameterName);
        }

        return normalized;
    }

    private static string NormalizeDeclaredHarness(string value, string parameterName)
    {
        var normalized = NormalizeRequiredText(value, parameterName);
        return normalized is "codex-cli"
            ? normalized
            : throw new ArgumentException("Ingestion rejection declared harness is not allowed.", parameterName);
    }

    private static bool IsSafeMachineToken(string value)
    {
        return value.All(static character =>
            char.IsAsciiLetterOrDigit(character) ||
            character is '_' or '-' or '.' or ':');
    }

    private static bool IsSafeRoute(string value)
    {
        return value.All(static character =>
            char.IsAsciiLetterOrDigit(character) ||
            character is '/' or '-' or '_' or '{' or '}');
    }

    private static bool IsSafeResourceId(string value)
    {
        return value.All(static character =>
            char.IsAsciiLetterOrDigit(character) ||
            character is '-' or '_' or ':' or '.' or '/');
    }

    private static string GetTargetResourceId(CustomerOrganizationId customerOrganizationId, ProductScope requestedScope)
    {
        return string.IsNullOrWhiteSpace(requestedScope.ScopeId)
            ? customerOrganizationId.ToString()
            : requestedScope.ScopeId.Trim();
    }

    private void RecordAuthorizationAuditEventUnderLock(
        CustomerOrganizationId customerOrganizationId,
        ProductUserId? actorProductUserId,
        ProductRole? effectiveRole,
        ProductAuthorizationAction action,
        ProductScope requestedScope,
        ProductAuthorizationDenialReason denialReason,
        string? correlationId = null)
    {
        var auditEventId = $"authz-{Guid.NewGuid():N}";
        var normalizedCorrelationId = string.IsNullOrWhiteSpace(correlationId)
            ? auditEventId
            : correlationId.Trim();
        var targetResourceId = GetTargetResourceId(customerOrganizationId, requestedScope);
        var now = clock.UtcNow.ToUniversalTime();

        var evidenceMetadata = NormalizeEvidenceMetadata(new Dictionary<string, string>
        {
            ["evidence_kind"] = "authorization_decision",
            ["requested_scope_kind"] = requestedScope.Kind.ToString(),
            ["requested_scope_id"] = targetResourceId,
            ["result"] = "denied",
            ["denial_reason"] = denialReason.ToString()
        });

        CreateGovernanceAuditEventUnderLock(
            auditEventId,
            customerOrganizationId,
            actorProductUserId,
            effectiveRole,
            action,
            requestedScope.Kind.ToString(),
            targetResourceId,
            "denied",
            denialReason,
            normalizedCorrelationId,
            evidenceMetadata,
            now);
    }

    private ProductRoleMapping CreateProductRoleMappingUnderLock(
        CustomerOrganizationId customerOrganizationId,
        IdentityTenantId identityTenantId,
        ExternalPrincipalType externalPrincipalType,
        string externalPrincipalId,
        ProductRole productRole,
        ProductScopeKind scopeKind,
        string? scopeId,
        DateTimeOffset effectiveFromUtc,
        DateTimeOffset? effectiveToUtc,
        ProductUserId changedByProductUserId,
        ProductRole actorEffectiveRole,
        string auditEventId,
        string correlationId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        if (productRoleMappings.Values.Any(mapping =>
                mapping.CustomerOrganizationId == customerOrganizationId &&
                mapping.IdentityTenantId == identityTenantId &&
                mapping.ExternalPrincipalType == externalPrincipalType &&
                StringComparer.Ordinal.Equals(mapping.ExternalPrincipalId, externalPrincipalId) &&
                mapping.ProductRole == productRole &&
                mapping.ScopeKind == scopeKind &&
                StringComparer.Ordinal.Equals(mapping.ScopeId ?? string.Empty, scopeId ?? string.Empty) &&
                mapping.Status == ProductRoleMappingStatus.Active))
        {
            throw new InvalidOperationException("Active product role mapping already exists for the principal, role, and scope.");
        }

        var mapping = new ProductRoleMapping(
            ProductRoleMappingId.NewId(),
            customerOrganizationId,
            identityTenantId,
            externalPrincipalType,
            externalPrincipalId,
            productRole,
            scopeKind,
            scopeId,
            ProductRoleMappingStatus.Active,
            effectiveFromUtc,
            effectiveToUtc,
            changedByProductUserId,
            changedByProductUserId,
            auditEventId,
            createdAtUtc,
            updatedAtUtc);

        var evidenceMetadata = NormalizeEvidenceMetadata(new Dictionary<string, string>
        {
            ["evidence_kind"] = "admin_operation",
            ["operation"] = "product_role_mapping_create",
            ["external_principal_type"] = externalPrincipalType.ToString(),
            ["product_role"] = productRole.ToString(),
            ["scope_kind"] = scopeKind.ToString(),
            ["scope_id"] = scopeId ?? customerOrganizationId.ToString()
        });

        CreateGovernanceAuditEventUnderLock(
            auditEventId,
            customerOrganizationId,
            changedByProductUserId,
            actorEffectiveRole,
            ProductAuthorizationAction.IdentityManage,
            "product_role_mapping",
            mapping.ProductRoleMappingId.ToString(),
            "created",
            null,
            correlationId,
            evidenceMetadata,
            createdAtUtc);

        productRoleMappings.Add(mapping.ProductRoleMappingId, mapping);

        return mapping;
    }

    private GovernanceAuditEvent CreateGovernanceAuditEventUnderLock(
        string auditEventId,
        CustomerOrganizationId customerOrganizationId,
        ProductUserId? actorProductUserId,
        ProductRole? effectiveRole,
        ProductAuthorizationAction action,
        string targetResourceKind,
        string targetResourceId,
        string decision,
        ProductAuthorizationDenialReason? denialReason,
        string correlationId,
        IReadOnlyDictionary<string, string> evidenceMetadata,
        DateTimeOffset createdAtUtc)
    {
        var normalizedAuditEventId = NormalizeRequiredText(auditEventId, nameof(auditEventId));
        var key = new GovernanceAuditEventKey(customerOrganizationId, normalizedAuditEventId);

        if (governanceAuditEvents.ContainsKey(key))
        {
            throw new InvalidOperationException("Governance audit event already exists for the customer organization.");
        }

        var auditEvent = new GovernanceAuditEvent(
            normalizedAuditEventId,
            customerOrganizationId,
            actorProductUserId,
            effectiveRole,
            action,
            NormalizeRequiredText(targetResourceKind, nameof(targetResourceKind)),
            NormalizeRequiredText(targetResourceId, nameof(targetResourceId)),
            NormalizeDecision(decision),
            denialReason,
            NormalizeRequiredText(correlationId, nameof(correlationId)),
            NormalizeEvidenceMetadata(evidenceMetadata),
            createdAtUtc);

        ValidateDenialShape(auditEvent.Decision, auditEvent.DenialReason);
        governanceAuditEvents.Add(key, auditEvent);

        return auditEvent;
    }

    private void RequireCustomerOrganization(CustomerOrganizationId customerOrganizationId)
    {
        if (customerOrganizationId == CustomerOrganizationId.Empty ||
            !customerOrganizations.TryGetValue(customerOrganizationId, out var organization) ||
            organization.Status != CustomerOrganizationStatus.Active)
        {
            throw new InvalidOperationException("Customer organization is not active.");
        }
    }

    private void RequireCustomerOrganizationExists(CustomerOrganizationId customerOrganizationId)
    {
        if (customerOrganizationId == CustomerOrganizationId.Empty ||
            !customerOrganizations.ContainsKey(customerOrganizationId))
        {
            throw new InvalidOperationException("Customer organization does not exist.");
        }
    }

    private void RequireGovernanceAuditEvent(CustomerOrganizationId customerOrganizationId, string auditEventId)
    {
        var normalizedAuditEventId = NormalizeRequiredText(auditEventId, nameof(auditEventId));
        if (!governanceAuditEvents.ContainsKey(new GovernanceAuditEventKey(customerOrganizationId, normalizedAuditEventId)))
        {
            throw new InvalidOperationException("Governance audit event does not belong to the customer organization.");
        }
    }

    private void RequireIdentityTenantForCustomerOrganization(
        CustomerOrganizationId customerOrganizationId,
        IdentityTenantId identityTenantId)
    {
        if (identityTenantId == IdentityTenantId.Empty ||
            !identityTenants.TryGetValue(identityTenantId, out var identityTenant) ||
            identityTenant.CustomerOrganizationId != customerOrganizationId)
        {
            throw new InvalidOperationException("Identity tenant does not belong to the customer organization.");
        }
    }

    private void RequireProductUserForCustomerOrganization(
        CustomerOrganizationId customerOrganizationId,
        ProductUserId productUserId)
    {
        if (productUserId == ProductUserId.Empty ||
            !productUsers.TryGetValue(productUserId, out var productUser) ||
            productUser.CustomerOrganizationId != customerOrganizationId)
        {
            throw new InvalidOperationException("Product user does not belong to the customer organization.");
        }
    }

    private void RequireClaimsMatchIdentityTenant(IdentityTenantId identityTenantId, AuthenticatedTokenClaims claims)
    {
        if (!identityTenants.TryGetValue(identityTenantId, out var identityTenant) ||
            !StringComparer.Ordinal.Equals(identityTenant.ExternalTenantId, NormalizeRequiredText(claims.ExternalTenantId, nameof(claims.ExternalTenantId))) ||
            !StringComparer.Ordinal.Equals(identityTenant.Issuer, NormalizeRequiredText(claims.Issuer, nameof(claims.Issuer))) ||
            !identityTenant.AllowedAudiences.Contains(NormalizeRequiredText(claims.Audience, nameof(claims.Audience)), StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Authenticated claims do not match the identity tenant.");
        }
    }

    private ProductUser ResolveProductUserFromAuthenticatedClaimsUnderLock(
        CustomerOrganizationId customerOrganizationId,
        IdentityTenantId identityTenantId,
        AuthenticatedTokenClaims claims)
    {
        var externalSubjectId = NormalizeRequiredText(claims.Subject, nameof(claims.Subject));
        var displayLabel = NormalizeRequiredText(claims.DisplayLabel, nameof(claims.DisplayLabel));
        var email = string.IsNullOrWhiteSpace(claims.Email) ? null : claims.Email.Trim();
        var now = clock.UtcNow.ToUniversalTime();
        var lookupKey = new ProductUserLookupKey(customerOrganizationId, identityTenantId, externalSubjectId);

        if (productUserExternalSubjectIndex.TryGetValue(lookupKey, out var existingProductUserId))
        {
            var existingUser = productUsers[existingProductUserId];
            if (existingUser.Status != ProductUserStatus.Active)
            {
                throw new InvalidOperationException("Product user is not active.");
            }

            var updatedUser = existingUser with
            {
                DisplayLabel = displayLabel,
                Email = email,
                LastSeenAtUtc = now,
                UpdatedAtUtc = now
            };

            productUsers[existingProductUserId] = updatedUser;

            return updatedUser;
        }

        var productUser = new ProductUser(
            ProductUserId.NewId(),
            customerOrganizationId,
            identityTenantId,
            externalSubjectId,
            displayLabel,
            email,
            ProductUserStatus.Active,
            FirstSeenAtUtc: now,
            LastSeenAtUtc: now,
            CreatedAtUtc: now,
            UpdatedAtUtc: now);

        productUsers.Add(productUser.ProductUserId, productUser);
        productUserExternalSubjectIndex.Add(lookupKey, productUser.ProductUserId);

        return productUser;
    }

    private ProductRoleMapping[] FindActivePrincipalMappingsUnderLock(
        CustomerOrganizationId customerOrganizationId,
        IdentityTenantId identityTenantId,
        AuthenticatedTokenClaims claims,
        DateTimeOffset now)
    {
        return productRoleMappings.Values
            .Where(mapping =>
                mapping.CustomerOrganizationId == customerOrganizationId &&
                mapping.IdentityTenantId == identityTenantId &&
                mapping.Status == ProductRoleMappingStatus.Active &&
                mapping.EffectiveFromUtc <= now &&
                (mapping.EffectiveToUtc is null || mapping.EffectiveToUtc > now) &&
                PrincipalMatches(mapping, claims))
            .ToArray();
    }

    private static ProductRole? FirstEffectiveRoleOrNull(IReadOnlyList<ProductRoleMapping> mappings)
    {
        return mappings.Count == 0
            ? null
            : mappings.Select(mapping => mapping.ProductRole).Distinct().First();
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

    private static void RejectGrafanaAppRolePrincipal(
        ExternalPrincipalType externalPrincipalType,
        string externalPrincipalId)
    {
        if (externalPrincipalType != ExternalPrincipalType.AppRole)
        {
            return;
        }

        var normalizedPrincipalId = externalPrincipalId.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (StringComparer.OrdinalIgnoreCase.Equals(normalizedPrincipalId, "GrafanaAdmin") ||
            StringComparer.OrdinalIgnoreCase.Equals(normalizedPrincipalId, "GrafanaEditor") ||
            StringComparer.OrdinalIgnoreCase.Equals(normalizedPrincipalId, "GrafanaViewer") ||
            StringComparer.OrdinalIgnoreCase.Equals(normalizedPrincipalId, "GrafanaLimitedViewer"))
        {
            throw new ArgumentException("Grafana role names must not be used as Product API app-role mapping external principal IDs.", nameof(externalPrincipalId));
        }
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

    private static string? NormalizeScopeId(ProductScopeKind scopeKind, string? scopeId)
    {
        if (scopeKind == ProductScopeKind.Organization || scopeKind == ProductScopeKind.Self)
        {
            if (!string.IsNullOrWhiteSpace(scopeId))
            {
                throw new ArgumentException("Organization and self role mappings must not include a scope identifier.", nameof(scopeId));
            }

            return null;
        }

        if (string.IsNullOrWhiteSpace(scopeId))
        {
            throw new ArgumentException("A scope identifier is required for resource-scoped role mappings.", nameof(scopeId));
        }

        return scopeId.Trim();
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
            ProductRole.ReadOnlyViewer => action is
                ProductAuthorizationAction.OverviewRead,
            _ => false
        };
    }

    private static NormalizedBudgetPolicyRequest NormalizeBudgetPolicyRequest(
        BudgetPolicyScopeKind scopeKind,
        string? scopeId,
        BudgetMetricKind metricKind,
        string thresholdJson,
        BudgetPolicyStatus status)
    {
        var normalizedScopeId = scopeKind == BudgetPolicyScopeKind.CustomerOrganization
            ? null
            : NormalizeRequiredText(scopeId ?? string.Empty, nameof(scopeId));

        if (normalizedScopeId is not null &&
            (!IsSafeResourceId(normalizedScopeId) || ContainsSensitiveFragment(normalizedScopeId)))
        {
            throw new ArgumentException("Budget policy scope identifier is not allowed.", nameof(scopeId));
        }

        return new NormalizedBudgetPolicyRequest(
            scopeKind,
            normalizedScopeId,
            metricKind,
            BudgetPolicyValidator.NormalizeThresholdJson(metricKind, thresholdJson),
            status);
    }

    private static IReadOnlyDictionary<string, string> CreateBudgetAuditMetadata(
        string operation,
        BudgetPolicyRecord record)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["operation"] = operation,
            ["budget_policy_id"] = record.BudgetPolicyId,
            ["budget_scope_kind"] = ToWireBudgetScopeKind(record.ScopeKind),
            ["budget_metric_kind"] = ToWireBudgetMetricKind(record.MetricKind),
            ["budget_policy_status"] = ToWireBudgetPolicyStatus(record.Status),
            ["result"] = operation.EndsWith("created", StringComparison.Ordinal) ? "created" : "updated"
        };
    }

    public static string ToWireBudgetScopeKind(BudgetPolicyScopeKind scopeKind)
    {
        return scopeKind switch
        {
            BudgetPolicyScopeKind.CustomerOrganization => "customer_organization",
            BudgetPolicyScopeKind.Team => "team",
            BudgetPolicyScopeKind.Repository => "repository",
            BudgetPolicyScopeKind.Workflow => "workflow",
            BudgetPolicyScopeKind.Harness => "harness",
            BudgetPolicyScopeKind.Model => "model",
            _ => throw new ArgumentOutOfRangeException(nameof(scopeKind), scopeKind, null)
        };
    }

    public static string ToWireBudgetMetricKind(BudgetMetricKind metricKind)
    {
        return metricKind switch
        {
            BudgetMetricKind.Tokens => "tokens",
            BudgetMetricKind.EstimatedCost => "estimated_cost",
            BudgetMetricKind.CacheMissRate => "cache_miss_rate",
            BudgetMetricKind.ErrorRework => "error_rework",
            _ => throw new ArgumentOutOfRangeException(nameof(metricKind), metricKind, null)
        };
    }

    public static string ToWireBudgetPolicyStatus(BudgetPolicyStatus status)
    {
        return status switch
        {
            BudgetPolicyStatus.Active => "active",
            BudgetPolicyStatus.Disabled => "disabled",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
    }

    public static BudgetPolicyScopeKind ParseBudgetScopeKind(string scopeKind)
    {
        return NormalizeRequiredText(scopeKind, nameof(scopeKind)) switch
        {
            "customer_organization" => BudgetPolicyScopeKind.CustomerOrganization,
            "team" => BudgetPolicyScopeKind.Team,
            "repository" => BudgetPolicyScopeKind.Repository,
            "workflow" => BudgetPolicyScopeKind.Workflow,
            "harness" => BudgetPolicyScopeKind.Harness,
            "model" => BudgetPolicyScopeKind.Model,
            _ => throw new ArgumentException("Budget policy scope kind is not supported.", nameof(scopeKind))
        };
    }

    public static BudgetMetricKind ParseBudgetMetricKind(string metricKind)
    {
        return NormalizeRequiredText(metricKind, nameof(metricKind)) switch
        {
            "tokens" => BudgetMetricKind.Tokens,
            "estimated_cost" => BudgetMetricKind.EstimatedCost,
            "cache_miss_rate" => BudgetMetricKind.CacheMissRate,
            "error_rework" => BudgetMetricKind.ErrorRework,
            _ => throw new ArgumentException("Budget metric kind is not supported.", nameof(metricKind))
        };
    }

    public static BudgetPolicyStatus ParseBudgetPolicyStatus(string status)
    {
        return NormalizeRequiredText(status, nameof(status)) switch
        {
            "active" => BudgetPolicyStatus.Active,
            "disabled" => BudgetPolicyStatus.Disabled,
            _ => throw new ArgumentException("Budget policy status is not supported.", nameof(status))
        };
    }

    private readonly record struct ProductUserLookupKey(
        CustomerOrganizationId CustomerOrganizationId,
        IdentityTenantId IdentityTenantId,
        string ExternalSubjectId);

    private readonly record struct GovernanceAuditEventKey(
        CustomerOrganizationId CustomerOrganizationId,
        string AuditEventId);

    private readonly record struct TelemetryEnvelopeDedupeLookupKey(
        CustomerOrganizationId CustomerOrganizationId,
        string DedupeKeyHash);

    private readonly record struct AgentSessionLookupKey(
        CustomerOrganizationId CustomerOrganizationId,
        string HarnessSetupProfileId,
        ProductUserId ProductUserId,
        string ProviderSessionKey);

    private readonly record struct ProductApiIdempotencyKey(
        CustomerOrganizationId CustomerOrganizationId,
        ProductUserId ProductUserId,
        string Route,
        string IdempotencyKey);

    private readonly record struct TokenHotspotDetectionKey(
        CustomerOrganizationId CustomerOrganizationId,
        string DetectionKey);

    private readonly record struct RecommendationGenerationKey(
        CustomerOrganizationId CustomerOrganizationId,
        string GenerationKey);

    private readonly record struct RecommendationModelPolicyKey(
        CustomerOrganizationId CustomerOrganizationId,
        string PolicyVersionId);

    private readonly record struct RecommendationPromptTemplateKey(
        CustomerOrganizationId CustomerOrganizationId,
        string PromptTemplateVersion);

    private sealed record NormalizedPricingBasisRequest(
        string Harness,
        string ProviderName,
        string ModelName,
        string BillingRoute,
        string Currency,
        string PricingVersion,
        string AuditEventId,
        IReadOnlyDictionary<string, string> SourceMetadata);

    private sealed record NormalizedCostEstimateRequest(
        string AgentSessionId,
        string? ModelInvocationId,
        string? PricingBasisId,
        string? PricingVersion,
        string Currency,
        string ProviderName,
        string ModelName,
        string BillingRoute);

    private sealed record NormalizedBudgetPolicyRequest(
        BudgetPolicyScopeKind ScopeKind,
        string? ScopeId,
        BudgetMetricKind MetricKind,
        string ThresholdJson,
        BudgetPolicyStatus Status);
}

internal sealed record SeedProductRoleMappingRequest(
    IdentityTenantId IdentityTenantId,
    ExternalPrincipalType ExternalPrincipalType,
    string ExternalPrincipalId,
    ProductRole ProductRole,
    ProductScopeKind ScopeKind,
    string? ScopeId,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    ProductUserId ChangedByProductUserId,
    ProductRole ActorEffectiveRole,
    string CorrelationId,
    string AuditEventId);

public sealed record CreateGovernanceAuditEventRequest(
    string AuditEventId,
    ProductUserId? ActorProductUserId,
    ProductRole? EffectiveRole,
    ProductAuthorizationAction Action,
    ProductScope TargetScope,
    string Decision,
    ProductAuthorizationDenialReason? DenialReason,
    string CorrelationId,
    IReadOnlyDictionary<string, string> EvidenceMetadata);

public sealed record EnsureCustomerOrganizationLoadedRequest(
    CustomerOrganizationId CustomerOrganizationId,
    string Slug,
    string DisplayName,
    string DataResidencyRegion,
    CustomerOrganizationIsolationTier IsolationTier);

public sealed record ProductApiIdempotencyRecord(
    CustomerOrganizationId CustomerOrganizationId,
    ProductUserId ProductUserId,
    string Route,
    string IdempotencyKey,
    string RequestHash,
    string? OperationId,
    int? ResponseStatusCode,
    string? ResponseLocation,
    string? ResponseJson,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? CompletedAtUtc)
{
    public bool IsCompleted => CompletedAtUtc is not null &&
        OperationId is not null &&
        ResponseStatusCode is not null &&
        ResponseJson is not null;
}

public sealed record ProductApiIdempotencyReservation(
    ProductApiIdempotencyReservationState State,
    ProductApiIdempotencyRecord? Record);

public enum ProductApiIdempotencyReservationState
{
    Reserved,
    Replay,
    Conflict,
    InProgress
}

public sealed record ReserveProductApiIdempotencyRecordRequest(
    CustomerOrganizationId CustomerOrganizationId,
    ProductUserId ProductUserId,
    string Route,
    string IdempotencyKey,
    string RequestHash,
    DateTimeOffset ExpiresAtUtc);

public sealed record CompleteProductApiIdempotencyRecordRequest(
    CustomerOrganizationId CustomerOrganizationId,
    ProductUserId ProductUserId,
    string Route,
    string IdempotencyKey,
    string RequestHash,
    string OperationId,
    int ResponseStatusCode,
    string? ResponseLocation,
    string ResponseJson,
    DateTimeOffset ExpiresAtUtc);
