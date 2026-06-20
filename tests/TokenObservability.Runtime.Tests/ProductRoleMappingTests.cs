using TokenObservability.Domain.Authorization;
using TokenObservability.Domain.Tenancy;
using TokenObservability.Infrastructure.Persistence;

namespace TokenObservability.Runtime.Tests;

public sealed class ProductRoleMappingTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 14, 21, 15, 0, TimeSpan.Zero);

    [Fact]
    public async Task PlatformAdminClaimsCanCreateGroupMappingAndAuditIt()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "internal", "contoso-tenant");
        var adminClaims = CreateClaims(subject: "admin-subject", groupObjectIds: ["entra-admin-group"]);
        var admin = await SeedPlatformAdminAsync(store, seed, adminClaims);

        var mapping = await store.CreateProductRoleMappingAsync(
            seed.Organization.CustomerOrganizationId,
            new CreateProductRoleMappingRequest(
                IdentityTenantId: seed.IdentityTenant.IdentityTenantId,
                ExternalPrincipalType: ExternalPrincipalType.GroupObjectId,
                ExternalPrincipalId: "entra-lead-group",
                ProductRole: ProductRole.EngineeringLead,
                ScopeKind: ProductScopeKind.Repository,
                ScopeId: "repo-001",
                EffectiveFromUtc: Now,
                EffectiveToUtc: null,
                ChangedByClaims: adminClaims,
                CorrelationId: "authz-create-001",
                AuditEventId: "audit-role-map-001"));

        var decision = await store.AuthorizeProductActionAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            CreateClaims(subject: "lead-subject", groupObjectIds: ["entra-lead-group"]),
            ProductAuthorizationAction.SessionReadScoped,
            new ProductScope(ProductScopeKind.Repository, ScopeId: "repo-001"));

        Assert.True(decision.IsAllowed);
        Assert.Equal(ProductAuthorizationDenialReason.None, decision.DenialReason);
        Assert.Contains(ProductRole.EngineeringLead, decision.EffectiveRoles);
        Assert.Equal(mapping.ProductRoleMappingId, decision.MatchedMappings.Single().ProductRoleMappingId);
        Assert.Equal(admin.ProductUserId, mapping.ChangedByProductUserId);

        var auditEvent = await store.FindGovernanceAuditEventAsync(
            seed.Organization.CustomerOrganizationId,
            mapping.AuditEventId);

        Assert.NotNull(auditEvent);
        Assert.Equal(admin.ProductUserId, auditEvent.ActorProductUserId);
        Assert.Equal(ProductRole.PlatformAdmin, auditEvent.EffectiveRole);
        Assert.Equal(ProductAuthorizationAction.IdentityManage, auditEvent.Action);
        Assert.Equal("product_role_mapping", auditEvent.TargetResourceKind);
        Assert.Equal(mapping.ProductRoleMappingId.ToString(), auditEvent.TargetResourceId);
        Assert.Equal("created", auditEvent.Decision);
        Assert.Null(auditEvent.DenialReason);
        Assert.Equal("authz-create-001", auditEvent.CorrelationId);
        Assert.Equal("admin_operation", auditEvent.EvidenceMetadata["evidence_kind"]);
    }

    [Fact]
    public async Task DeveloperClaimsCannotCreateRoleMapping()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "internal", "contoso-tenant");
        var developerClaims = CreateClaims(subject: "developer-subject", groupObjectIds: ["entra-developer-group"]);
        var developer = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            developerClaims);

        await SeedRoleMappingAsync(
            store,
            seed,
            developer.ProductUserId,
            ExternalPrincipalType.GroupObjectId,
            "entra-developer-group",
            ProductRole.Developer,
            ProductScopeKind.Organization,
            scopeId: null,
            auditEventId: "audit-seed-developer-001");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.CreateProductRoleMappingAsync(
            seed.Organization.CustomerOrganizationId,
            new CreateProductRoleMappingRequest(
                IdentityTenantId: seed.IdentityTenant.IdentityTenantId,
                ExternalPrincipalType: ExternalPrincipalType.GroupObjectId,
                ExternalPrincipalId: "entra-lead-group",
                ProductRole: ProductRole.EngineeringLead,
                ScopeKind: ProductScopeKind.Repository,
                ScopeId: "repo-001",
                EffectiveFromUtc: Now,
                EffectiveToUtc: null,
                ChangedByClaims: developerClaims,
                CorrelationId: "authz-create-denied-001",
                AuditEventId: "audit-role-map-developer-denied-001")));

        var auditEvents = await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId);
        var auditEvent = Assert.Single(auditEvents, auditEvent => auditEvent.CorrelationId == "authz-create-denied-001");
        Assert.Equal("denied", auditEvent.Decision);
        Assert.Equal(ProductAuthorizationDenialReason.InsufficientRole, auditEvent.DenialReason);
        Assert.Equal(developer.ProductUserId, auditEvent.ActorProductUserId);
        Assert.Equal(ProductRole.Developer, auditEvent.EffectiveRole);
        Assert.Equal(ProductAuthorizationAction.IdentityManage, auditEvent.Action);
    }

    [Fact]
    public async Task GovernanceAuditWritePathRecordsSuccessfulAdministrativeOperationWithEvidenceMetadata()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "internal", "contoso-tenant");
        var adminClaims = CreateClaims(subject: "admin-subject", groupObjectIds: ["entra-admin-group"]);
        var admin = await SeedPlatformAdminAsync(store, seed, adminClaims);

        var auditEvent = await store.RecordGovernanceAuditEventAsync(
            seed.Organization.CustomerOrganizationId,
            new CreateGovernanceAuditEventRequest(
                AuditEventId: "audit-admin-operation-001",
                ActorProductUserId: admin.ProductUserId,
                EffectiveRole: ProductRole.PlatformAdmin,
                Action: ProductAuthorizationAction.TenantUpdate,
                TargetScope: new ProductScope(ProductScopeKind.Organization, ScopeId: null),
                Decision: "updated",
                DenialReason: null,
                CorrelationId: "tenant-update-001",
                EvidenceMetadata: new Dictionary<string, string>
                {
                    ["evidence_kind"] = "admin_operation",
                    ["request_route"] = "/api/v1/customer-organization",
                    ["operation"] = "tenant_settings_update"
                }));

        var found = await store.FindGovernanceAuditEventAsync(
            seed.Organization.CustomerOrganizationId,
            "audit-admin-operation-001");

        Assert.Equal(auditEvent, found);
        Assert.Equal(admin.ProductUserId, auditEvent.ActorProductUserId);
        Assert.Equal(ProductAuthorizationAction.TenantUpdate, auditEvent.Action);
        Assert.Equal("updated", auditEvent.Decision);
        Assert.Equal("admin_operation", auditEvent.EvidenceMetadata["evidence_kind"]);
        Assert.Equal("/api/v1/customer-organization", auditEvent.EvidenceMetadata["request_route"]);
        Assert.Equal("tenant_settings_update", auditEvent.EvidenceMetadata["operation"]);
        var dictionary = Assert.IsAssignableFrom<IDictionary<string, string>>(auditEvent.EvidenceMetadata);
        Assert.Throws<NotSupportedException>(() => dictionary["operation"] = "tampered_operation");
    }

    [Fact]
    public async Task GovernanceAuditWritePathRejectsCapturedContentEvidenceMetadata()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "internal", "contoso-tenant");
        var adminClaims = CreateClaims(subject: "admin-subject", groupObjectIds: ["entra-admin-group"]);
        var admin = await SeedPlatformAdminAsync(store, seed, adminClaims);

        await Assert.ThrowsAsync<ArgumentException>(() => store.RecordGovernanceAuditEventAsync(
            seed.Organization.CustomerOrganizationId,
            new CreateGovernanceAuditEventRequest(
                AuditEventId: "audit-raw-prompt-001",
                ActorProductUserId: admin.ProductUserId,
                EffectiveRole: ProductRole.PlatformAdmin,
                Action: ProductAuthorizationAction.ContentReviewDecide,
                TargetScope: new ProductScope(ProductScopeKind.ContentReviewQueue, ScopeId: "queue-001"),
                Decision: "updated",
                DenialReason: null,
                CorrelationId: "raw-prompt-rejected-001",
                EvidenceMetadata: new Dictionary<string, string>
                {
                    ["raw_prompt_text"] = "Please inspect this secret code fragment."
                })));
    }

    [Fact]
    public async Task GovernanceAuditWritePathRejectsSensitiveEvidenceMetadataValues()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "internal", "contoso-tenant");
        var adminClaims = CreateClaims(subject: "admin-subject", groupObjectIds: ["entra-admin-group"]);
        var admin = await SeedPlatformAdminAsync(store, seed, adminClaims);

        await Assert.ThrowsAsync<ArgumentException>(() => store.RecordGovernanceAuditEventAsync(
            seed.Organization.CustomerOrganizationId,
            new CreateGovernanceAuditEventRequest(
                AuditEventId: "audit-sensitive-value-001",
                ActorProductUserId: admin.ProductUserId,
                EffectiveRole: ProductRole.PlatformAdmin,
                Action: ProductAuthorizationAction.TenantUpdate,
                TargetScope: new ProductScope(ProductScopeKind.Organization, ScopeId: null),
                Decision: "updated",
                DenialReason: null,
                CorrelationId: "sensitive-value-rejected-001",
                EvidenceMetadata: new Dictionary<string, string>
                {
                    ["operation"] = "Bearer sk-test-token-that-must-not-be-stored"
                })));
    }

    [Fact]
    public async Task GovernanceAuditWritePathRejectsEmptyEvidenceMetadata()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "internal", "contoso-tenant");
        var adminClaims = CreateClaims(subject: "admin-subject", groupObjectIds: ["entra-admin-group"]);
        var admin = await SeedPlatformAdminAsync(store, seed, adminClaims);

        await Assert.ThrowsAsync<ArgumentException>(() => store.RecordGovernanceAuditEventAsync(
            seed.Organization.CustomerOrganizationId,
            new CreateGovernanceAuditEventRequest(
                AuditEventId: "audit-empty-evidence-001",
                ActorProductUserId: admin.ProductUserId,
                EffectiveRole: ProductRole.PlatformAdmin,
                Action: ProductAuthorizationAction.TenantUpdate,
                TargetScope: new ProductScope(ProductScopeKind.Organization, ScopeId: null),
                Decision: "updated",
                DenialReason: null,
                CorrelationId: "empty-evidence-rejected-001",
                EvidenceMetadata: new Dictionary<string, string>())));
    }

    [Fact]
    public async Task SecurityReviewerCanUseContentReviewActionsAndPlatformAdminCannotByDefault()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "internal", "contoso-tenant");
        var adminClaims = CreateClaims(subject: "admin-subject", groupObjectIds: ["entra-admin-group"]);
        var reviewerClaims = CreateClaims(subject: "reviewer-subject", groupObjectIds: ["entra-reviewer-group"]);
        var admin = await SeedPlatformAdminAsync(store, seed, adminClaims);

        await SeedRoleMappingAsync(
            store,
            seed,
            admin.ProductUserId,
            ExternalPrincipalType.GroupObjectId,
            "entra-reviewer-group",
            ProductRole.SecurityReviewer,
            ProductScopeKind.ContentReviewQueue,
            scopeId: "queue-001",
            auditEventId: "audit-seed-reviewer-001");

        var adminDecision = await store.AuthorizeProductActionAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            adminClaims,
            ProductAuthorizationAction.ContentReviewDecide,
            new ProductScope(ProductScopeKind.ContentReviewQueue, ScopeId: "queue-001"));

        var reviewerDecision = await store.AuthorizeProductActionAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            reviewerClaims,
            ProductAuthorizationAction.ContentReviewDecide,
            new ProductScope(ProductScopeKind.ContentReviewQueue, ScopeId: "queue-001"));

        Assert.False(adminDecision.IsAllowed);
        Assert.Equal(ProductAuthorizationDenialReason.InsufficientRole, adminDecision.DenialReason);
        Assert.True(reviewerDecision.IsAllowed);
        Assert.Contains(ProductRole.SecurityReviewer, reviewerDecision.EffectiveRoles);
    }

    [Fact]
    public async Task LowerPrivilegeRoleIsExplicitlyDeniedForAdminAction()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "internal", "contoso-tenant");
        var developerClaims = CreateClaims(subject: "developer-subject", groupObjectIds: ["entra-developer-group"]);
        var developer = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            developerClaims);

        await SeedRoleMappingAsync(
            store,
            seed,
            developer.ProductUserId,
            ExternalPrincipalType.GroupObjectId,
            "entra-developer-group",
            ProductRole.Developer,
            ProductScopeKind.Organization,
            scopeId: null,
            auditEventId: "audit-role-map-002");

        var decision = await store.AuthorizeProductActionAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            developerClaims,
            ProductAuthorizationAction.IdentityManage,
            new ProductScope(ProductScopeKind.Organization, ScopeId: null));

        Assert.False(decision.IsAllowed);
        Assert.Equal(ProductAuthorizationDenialReason.InsufficientRole, decision.DenialReason);
        Assert.Contains(ProductRole.Developer, decision.EffectiveRoles);

        var auditEvents = await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId);
        var denialAuditEvent = Assert.Single(auditEvents, auditEvent => auditEvent.DenialReason == ProductAuthorizationDenialReason.InsufficientRole);
        Assert.Equal("denied", denialAuditEvent.Decision);
        Assert.Equal(developer.ProductUserId, denialAuditEvent.ActorProductUserId);
        Assert.Equal(ProductRole.Developer, denialAuditEvent.EffectiveRole);
        Assert.Equal(ProductAuthorizationAction.IdentityManage, denialAuditEvent.Action);
    }

    [Fact]
    public async Task DeveloperSelfScopeAllowsOwnSessionRead()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "internal", "contoso-tenant");
        var developerClaims = CreateClaims(subject: "developer-subject", groupObjectIds: ["entra-developer-group"]);
        var developer = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            developerClaims);

        await SeedRoleMappingAsync(
            store,
            seed,
            developer.ProductUserId,
            ExternalPrincipalType.GroupObjectId,
            "entra-developer-group",
            ProductRole.Developer,
            ProductScopeKind.Self,
            scopeId: null,
            auditEventId: "audit-role-map-self-001");

        var decision = await store.AuthorizeProductActionAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            developerClaims,
            ProductAuthorizationAction.SessionReadOwn,
            new ProductScope(ProductScopeKind.Self, ScopeId: null));

        Assert.True(decision.IsAllowed);
        Assert.Contains(ProductRole.Developer, decision.EffectiveRoles);
    }

    [Fact]
    public async Task OrganizationAndSelfScopesRejectConcreteScopeIds()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "internal", "contoso-tenant");
        var adminClaims = CreateClaims(subject: "admin-subject", groupObjectIds: ["entra-admin-group"]);
        await SeedPlatformAdminAsync(store, seed, adminClaims);

        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateProductRoleMappingAsync(
            seed.Organization.CustomerOrganizationId,
            new CreateProductRoleMappingRequest(
                IdentityTenantId: seed.IdentityTenant.IdentityTenantId,
                ExternalPrincipalType: ExternalPrincipalType.GroupObjectId,
                ExternalPrincipalId: "entra-admin-group",
                ProductRole: ProductRole.PlatformAdmin,
                ScopeKind: ProductScopeKind.Organization,
                ScopeId: "ignored-by-authorization",
                EffectiveFromUtc: Now,
                EffectiveToUtc: null,
                ChangedByClaims: adminClaims,
                CorrelationId: "authz-invalid-org-scope",
                AuditEventId: "audit-role-map-org-001")));

        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateProductRoleMappingAsync(
            seed.Organization.CustomerOrganizationId,
            new CreateProductRoleMappingRequest(
                IdentityTenantId: seed.IdentityTenant.IdentityTenantId,
                ExternalPrincipalType: ExternalPrincipalType.GroupObjectId,
                ExternalPrincipalId: "entra-admin-group",
                ProductRole: ProductRole.Developer,
                ScopeKind: ProductScopeKind.Self,
                ScopeId: "developer-subject",
                EffectiveFromUtc: Now,
                EffectiveToUtc: null,
                ChangedByClaims: adminClaims,
                CorrelationId: "authz-invalid-self-scope",
                AuditEventId: "audit-role-map-self-002")));
    }

    [Fact]
    public async Task MissingGroupMappingFailsClosed()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "internal", "contoso-tenant");

        var decision = await store.AuthorizeProductActionAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            CreateClaims(subject: "unknown-subject", groupObjectIds: ["unmapped-group"]),
            ProductAuthorizationAction.IdentityManage,
            new ProductScope(ProductScopeKind.Organization, ScopeId: null));

        Assert.False(decision.IsAllowed);
        Assert.Equal(ProductAuthorizationDenialReason.MissingRoleMapping, decision.DenialReason);
        Assert.Empty(decision.EffectiveRoles);

        var auditEvents = await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId);
        var auditEvent = Assert.Single(auditEvents);
        Assert.Equal("denied", auditEvent.Decision);
        Assert.Equal(ProductAuthorizationDenialReason.MissingRoleMapping, auditEvent.DenialReason);
        Assert.Equal(ProductAuthorizationAction.IdentityManage, auditEvent.Action);
        Assert.Null(auditEvent.EffectiveRole);
    }

    [Fact]
    public async Task RoleMappingDoesNotCrossTenantBoundary()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var firstTenant = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var secondTenant = await CreateTenantAsync(store, "fabrikam", "fabrikam-tenant");
        var adminClaims = CreateClaims(subject: "admin-subject", groupObjectIds: ["shared-group"]);
        var admin = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            firstTenant.Organization.CustomerOrganizationId,
            firstTenant.IdentityTenant.IdentityTenantId,
            adminClaims);

        await SeedRoleMappingAsync(
            store,
            firstTenant,
            admin.ProductUserId,
            ExternalPrincipalType.GroupObjectId,
            "shared-group",
            ProductRole.PlatformAdmin,
            ProductScopeKind.Organization,
            scopeId: null,
            auditEventId: "audit-role-map-003");

        var decision = await store.AuthorizeProductActionAsync(
            secondTenant.Organization.CustomerOrganizationId,
            secondTenant.IdentityTenant.IdentityTenantId,
            CreateClaims(
                subject: "admin-subject",
                groupObjectIds: ["shared-group"],
                externalTenantId: "fabrikam-tenant"),
            ProductAuthorizationAction.IdentityManage,
            new ProductScope(ProductScopeKind.Organization, ScopeId: null));

        Assert.False(decision.IsAllowed);
        Assert.Equal(ProductAuthorizationDenialReason.MissingRoleMapping, decision.DenialReason);
        Assert.Empty(decision.EffectiveRoles);
    }

    [Fact]
    public async Task SameAuditEventIdCanBeUsedInSeparateTenants()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var firstTenant = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var secondTenant = await CreateTenantAsync(store, "fabrikam", "fabrikam-tenant");
        var firstAdminClaims = CreateClaims(
            subject: "admin-subject",
            groupObjectIds: ["contoso-admin-group"],
            externalTenantId: "contoso-tenant");
        var secondAdminClaims = CreateClaims(
            subject: "admin-subject",
            groupObjectIds: ["fabrikam-admin-group"],
            externalTenantId: "fabrikam-tenant");

        var firstAdmin = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            firstTenant.Organization.CustomerOrganizationId,
            firstTenant.IdentityTenant.IdentityTenantId,
            firstAdminClaims);
        var secondAdmin = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            secondTenant.Organization.CustomerOrganizationId,
            secondTenant.IdentityTenant.IdentityTenantId,
            secondAdminClaims);

        await SeedRoleMappingAsync(
            store,
            firstTenant,
            firstAdmin.ProductUserId,
            ExternalPrincipalType.GroupObjectId,
            "contoso-admin-group",
            ProductRole.PlatformAdmin,
            ProductScopeKind.Organization,
            scopeId: null,
            auditEventId: "shared-audit-event-id");
        await SeedRoleMappingAsync(
            store,
            secondTenant,
            secondAdmin.ProductUserId,
            ExternalPrincipalType.GroupObjectId,
            "fabrikam-admin-group",
            ProductRole.PlatformAdmin,
            ProductScopeKind.Organization,
            scopeId: null,
            auditEventId: "shared-audit-event-id");

        var firstAuditEvent = await store.FindGovernanceAuditEventAsync(
            firstTenant.Organization.CustomerOrganizationId,
            "shared-audit-event-id");
        var secondAuditEvent = await store.FindGovernanceAuditEventAsync(
            secondTenant.Organization.CustomerOrganizationId,
            "shared-audit-event-id");

        Assert.NotNull(firstAuditEvent);
        Assert.NotNull(secondAuditEvent);
        Assert.Equal(firstTenant.Organization.CustomerOrganizationId, firstAuditEvent.CustomerOrganizationId);
        Assert.Equal(secondTenant.Organization.CustomerOrganizationId, secondAuditEvent.CustomerOrganizationId);
    }

    [Fact]
    public async Task ResourceScopedRoleMappingRequiresScopeId()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "internal", "contoso-tenant");
        var adminClaims = CreateClaims(subject: "admin-subject", groupObjectIds: ["entra-admin-group"]);
        await SeedPlatformAdminAsync(store, seed, adminClaims);

        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateProductRoleMappingAsync(
            seed.Organization.CustomerOrganizationId,
            new CreateProductRoleMappingRequest(
                IdentityTenantId: seed.IdentityTenant.IdentityTenantId,
                ExternalPrincipalType: ExternalPrincipalType.GroupObjectId,
                ExternalPrincipalId: "entra-lead-group",
                ProductRole: ProductRole.EngineeringLead,
                ScopeKind: ProductScopeKind.Repository,
                ScopeId: null,
                EffectiveFromUtc: Now,
                EffectiveToUtc: null,
                ChangedByClaims: adminClaims,
                CorrelationId: "authz-invalid-resource-scope",
                AuditEventId: "audit-role-map-004")));
    }

    [Fact]
    public async Task ResourceScopedRoleMappingRequiresMatchingRequestedScope()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "internal", "contoso-tenant");
        var leadClaims = CreateClaims(subject: "lead-subject", groupObjectIds: ["entra-lead-group"]);
        var lead = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            leadClaims);

        await SeedRoleMappingAsync(
            store,
            seed,
            lead.ProductUserId,
            ExternalPrincipalType.GroupObjectId,
            "entra-lead-group",
            ProductRole.EngineeringLead,
            ProductScopeKind.Repository,
            scopeId: "repo-001",
            auditEventId: "audit-role-map-005");

        var missingScopeDecision = await store.AuthorizeProductActionAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            leadClaims,
            ProductAuthorizationAction.SessionReadScoped,
            new ProductScope(ProductScopeKind.Repository, ScopeId: null));

        var wrongScopeDecision = await store.AuthorizeProductActionAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            leadClaims,
            ProductAuthorizationAction.SessionReadScoped,
            new ProductScope(ProductScopeKind.Repository, ScopeId: "repo-999"));

        var matchingScopeDecision = await store.AuthorizeProductActionAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            leadClaims,
            ProductAuthorizationAction.SessionReadScoped,
            new ProductScope(ProductScopeKind.Repository, ScopeId: "repo-001"));

        Assert.False(missingScopeDecision.IsAllowed);
        Assert.Equal(ProductAuthorizationDenialReason.ScopeMismatch, missingScopeDecision.DenialReason);
        Assert.False(wrongScopeDecision.IsAllowed);
        Assert.Equal(ProductAuthorizationDenialReason.ScopeMismatch, wrongScopeDecision.DenialReason);
        Assert.True(matchingScopeDecision.IsAllowed);

        var auditEvents = await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId);
        Assert.Equal(2, auditEvents.Count(auditEvent => auditEvent.DenialReason == ProductAuthorizationDenialReason.ScopeMismatch));
    }

    [Fact]
    public async Task InvalidAudienceFailsClosedBeforeRoleMappingResolution()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "internal", "contoso-tenant");

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            CreateClaims(
                subject: "admin-subject",
                groupObjectIds: ["entra-admin-group"],
                audience: "api://wrong-audience")));

        var decision = await store.AuthorizeProductActionAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            CreateClaims(
                subject: "admin-subject",
                groupObjectIds: ["entra-admin-group"],
                audience: "api://wrong-audience"),
            ProductAuthorizationAction.IdentityManage,
            new ProductScope(ProductScopeKind.Organization, ScopeId: null));

        Assert.False(decision.IsAllowed);
        Assert.Equal(ProductAuthorizationDenialReason.InvalidTenant, decision.DenialReason);

        var auditEvents = await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId);
        var auditEvent = Assert.Single(auditEvents);
        Assert.Equal("denied", auditEvent.Decision);
        Assert.Equal(ProductAuthorizationDenialReason.InvalidTenant, auditEvent.DenialReason);
        Assert.Null(auditEvent.ActorProductUserId);
        Assert.Null(auditEvent.EffectiveRole);
    }

    [Fact]
    public async Task InvalidAudienceCannotCreateRoleMappingAndAuditsDenial()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "internal", "contoso-tenant");
        var invalidClaims = CreateClaims(
            subject: "admin-subject",
            groupObjectIds: ["entra-admin-group"],
            audience: "api://wrong-audience");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.CreateProductRoleMappingAsync(
            seed.Organization.CustomerOrganizationId,
            new CreateProductRoleMappingRequest(
                IdentityTenantId: seed.IdentityTenant.IdentityTenantId,
                ExternalPrincipalType: ExternalPrincipalType.GroupObjectId,
                ExternalPrincipalId: "entra-lead-group",
                ProductRole: ProductRole.EngineeringLead,
                ScopeKind: ProductScopeKind.Repository,
                ScopeId: "repo-001",
                EffectiveFromUtc: Now,
                EffectiveToUtc: null,
                ChangedByClaims: invalidClaims,
                CorrelationId: "authz-create-invalid-claims",
                AuditEventId: "audit-role-map-invalid-claims-001")));

        var auditEvents = await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId);
        var auditEvent = Assert.Single(auditEvents);
        Assert.Equal("denied", auditEvent.Decision);
        Assert.Equal(ProductAuthorizationDenialReason.InvalidTenant, auditEvent.DenialReason);
        Assert.Equal("authz-create-invalid-claims", auditEvent.CorrelationId);
        Assert.Null(auditEvent.ActorProductUserId);
        Assert.Null(auditEvent.EffectiveRole);
    }

    [Fact]
    public async Task AppRoleUserSubjectAndServicePrincipalMappingsResolveWithoutGroupNames()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "internal", "contoso-tenant");
        var adminClaims = CreateClaims(subject: "admin-subject", groupObjectIds: ["entra-admin-group"]);
        var admin = await SeedPlatformAdminAsync(store, seed, adminClaims);

        await store.CreateProductRoleMappingAsync(
            seed.Organization.CustomerOrganizationId,
            new CreateProductRoleMappingRequest(
                IdentityTenantId: seed.IdentityTenant.IdentityTenantId,
                ExternalPrincipalType: ExternalPrincipalType.AppRole,
                ExternalPrincipalId: "TokenObservability.PlatformAdmin",
                ProductRole: ProductRole.PlatformAdmin,
                ScopeKind: ProductScopeKind.Organization,
                ScopeId: null,
                EffectiveFromUtc: Now,
                EffectiveToUtc: null,
                ChangedByClaims: adminClaims,
                CorrelationId: "authz-create-app-role",
                AuditEventId: "audit-role-map-006"));

        await store.CreateProductRoleMappingAsync(
            seed.Organization.CustomerOrganizationId,
            new CreateProductRoleMappingRequest(
                IdentityTenantId: seed.IdentityTenant.IdentityTenantId,
                ExternalPrincipalType: ExternalPrincipalType.UserSubject,
                ExternalPrincipalId: "lead-subject",
                ProductRole: ProductRole.EngineeringLead,
                ScopeKind: ProductScopeKind.Repository,
                ScopeId: "repo-001",
                EffectiveFromUtc: Now,
                EffectiveToUtc: null,
                ChangedByClaims: adminClaims,
                CorrelationId: "authz-create-user-subject",
                AuditEventId: "audit-role-map-007"));

        await SeedRoleMappingAsync(
            store,
            seed,
            admin.ProductUserId,
            ExternalPrincipalType.ServicePrincipal,
            "service-principal-subject",
            ProductRole.PlatformAdmin,
            ProductScopeKind.Organization,
            scopeId: null,
            auditEventId: "audit-role-map-008");

        var appRoleDecision = await store.AuthorizeProductActionAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            CreateClaims(
                subject: "app-role-subject",
                groupObjectIds: [],
                appRoles: ["TokenObservability.PlatformAdmin"]),
            ProductAuthorizationAction.IdentityManage,
            new ProductScope(ProductScopeKind.Organization, ScopeId: null));

        var userSubjectDecision = await store.AuthorizeProductActionAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            CreateClaims(subject: "lead-subject", groupObjectIds: []),
            ProductAuthorizationAction.SessionReadScoped,
            new ProductScope(ProductScopeKind.Repository, ScopeId: "repo-001"));

        var servicePrincipalDecision = await store.AuthorizeProductActionAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            CreateClaims(subject: "service-principal-subject", groupObjectIds: []),
            ProductAuthorizationAction.IdentityManage,
            new ProductScope(ProductScopeKind.Organization, ScopeId: null));

        Assert.True(appRoleDecision.IsAllowed);
        Assert.True(userSubjectDecision.IsAllowed);
        Assert.True(servicePrincipalDecision.IsAllowed);
    }

    [Fact]
    public async Task GrafanaRoleNamesCannotBeMappedAsProductApiAppRoles()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "internal", "contoso-tenant");
        var adminClaims = CreateClaims(subject: "admin-subject", groupObjectIds: ["entra-admin-group"]);
        var admin = await SeedPlatformAdminAsync(store, seed, adminClaims);
        var grafanaRoleNames = new[]
        {
            "Grafana Admin",
            "Grafana Editor",
            "Grafana Viewer",
            "Grafana Limited Viewer",
            "GrafanaAdmin",
            "GrafanaEditor",
            "GrafanaViewer",
            "GrafanaLimitedViewer",
        };

        for (var index = 0; index < grafanaRoleNames.Length; index++)
        {
            var grafanaRoleName = grafanaRoleNames[index];

            await Assert.ThrowsAsync<ArgumentException>(() => store.CreateProductRoleMappingAsync(
                seed.Organization.CustomerOrganizationId,
                new CreateProductRoleMappingRequest(
                    IdentityTenantId: seed.IdentityTenant.IdentityTenantId,
                    ExternalPrincipalType: ExternalPrincipalType.AppRole,
                    ExternalPrincipalId: grafanaRoleName,
                    ProductRole: ProductRole.PlatformAdmin,
                    ScopeKind: ProductScopeKind.Organization,
                    ScopeId: null,
                    EffectiveFromUtc: Now,
                    EffectiveToUtc: null,
                    ChangedByClaims: adminClaims,
                    CorrelationId: $"authz-grafana-app-role-denied-{index}",
                    AuditEventId: $"audit-grafana-app-role-denied-{index}")));

            await Assert.ThrowsAsync<ArgumentException>(() => SeedRoleMappingAsync(
                store,
                seed,
                admin.ProductUserId,
                ExternalPrincipalType.AppRole,
                grafanaRoleName,
                ProductRole.PlatformAdmin,
                ProductScopeKind.Organization,
                scopeId: null,
                auditEventId: $"audit-grafana-app-role-seed-denied-{index}"));
        }
    }

    [Fact]
    public void PostgreSqlTenantMetadataMigrationIncludesAuditableProductRoleMapping()
    {
        var root = FindRepositoryRoot();
        var migrationPath = Path.Combine(
            root,
            "src",
            "TokenObservability.Infrastructure",
            "Persistence",
            "PostgreSql",
            "Migrations",
            "0001_tenant_metadata.sql");

        var migration = File.ReadAllText(migrationPath);

        Assert.Contains("CREATE TABLE IF NOT EXISTS product_role_mapping", migration);
        Assert.Contains("product_role_mapping_id uuid PRIMARY KEY", migration);
        Assert.Contains("external_principal_type text NOT NULL", migration);
        Assert.Contains("external_principal_id text NOT NULL", migration);
        Assert.Contains("product_role text NOT NULL", migration);
        Assert.Contains("scope_kind text NOT NULL", migration);
        Assert.Contains("created_by_product_user_id uuid NOT NULL", migration);
        Assert.Contains("changed_by_product_user_id uuid NOT NULL", migration);
        Assert.Contains("audit_event_id text NOT NULL", migration);
        Assert.Contains("CREATE TABLE IF NOT EXISTS governance_audit_event", migration);
        Assert.Contains("actor_product_user_id uuid NULL", migration);
        Assert.Contains("effective_role text NULL", migration);
        Assert.Contains("decision text NOT NULL", migration);
        Assert.Contains("denial_reason text NULL", migration);
        Assert.Contains("target_resource_kind text NOT NULL", migration);
        Assert.Contains("correlation_id text NOT NULL", migration);
        Assert.Contains("audit_event_id text NOT NULL", migration);
        Assert.Contains("CONSTRAINT pk_governance_audit_event PRIMARY KEY (customer_organization_id, audit_event_id)", migration);
        Assert.DoesNotContain("audit_event_id text PRIMARY KEY", migration);
        Assert.Contains("CONSTRAINT fk_product_role_mapping_audit_event FOREIGN KEY (customer_organization_id, audit_event_id) REFERENCES governance_audit_event (customer_organization_id, audit_event_id)", migration);
        Assert.Contains("ck_governance_audit_event_effective_role", migration);
        Assert.Contains("ck_governance_audit_event_denial_reason", migration);
        Assert.Contains("ck_governance_audit_event_decision", migration);
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS ux_product_role_mapping_active_principal_role_scope", migration);
        Assert.Contains("COALESCE(scope_id, '')", migration);
        Assert.Contains("ck_product_role_mapping_scope_id_required", migration);
        Assert.Contains("scope_kind IN ('organization', 'self') AND scope_id IS NULL", migration);
        Assert.DoesNotContain("group_display_name", migration, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ProductUser> SeedPlatformAdminAsync(
        InMemoryTenantMetadataStore store,
        TenantSeed seed,
        AuthenticatedTokenClaims adminClaims)
    {
        var admin = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            adminClaims);

        await SeedRoleMappingAsync(
            store,
            seed,
            admin.ProductUserId,
            ExternalPrincipalType.GroupObjectId,
            adminClaims.GroupObjectIds.Single(),
            ProductRole.PlatformAdmin,
            ProductScopeKind.Organization,
            scopeId: null,
            auditEventId: $"audit-seed-admin-{admin.ProductUserId}");

        return admin;
    }

    private static Task<ProductRoleMapping> SeedRoleMappingAsync(
        InMemoryTenantMetadataStore store,
        TenantSeed seed,
        ProductUserId changedByProductUserId,
        ExternalPrincipalType externalPrincipalType,
        string externalPrincipalId,
        ProductRole productRole,
        ProductScopeKind scopeKind,
        string? scopeId,
        string auditEventId)
    {
        return store.SeedProductRoleMappingAsync(
            seed.Organization.CustomerOrganizationId,
            new SeedProductRoleMappingRequest(
                IdentityTenantId: seed.IdentityTenant.IdentityTenantId,
                ExternalPrincipalType: externalPrincipalType,
                ExternalPrincipalId: externalPrincipalId,
                ProductRole: productRole,
                ScopeKind: scopeKind,
                ScopeId: scopeId,
                EffectiveFromUtc: Now,
                EffectiveToUtc: null,
                ChangedByProductUserId: changedByProductUserId,
                ActorEffectiveRole: ProductRole.PlatformAdmin,
                CorrelationId: $"bootstrap-{auditEventId}",
                AuditEventId: auditEventId));
    }

    private static AuthenticatedTokenClaims CreateClaims(
        string subject,
        IReadOnlyList<string> groupObjectIds,
        string externalTenantId = "contoso-tenant",
        string audience = "api://token-observability",
        IReadOnlyList<string>? appRoles = null)
    {
        return new AuthenticatedTokenClaims(
            Issuer: $"https://login.microsoftonline.com/{externalTenantId}/v2.0",
            ExternalTenantId: externalTenantId,
            Audience: audience,
            Subject: subject,
            DisplayLabel: "Hari Praghash",
            Email: "hari@example.com",
            GroupObjectIds: groupObjectIds,
            AppRoles: appRoles ?? []);
    }

    private static async Task<TenantSeed> CreateTenantAsync(
        InMemoryTenantMetadataStore store,
        string slug,
        string externalTenantId)
    {
        var organization = await store.CreateCustomerOrganizationAsync(new CreateCustomerOrganizationRequest(
            Slug: slug,
            DisplayName: $"{slug} Engineering",
            DataResidencyRegion: "eastus2",
            IsolationTier: CustomerOrganizationIsolationTier.Shared));

        var identityTenant = await store.CreateIdentityTenantAsync(
            organization.CustomerOrganizationId,
            new CreateIdentityTenantRequest(
                Provider: IdentityTenantProvider.MicrosoftEntra,
                Issuer: $"https://login.microsoftonline.com/{externalTenantId}/v2.0",
                ExternalTenantId: externalTenantId,
                AllowedAudiences: ["api://token-observability"],
                JwksUri: null,
                DisplayName: $"{slug} Entra ID"));

        return new TenantSeed(organization, identityTenant);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AiAgentTokenObservability.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed record TenantSeed(CustomerOrganization Organization, IdentityTenant IdentityTenant);
}
