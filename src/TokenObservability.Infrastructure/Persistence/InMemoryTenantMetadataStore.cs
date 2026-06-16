using System.Collections.ObjectModel;
using TokenObservability.Domain.Authorization;
using TokenObservability.Domain.Ingestion;
using TokenObservability.Domain.Tenancy;

namespace TokenObservability.Infrastructure.Persistence;

public sealed class InMemoryTenantMetadataStore(ITenantMetadataClock clock)
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
        "scope_kind"
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
    private readonly Dictionary<TelemetryEnvelopeId, TelemetryEnvelopeRecord> telemetryEnvelopes = [];
    private readonly Dictionary<AgentSessionId, AgentSessionRecord> agentSessions = [];
    private readonly Dictionary<TokenObservationId, TokenObservationRecord> tokenObservations = [];
    private readonly object gate = new();

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

    public Task<TelemetryEnvelopeRecord> RecordTelemetryEnvelopeAsync(
        CreateTelemetryEnvelopeRecordRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var harnessSetupProfileId = NormalizeHarnessSetupProfileId(
            request.HarnessSetupProfileId,
            nameof(request.HarnessSetupProfileId));
        var schemaVersion = NormalizeRequiredText(request.SchemaVersion, nameof(request.SchemaVersion));
        var signalType = NormalizeSignalType(request.SignalType, nameof(request.SignalType));
        var sourceEventName = string.IsNullOrWhiteSpace(request.SourceEventName)
            ? null
            : NormalizeSafeOptionalText(request.SourceEventName, nameof(request.SourceEventName));
        var conversationIdHash = string.IsNullOrWhiteSpace(request.ConversationIdHash)
            ? null
            : NormalizeSafeOptionalText(request.ConversationIdHash, nameof(request.ConversationIdHash));
        var modelName = string.IsNullOrWhiteSpace(request.ModelName)
            ? null
            : NormalizeSafeOptionalText(request.ModelName, nameof(request.ModelName));
        var contentPolicyDecision = NormalizeContentPolicyDecision(request.ContentPolicyDecision);
        var routingDecision = NormalizeRequiredText(request.RoutingDecision, nameof(request.RoutingDecision));
        var dedupeKeyHash = NormalizeRequiredText(request.DedupeKeyHash, nameof(request.DedupeKeyHash));
        var now = clock.UtcNow.ToUniversalTime();

        ValidateMetricQuality(request.MetricStatus, request.MetricConfidence);

        lock (gate)
        {
            RequireCustomerOrganization(request.CustomerOrganizationId);
            RequireProductUserForCustomerOrganization(request.CustomerOrganizationId, request.ProductUserId);
            var credential = RequireScopedIngestionCredentialForCustomerOrganization(
                request.CustomerOrganizationId,
                request.ScopedIngestionCredentialId);

            var existingEnvelope = telemetryEnvelopes.Values.SingleOrDefault(envelope =>
                envelope.CustomerOrganizationId == request.CustomerOrganizationId &&
                StringComparer.Ordinal.Equals(envelope.DedupeKeyHash, dedupeKeyHash));

            if (existingEnvelope is not null)
            {
                if (!StringComparer.Ordinal.Equals(existingEnvelope.HarnessSetupProfileId, harnessSetupProfileId) ||
                    existingEnvelope.ScopedIngestionCredentialId != request.ScopedIngestionCredentialId ||
                    existingEnvelope.ProductUserId != request.ProductUserId ||
                    existingEnvelope.Harness != request.Harness ||
                    !StringComparer.Ordinal.Equals(existingEnvelope.SignalType, signalType))
                {
                    throw new InvalidOperationException("Telemetry envelope dedupe key conflicts with a different accepted telemetry context.");
                }

                return Task.FromResult(existingEnvelope);
            }

            if (!StringComparer.Ordinal.Equals(credential.HarnessSetupProfileId, harnessSetupProfileId) ||
                credential.ProductUserId != request.ProductUserId ||
                credential.AllowedHarness != request.Harness)
            {
                throw new InvalidOperationException("Telemetry envelope credential context does not match the request.");
            }

            var envelope = new TelemetryEnvelopeRecord(
                TelemetryEnvelopeId.NewId(),
                request.CustomerOrganizationId,
                harnessSetupProfileId,
                request.ScopedIngestionCredentialId,
                request.ProductUserId,
                request.Harness,
                schemaVersion,
                signalType,
                sourceEventName,
                request.SourceEventTimestampUtc?.ToUniversalTime(),
                now,
                conversationIdHash,
                modelName,
                contentPolicyDecision,
                routingDecision,
                request.MetricStatus,
                request.MetricConfidence,
                dedupeKeyHash);

            telemetryEnvelopes.Add(envelope.TelemetryEnvelopeId, envelope);

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
                    .ThenBy(envelope => envelope.TelemetryEnvelopeId.ToString(), StringComparer.Ordinal)
                    .ToArray());
        }
    }

    public Task<AgentSessionRecord> UpsertAgentSessionAsync(
        CreateAgentSessionRecordRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var harnessSetupProfileId = NormalizeHarnessSetupProfileId(
            request.HarnessSetupProfileId,
            nameof(request.HarnessSetupProfileId));
        var providerSessionIdHash = string.IsNullOrWhiteSpace(request.ProviderSessionIdHash)
            ? null
            : NormalizeSafeOptionalText(request.ProviderSessionIdHash, nameof(request.ProviderSessionIdHash));
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

            var existingSession = agentSessions.Values.SingleOrDefault(session =>
                session.CustomerOrganizationId == request.CustomerOrganizationId &&
                StringComparer.Ordinal.Equals(session.ProviderSessionIdHash, providerSessionIdHash) &&
                StringComparer.Ordinal.Equals(session.HarnessSetupProfileId, harnessSetupProfileId) &&
                session.ProductUserId == request.ProductUserId);

            if (existingSession is not null)
            {
                var updated = existingSession with
                {
                    StartedAtUtc = MinDateTimeOffset(existingSession.StartedAtUtc, startedAtUtc),
                    EndedAtUtc = MaxDateTimeOffset(existingSession.EndedAtUtc, endedAtUtc),
                    SessionStatus = request.SessionStatus,
                    RepositoryEvidenceState = request.RepositoryEvidenceState,
                    ContentCaptureSummary = request.ContentCaptureSummary,
                    RecommendationStatus = request.RecommendationStatus,
                    TokenMetricStatus = request.TokenMetricStatus,
                    TokenMetricConfidence = request.TokenMetricConfidence,
                    UpdatedAtUtc = now
                };

                agentSessions[updated.AgentSessionId] = updated;

                return Task.FromResult(updated);
            }

            var session = new AgentSessionRecord(
                AgentSessionId.NewId(),
                request.CustomerOrganizationId,
                request.ProductUserId,
                harnessSetupProfileId,
                request.Harness,
                providerSessionIdHash,
                startedAtUtc,
                endedAtUtc,
                request.SessionStatus,
                request.RepositoryEvidenceState,
                request.ContentCaptureSummary,
                request.RecommendationStatus,
                request.TokenMetricStatus,
                request.TokenMetricConfidence,
                now,
                now);

            agentSessions.Add(session.AgentSessionId, session);

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
                    .OrderByDescending(session => session.StartedAtUtc ?? session.CreatedAtUtc)
                    .ThenBy(session => session.AgentSessionId.ToString(), StringComparer.Ordinal)
                    .ToArray());
        }
    }

    public Task<TokenObservationRecord> RecordTokenObservationAsync(
        CreateTokenObservationRecordRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var modelInvocationId = string.IsNullOrWhiteSpace(request.ModelInvocationId)
            ? null
            : NormalizeSafeOptionalText(request.ModelInvocationId, nameof(request.ModelInvocationId));
        var now = clock.UtcNow.ToUniversalTime();

        ValidateTokenObservationShape(request.Value, request.MetricStatus, request.MetricConfidence, request.SourceKind);

        lock (gate)
        {
            RequireCustomerOrganization(request.CustomerOrganizationId);

            if (!agentSessions.TryGetValue(request.AgentSessionId, out var session) ||
                session.CustomerOrganizationId != request.CustomerOrganizationId)
            {
                throw new InvalidOperationException("Token observation session does not belong to the customer organization.");
            }

            if (request.SourceTelemetryEnvelopeId is { } sourceTelemetryEnvelopeId &&
                (!telemetryEnvelopes.TryGetValue(sourceTelemetryEnvelopeId, out var envelope) ||
                    envelope.CustomerOrganizationId != request.CustomerOrganizationId))
            {
                throw new InvalidOperationException("Token observation source envelope does not belong to the customer organization.");
            }

            var observation = new TokenObservationRecord(
                TokenObservationId.NewId(),
                request.CustomerOrganizationId,
                request.AgentSessionId,
                modelInvocationId,
                request.MetricName,
                request.Value,
                request.MetricStatus,
                request.MetricConfidence,
                request.SourceKind,
                request.SourceTelemetryEnvelopeId,
                now);

            tokenObservations.Add(observation.TokenObservationId, observation);

            return Task.FromResult(observation);
        }
    }

    public Task<IReadOnlyList<TokenObservationRecord>> ListTokenObservationsAsync(
        CustomerOrganizationId customerOrganizationId,
        AgentSessionId agentSessionId)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<TokenObservationRecord>>(
                tokenObservations.Values
                    .Where(observation =>
                        observation.CustomerOrganizationId == customerOrganizationId &&
                        observation.AgentSessionId == agentSessionId)
                    .OrderBy(observation => TokenMetricSortOrder(observation.MetricName))
                    .ThenBy(observation => observation.CreatedAtUtc)
                    .ThenBy(observation => observation.TokenObservationId.ToString(), StringComparer.Ordinal)
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

    private static string NormalizeSafeOptionalText(string value, string parameterName)
    {
        var normalized = NormalizeRequiredText(value, parameterName);

        return IsSafeResourceId(normalized) || IsSafeMachineToken(normalized)
            ? normalized
            : throw new ArgumentException("The value contains unsupported metadata characters.", parameterName);
    }

    private static string NormalizeSignalType(string value, string parameterName)
    {
        var normalized = NormalizeRequiredText(value, parameterName).ToLowerInvariant();

        return normalized is "logs" or "traces" or "metrics"
            ? normalized
            : throw new ArgumentException("Telemetry signal type is not supported.", parameterName);
    }

    private static string NormalizeContentPolicyDecision(string value)
    {
        var normalized = NormalizeRequiredText(value, nameof(value)).ToLowerInvariant();

        return normalized is "metadata_only" or "capture_candidate" or "blocked" or "redaction_required"
            ? normalized
            : throw new ArgumentException("Content policy decision is not supported.", nameof(value));
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

    private readonly record struct ProductUserLookupKey(
        CustomerOrganizationId CustomerOrganizationId,
        IdentityTenantId IdentityTenantId,
        string ExternalSubjectId);

    private readonly record struct GovernanceAuditEventKey(
        CustomerOrganizationId CustomerOrganizationId,
        string AuditEventId);
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
