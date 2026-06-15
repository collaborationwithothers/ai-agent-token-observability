using TokenObservability.Domain.Authorization;
using TokenObservability.Domain.Tenancy;

namespace TokenObservability.Infrastructure.Persistence;

public sealed class InMemoryTenantMetadataStore(ITenantMetadataClock clock)
{
    private readonly Dictionary<CustomerOrganizationId, CustomerOrganization> customerOrganizations = [];
    private readonly Dictionary<string, CustomerOrganizationId> organizationSlugIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<IdentityTenantId, IdentityTenant> identityTenants = [];
    private readonly Dictionary<ProductUserId, ProductUser> productUsers = [];
    private readonly Dictionary<ProductUserLookupKey, ProductUserId> productUserExternalSubjectIndex = [];
    private readonly Dictionary<ProductRoleMappingId, ProductRoleMapping> productRoleMappings = [];
    private readonly Dictionary<GovernanceAuditEventKey, GovernanceAuditEvent> governanceAuditEvents = [];
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

    public Task<ProductAuthorizationDecision> AuthorizeProductActionAsync(
        CustomerOrganizationId customerOrganizationId,
        IdentityTenantId identityTenantId,
        AuthenticatedTokenClaims claims,
        ProductAuthorizationAction action,
        ProductScope requestedScope)
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
                    ProductAuthorizationDenialReason.InvalidTenant);

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
                    ProductAuthorizationDenialReason.InvalidTenant);

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
                denialReason);

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

    private static string NormalizeRequiredText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        return value.Trim();
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
        var targetResourceId = string.IsNullOrWhiteSpace(requestedScope.ScopeId)
            ? customerOrganizationId.ToString()
            : requestedScope.ScopeId.Trim();
        var now = clock.UtcNow.ToUniversalTime();

        var auditEvent = new GovernanceAuditEvent(
            auditEventId,
            customerOrganizationId,
            actorProductUserId,
            effectiveRole,
            action,
            TargetResourceKind: requestedScope.Kind.ToString(),
            TargetResourceId: targetResourceId,
            Decision: "denied",
            DenialReason: denialReason,
            CorrelationId: normalizedCorrelationId,
            now);

        governanceAuditEvents.Add(new GovernanceAuditEventKey(customerOrganizationId, auditEvent.AuditEventId), auditEvent);
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

        var auditEvent = new GovernanceAuditEvent(
            auditEventId,
            customerOrganizationId,
            changedByProductUserId,
            actorEffectiveRole,
            ProductAuthorizationAction.IdentityManage,
            TargetResourceKind: "product_role_mapping",
            TargetResourceId: mapping.ProductRoleMappingId.ToString(),
            Decision: "created",
            DenialReason: null,
            CorrelationId: correlationId,
            createdAtUtc);

        governanceAuditEvents.Add(new GovernanceAuditEventKey(customerOrganizationId, auditEvent.AuditEventId), auditEvent);
        productRoleMappings.Add(mapping.ProductRoleMappingId, mapping);

        return mapping;
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
