using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TokenObservability.Api;
using TokenObservability.Domain.Authorization;
using TokenObservability.Domain.Ingestion;
using TokenObservability.Domain.Pricing;
using TokenObservability.Domain.Recommendations;
using TokenObservability.Domain.Tenancy;
using TokenObservability.Infrastructure.Ingestion;
using TokenObservability.Infrastructure.Persistence;

namespace TokenObservability.Runtime.Tests;

public sealed class ProductApiAuthorizationContextTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ProductAuthorizationRolesDoNotIncludeGrafanaRoleNames()
    {
        var productRoleNames = Enum.GetNames<ProductRole>();

        foreach (var grafanaRoleName in new[]
        {
            "Grafana Admin",
            "Grafana Editor",
            "Grafana Viewer",
            "Grafana Limited Viewer",
            "GrafanaAdmin",
            "GrafanaEditor",
            "GrafanaViewer",
            "GrafanaLimitedViewer",
        })
        {
            Assert.DoesNotContain(grafanaRoleName, productRoleNames);
        }
    }

    [Fact]
    public async Task CurrentUserRouteReturnsResolvedAuthorizationContext()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var adminClaims = CreateClaims(subject: "admin-subject", groupObjectIds: ["entra-admin-group"]);
        var admin = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            adminClaims);
        await SeedRoleMappingAsync(
            store,
            seed,
            admin.ProductUserId,
            "entra-admin-group",
            ProductRole.PlatformAdmin);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/me", "contoso", adminClaims);

        using var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("contoso", body.RootElement.GetProperty("customerOrganization").GetProperty("slug").GetString());
        Assert.Equal("admin-subject", body.RootElement.GetProperty("productUser").GetProperty("displayLabel").GetString());
        Assert.Contains("PlatformAdmin", body.RootElement.GetProperty("roles").EnumerateArray().Select(role => role.GetString()));
    }

    [Fact]
    public async Task CurrentUserRouteAllowsMappedNonAdminDashboardUsers()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var developerClaims = CreateClaims(subject: "developer-subject", groupObjectIds: ["entra-developer-group"]);
        var leadClaims = CreateClaims(subject: "lead-subject", groupObjectIds: ["entra-lead-group"]);
        var developer = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            developerClaims);
        var lead = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            leadClaims);
        await SeedRoleMappingAsync(
            store,
            seed,
            developer.ProductUserId,
            "entra-developer-group",
            ProductRole.Developer);
        await SeedRoleMappingAsync(
            store,
            seed,
            lead.ProductUserId,
            "entra-lead-group",
            ProductRole.EngineeringLead);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var developerRequest = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/me", "contoso", developerClaims);
        using var leadRequest = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/me", "contoso", leadClaims);

        using var developerResponse = await client.SendAsync(developerRequest);
        using var leadResponse = await client.SendAsync(leadRequest);

        developerResponse.EnsureSuccessStatusCode();
        leadResponse.EnsureSuccessStatusCode();
        await AssertResponseContainsRoleAsync(developerResponse, "Developer");
        await AssertResponseContainsRoleAsync(leadResponse, "EngineeringLead");
    }

    [Fact]
    public async Task CurrentUserRouteRejectsMissingTenantContext()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var claims = CreateClaims(subject: "admin-subject", groupObjectIds: ["entra-admin-group"]);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/me", tenantSlug: null, claims);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertProblemCodeAsync(response, "tenant_context_required");
    }

    [Fact]
    public async Task CurrentUserRouteRejectsAmbiguousTenantContext()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        await CreateTenantAsync(store, "contoso", "contoso-tenant");
        await CreateTenantAsync(store, "fabrikam", "fabrikam-tenant");
        var claims = CreateClaims(subject: "admin-subject", groupObjectIds: ["entra-admin-group"]);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/me", "contoso, fabrikam", claims);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertProblemCodeAsync(response, "tenant_context_ambiguous");
    }

    [Fact]
    public async Task CurrentUserRouteRejectsCrossTenantClaims()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var adminClaims = CreateClaims(subject: "admin-subject", groupObjectIds: ["entra-admin-group"]);
        var admin = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            adminClaims);
        await SeedRoleMappingAsync(
            store,
            seed,
            admin.ProductUserId,
            "entra-admin-group",
            ProductRole.PlatformAdmin);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateAuthorizedRequest(
            HttpMethod.Get,
            "/api/v1/me",
            "contoso",
            CreateClaims(
                subject: "admin-subject",
                groupObjectIds: ["entra-admin-group"],
                externalTenantId: "fabrikam-tenant"));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertProblemCodeAsync(response, "authorization_denied");

        var auditEvents = await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId);
        var auditEvent = Assert.Single(auditEvents, auditEvent => auditEvent.Decision == "denied");
        Assert.Equal("denied", auditEvent.Decision);
        Assert.Equal(ProductAuthorizationDenialReason.InvalidTenant, auditEvent.DenialReason);
        Assert.Equal(ProductAuthorizationAction.CurrentUserRead, auditEvent.Action);
    }

    [Fact]
    public async Task CurrentUserRouteRejectsMissingRoleMappingAndAuditsDenial()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateAuthorizedRequest(
            HttpMethod.Get,
            "/api/v1/me",
            "contoso",
            CreateClaims(subject: "unmapped-subject", groupObjectIds: ["unmapped-group"]));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertProblemCodeAsync(response, "authorization_denied");

        var auditEvent = Assert.Single(await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId));
        Assert.Equal("denied", auditEvent.Decision);
        Assert.Equal(ProductAuthorizationDenialReason.MissingRoleMapping, auditEvent.DenialReason);
        Assert.Equal(ProductAuthorizationAction.CurrentUserRead, auditEvent.Action);
    }

    [Fact]
    public async Task CurrentUserRouteRejectsSuspendedCustomerOrganization()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var claims = CreateClaims(subject: "admin-subject", groupObjectIds: ["entra-admin-group"]);
        await store.SetCustomerOrganizationStatusAsync(
            seed.Organization.CustomerOrganizationId,
            CustomerOrganizationStatus.Suspended);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/me", "contoso", claims);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertProblemCodeAsync(response, "tenant_context_required");
    }

    [Fact]
    public async Task CurrentUserRouteRejectsDisabledProductUserAndAuditsDenial()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var adminClaims = CreateClaims(subject: "admin-subject", groupObjectIds: ["entra-admin-group"]);
        var admin = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            adminClaims);
        await SeedRoleMappingAsync(
            store,
            seed,
            admin.ProductUserId,
            "entra-admin-group",
            ProductRole.PlatformAdmin);
        await store.SetProductUserStatusAsync(admin.ProductUserId, ProductUserStatus.Disabled);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/me", "contoso", adminClaims);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertProblemCodeAsync(response, "authorization_denied");

        var auditEvents = await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId);
        var auditEvent = Assert.Single(auditEvents, auditEvent => auditEvent.Decision == "denied");
        Assert.Equal(ProductAuthorizationDenialReason.InvalidTenant, auditEvent.DenialReason);
        Assert.Equal(ProductAuthorizationAction.CurrentUserRead, auditEvent.Action);
    }

    [Fact]
    public async Task VersionedReadinessRejectsCallerWithoutOperationalScope()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var developerClaims = CreateClaims(subject: "developer-subject", groupObjectIds: ["entra-developer-group"]);
        var developer = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            developerClaims);
        await SeedRoleMappingAsync(
            store,
            seed,
            developer.ProductUserId,
            "entra-developer-group",
            ProductRole.Developer);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/system/readiness", "contoso", developerClaims);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertProblemCodeAsync(response, "authorization_denied");
    }

    [Fact]
    public async Task VersionedReadinessAllowsOperationalRoleWhenReadinessIsConfigured()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var adminClaims = CreateClaims(subject: "admin-subject", groupObjectIds: ["entra-admin-group"]);
        var admin = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            adminClaims);
        await SeedRoleMappingAsync(
            store,
            seed,
            admin.ProductUserId,
            "entra-admin-group",
            ProductRole.PlatformAdmin);
        using var factory = CreateFactory(store, configureReadiness: true);
        using var client = factory.CreateClient();
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/system/readiness", "contoso", adminClaims);

        using var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("authorization_enforcement", content);
    }

    [Fact]
    public async Task AuditEventsRouteReturnsTenantScopedEventsWithEvidenceMetadata()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var contoso = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var fabrikam = await CreateTenantAsync(store, "fabrikam", "fabrikam-tenant");
        var contosoAdminClaims = CreateClaims(subject: "contoso-admin", groupObjectIds: ["contoso-admin-group"]);
        var fabrikamAdminClaims = CreateClaims(
            subject: "fabrikam-admin",
            groupObjectIds: ["fabrikam-admin-group"],
            externalTenantId: "fabrikam-tenant");
        var contosoAdmin = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            contoso.Organization.CustomerOrganizationId,
            contoso.IdentityTenant.IdentityTenantId,
            contosoAdminClaims);
        var fabrikamAdmin = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            fabrikam.Organization.CustomerOrganizationId,
            fabrikam.IdentityTenant.IdentityTenantId,
            fabrikamAdminClaims);
        await SeedRoleMappingAsync(
            store,
            contoso,
            contosoAdmin.ProductUserId,
            "contoso-admin-group",
            ProductRole.PlatformAdmin);
        await SeedRoleMappingAsync(
            store,
            fabrikam,
            fabrikamAdmin.ProductUserId,
            "fabrikam-admin-group",
            ProductRole.PlatformAdmin);
        await store.RecordGovernanceAuditEventAsync(
            contoso.Organization.CustomerOrganizationId,
            new CreateGovernanceAuditEventRequest(
                AuditEventId: "audit-contoso-created-001",
                ActorProductUserId: contosoAdmin.ProductUserId,
                EffectiveRole: ProductRole.PlatformAdmin,
                Action: ProductAuthorizationAction.IdentityManage,
                TargetScope: new ProductScope(ProductScopeKind.Organization, ScopeId: null),
                Decision: "created",
                DenialReason: null,
                CorrelationId: "audit-contoso-correlation-001",
                EvidenceMetadata: new Dictionary<string, string>
                {
                    ["evidence_kind"] = "admin_operation",
                    ["request_route"] = "/api/v1/identity/role-mappings"
                }));
        await store.RecordGovernanceAuditEventAsync(
            fabrikam.Organization.CustomerOrganizationId,
            new CreateGovernanceAuditEventRequest(
                AuditEventId: "audit-fabrikam-created-001",
                ActorProductUserId: fabrikamAdmin.ProductUserId,
                EffectiveRole: ProductRole.PlatformAdmin,
                Action: ProductAuthorizationAction.IdentityManage,
                TargetScope: new ProductScope(ProductScopeKind.Organization, ScopeId: null),
                Decision: "created",
                DenialReason: null,
                CorrelationId: "audit-fabrikam-correlation-001",
                EvidenceMetadata: new Dictionary<string, string>
                {
                    ["evidence_kind"] = "admin_operation",
                    ["request_route"] = "/api/v1/identity/role-mappings"
                }));
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/audit-events", "contoso", contosoAdminClaims);

        using var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToArray();
        var auditEvent = Assert.Single(items, item => item.GetProperty("auditEventId").GetString() == "audit-contoso-created-001");
        Assert.DoesNotContain(items, item => item.GetProperty("auditEventId").GetString() == "audit-fabrikam-created-001");
        Assert.Equal("created", auditEvent.GetProperty("decision").GetString());
        Assert.Equal("admin_operation", auditEvent.GetProperty("evidenceMetadata").GetProperty("evidence_kind").GetString());
        Assert.Equal("/api/v1/identity/role-mappings", auditEvent.GetProperty("evidenceMetadata").GetProperty("request_route").GetString());
    }

    [Fact]
    public async Task AuditEventsRouteRejectsUnauthorizedCallerAndAuditsDenialWithoutCapturedContent()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var developerClaims = CreateClaims(subject: "developer-subject", groupObjectIds: ["developer-group"]);
        var developer = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            developerClaims);
        await SeedRoleMappingAsync(
            store,
            seed,
            developer.ProductUserId,
            "developer-group",
            ProductRole.Developer);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/audit-events", "contoso", developerClaims);
        request.Headers.Add("X-Correlation-Id", "audit-read-denied-001");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertProblemCodeAsync(response, "authorization_denied");

        var auditEvents = await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId);
        var deniedAuditEvent = Assert.Single(auditEvents, auditEvent => auditEvent.CorrelationId == "audit-read-denied-001");
        Assert.Equal("denied", deniedAuditEvent.Decision);
        Assert.Equal(ProductAuthorizationAction.AuditRead, deniedAuditEvent.Action);
        Assert.Equal(ProductAuthorizationDenialReason.InsufficientRole, deniedAuditEvent.DenialReason);
        Assert.Equal("Organization", deniedAuditEvent.EvidenceMetadata["requested_scope_kind"]);
        Assert.DoesNotContain(deniedAuditEvent.EvidenceMetadata.Keys, key => key.Contains("prompt", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(deniedAuditEvent.EvidenceMetadata.Keys, key => key.Contains("code", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(deniedAuditEvent.EvidenceMetadata.Keys, key => key.Contains("command", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(deniedAuditEvent.EvidenceMetadata.Keys, key => key.Contains("tool", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task IngestionRejectionsRouteReturnsOnlyTenantScopedSafeMetadata()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var contoso = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var fabrikam = await CreateTenantAsync(store, "fabrikam", "fabrikam-tenant");
        var contosoAdminClaims = CreateClaims(subject: "contoso-admin", groupObjectIds: ["contoso-admin-group"]);
        var fabrikamAdminClaims = CreateClaims(
            subject: "fabrikam-admin",
            groupObjectIds: ["fabrikam-admin-group"],
            externalTenantId: "fabrikam-tenant");
        var contosoAdmin = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            contoso.Organization.CustomerOrganizationId,
            contoso.IdentityTenant.IdentityTenantId,
            contosoAdminClaims);
        var fabrikamAdmin = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            fabrikam.Organization.CustomerOrganizationId,
            fabrikam.IdentityTenant.IdentityTenantId,
            fabrikamAdminClaims);
        await SeedRoleMappingAsync(
            store,
            contoso,
            contosoAdmin.ProductUserId,
            "contoso-admin-group",
            ProductRole.PlatformAdmin);
        await SeedRoleMappingAsync(
            store,
            fabrikam,
            fabrikamAdmin.ProductUserId,
            "fabrikam-admin-group",
            ProductRole.PlatformAdmin);
        var contosoAuditEvent = await RecordRejectionAuditEventAsync(
            store,
            contoso,
            "audit-contoso-rejection-001",
            "contoso-rejection-001",
            "malformed_otlp",
            "profile-contoso-codex");
        var fabrikamAuditEvent = await RecordRejectionAuditEventAsync(
            store,
            fabrikam,
            "audit-fabrikam-rejection-001",
            "fabrikam-rejection-001",
            "payload_too_large",
            "profile-fabrikam-codex");
        await store.RecordIngestionRejectionAsync(new CreateIngestionRejectionRecordRequest(
            CustomerOrganizationId: contoso.Organization.CustomerOrganizationId,
            HarnessSetupProfileId: "profile-contoso-codex",
            ScopedIngestionCredentialId: null,
            DeclaredHarness: "codex-cli",
            SignalType: "logs",
            RequestRoute: "/v1/logs",
            ReasonCode: "malformed_otlp",
            HttpStatus: StatusCodes.Status400BadRequest,
            CorrelationId: "contoso-rejection-001",
            AuditEventId: contosoAuditEvent.AuditEventId,
            EvidenceMetadata: new Dictionary<string, string>
            {
                ["evidence_kind"] = "ingestion_decision",
                ["operation"] = "ingestion_rejection",
                ["result"] = "malformed_otlp",
                ["request_route"] = "/v1/logs",
                ["scope_kind"] = ProductScopeKind.HarnessProfile.ToString(),
                ["scope_id"] = "profile-contoso-codex"
            }));
        await store.RecordIngestionRejectionAsync(new CreateIngestionRejectionRecordRequest(
            CustomerOrganizationId: fabrikam.Organization.CustomerOrganizationId,
            HarnessSetupProfileId: "profile-fabrikam-codex",
            ScopedIngestionCredentialId: null,
            DeclaredHarness: "codex-cli",
            SignalType: "logs",
            RequestRoute: "/v1/logs",
            ReasonCode: "payload_too_large",
            HttpStatus: StatusCodes.Status413PayloadTooLarge,
            CorrelationId: "fabrikam-rejection-001",
            AuditEventId: fabrikamAuditEvent.AuditEventId,
            EvidenceMetadata: new Dictionary<string, string>
            {
                ["evidence_kind"] = "ingestion_decision",
                ["operation"] = "ingestion_rejection",
                ["result"] = "payload_too_large",
                ["request_route"] = "/v1/logs",
                ["scope_kind"] = ProductScopeKind.HarnessProfile.ToString(),
                ["scope_id"] = "profile-fabrikam-codex"
            }));
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/ingestion-rejections", "contoso", contosoAdminClaims);

        using var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToArray();
        var rejection = Assert.Single(items);
        Assert.Equal("contoso-rejection-001", rejection.GetProperty("correlationId").GetString());
        Assert.Equal("malformed_otlp", rejection.GetProperty("reasonCode").GetString());
        Assert.Equal("profile-contoso-codex", rejection.GetProperty("harnessSetupProfileId").GetString());
        Assert.DoesNotContain("fabrikam", rejection.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", rejection.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prompt", rejection.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("command", rejection.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tool", rejection.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SessionsRoutePreservesTokenMetricQualityMarkers()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var adminClaims = CreateClaims(subject: "admin-subject", groupObjectIds: ["entra-admin-group"]);
        var admin = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            adminClaims);
        await SeedRoleMappingAsync(
            store,
            seed,
            admin.ProductUserId,
            "entra-admin-group",
            ProductRole.PlatformAdmin);
        var credential = await IssueCredentialAsync(store, seed, "profile-contoso-codex");
        var providerSessionIdHash = ComputeSha256Hex("conversation-api-001");
        var envelope = await store.RecordTelemetryEnvelopeAsync(new CreateTelemetryEnvelopeRecordRequest(
            CustomerOrganizationId: seed.Organization.CustomerOrganizationId,
            HarnessSetupProfileId: credential.Credential.HarnessSetupProfileId,
            ScopedIngestionCredentialId: credential.Credential.ScopedIngestionCredentialId,
            ProductUserId: credential.Credential.ProductUserId,
            Harness: "codex-cli",
            SchemaVersion: "2026-06-01",
            SignalType: "metric",
            SourceEventName: "codex.api_request",
            SourceEventTimestampUtc: Now,
            ConversationIdHash: providerSessionIdHash,
            TurnIdHash: null,
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
            MetricState: "mixed",
            MetricStatus: TokenMetricStatus.Mixed,
            MetricConfidence: TokenMetricConfidence.Estimated,
            SourceEvidenceKind: "harness_emitted",
            CorrelationId: "correlation-api-001",
            DedupeKeyHash: ComputeSha256Hex("dedupe-api-001"),
            IngestionVersionMetadata: new Dictionary<string, string>
            {
                ["schema_version"] = "2026-06-01",
                ["harness_version"] = "unavailable",
                ["contract_version"] = "2026-06-01"
            }));
        var session = await store.UpsertAgentSessionAsync(new CreateAgentSessionRecordRequest(
            CustomerOrganizationId: seed.Organization.CustomerOrganizationId,
            ProductUserId: credential.Credential.ProductUserId,
            HarnessSetupProfileId: credential.Credential.HarnessSetupProfileId,
            Harness: CodingAgentHarness.CodexCli,
            ProviderSessionIdHash: providerSessionIdHash,
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
            TokenMetricName.CachedInputTokens,
            Value: null,
            TokenMetricStatus.Unavailable,
            TokenMetricConfidence.Unavailable,
            TokenObservationSourceKind.Missing,
            envelope.TelemetryEnvelopeId));
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/sessions", "contoso", adminClaims);

        using var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var sessionJson = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("mixed", sessionJson.GetProperty("tokenSummary").GetProperty("metricStatus").GetString());
        Assert.Equal("estimated", sessionJson.GetProperty("tokenSummary").GetProperty("metricConfidence").GetString());
        var tokenObservations = sessionJson.GetProperty("tokenObservations").EnumerateArray().ToArray();
        var inputTokens = Assert.Single(
            tokenObservations,
            observation => observation.GetProperty("metricName").GetString() == "input_tokens");
        Assert.Equal(0, inputTokens.GetProperty("value").GetInt64());
        Assert.Equal("observed", inputTokens.GetProperty("metricStatus").GetString());
        Assert.Equal("observed", inputTokens.GetProperty("metricConfidence").GetString());
        var cachedTokens = Assert.Single(
            tokenObservations,
            observation => observation.GetProperty("metricName").GetString() == "cached_input_tokens");
        Assert.True(cachedTokens.GetProperty("value").ValueKind == JsonValueKind.Null);
        Assert.Equal("unavailable", cachedTokens.GetProperty("metricStatus").GetString());
        Assert.Equal("unavailable", cachedTokens.GetProperty("metricConfidence").GetString());
    }

    [Fact]
    public async Task TokenHotspotStorePreservesEvidenceBoundariesAndTenantIsolation()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var contoso = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var fabrikam = await CreateTenantAsync(store, "fabrikam", "fabrikam-tenant");
        var contosoCredential = await IssueCredentialAsync(store, contoso, "profile-contoso-codex");
        var fabrikamCredential = await IssueCredentialAsync(store, fabrikam, "profile-fabrikam-codex");
        var contosoSession = await SeedTokenSessionAsync(
            store,
            contosoCredential.Credential,
            "contoso-hotspot-session",
            Now,
            [(TokenMetricName.InputTokens, 120_000, TokenMetricStatus.Observed, TokenMetricConfidence.Observed, null)]);
        var fabrikamSession = await SeedTokenSessionAsync(
            store,
            fabrikamCredential.Credential,
            "fabrikam-hotspot-session",
            Now,
            [(TokenMetricName.InputTokens, 90_000, TokenMetricStatus.Observed, TokenMetricConfidence.Observed, null)]);

        var confirmed = await store.RecordTokenHotspotAsync(new CreateTokenHotspotRecordRequest(
            contoso.Organization.CustomerOrganizationId,
            contosoSession.AgentSessionId,
            TokenHotspotType.LargeContext,
            TokenHotspotFindingState.Confirmed,
            TokenHotspotAttributionType.Direct,
            TokenHotspotConfidence.High,
            TokenMetricStatus.Observed,
            TokenMetricConfidence.Observed,
            PromptCacheEvidenceState.NotApplicable,
            ModelName: "gpt-5",
            EvidenceSummary: "Input tokens exceeded configured threshold using observed token evidence.",
            EvidenceReferenceIds: [contosoSession.AgentSessionId],
            TokenBurnScore: 0.91,
            EstimatedCostImpact: null));
        await store.RecordTokenHotspotAsync(new CreateTokenHotspotRecordRequest(
            fabrikam.Organization.CustomerOrganizationId,
            fabrikamSession.AgentSessionId,
            TokenHotspotType.PromptCacheBreakage,
            TokenHotspotFindingState.CandidateLlmInferred,
            TokenHotspotAttributionType.LlmInferred,
            TokenHotspotConfidence.Low,
            TokenMetricStatus.Estimated,
            TokenMetricConfidence.LlmInferred,
            PromptCacheEvidenceState.InferredCandidate,
            ModelName: "gpt-5",
            EvidenceSummary: "Candidate cache breakage inferred from explicit cache evidence gaps.",
            EvidenceReferenceIds: [fabrikamSession.AgentSessionId],
            TokenBurnScore: null,
            EstimatedCostImpact: null));
        await store.RecordTokenHotspotAsync(new CreateTokenHotspotRecordRequest(
            contoso.Organization.CustomerOrganizationId,
            contosoSession.AgentSessionId,
            TokenHotspotType.PromptCacheBreakage,
            TokenHotspotFindingState.CandidateCorrelated,
            TokenHotspotAttributionType.Correlated,
            TokenHotspotConfidence.Medium,
            TokenMetricStatus.Unavailable,
            TokenMetricConfidence.Unavailable,
            PromptCacheEvidenceState.Unavailable,
            ModelName: "gpt-5",
            EvidenceSummary: "Cache cause is unknown because cache evidence was unavailable.",
            EvidenceReferenceIds: [contosoSession.AgentSessionId],
            TokenBurnScore: null,
            EstimatedCostImpact: null));
        await store.RecordTokenHotspotAsync(new CreateTokenHotspotRecordRequest(
            contoso.Organization.CustomerOrganizationId,
            contosoSession.AgentSessionId,
            TokenHotspotType.PromptCacheBreakage,
            TokenHotspotFindingState.CandidateCorrelated,
            TokenHotspotAttributionType.Correlated,
            TokenHotspotConfidence.High,
            TokenMetricStatus.Observed,
            TokenMetricConfidence.Observed,
            PromptCacheEvidenceState.KnownReason,
            ModelName: "gpt-5",
            EvidenceSummary: "Stable prefix changed based on observed cache evidence.",
            EvidenceReferenceIds: [contosoSession.AgentSessionId],
            TokenBurnScore: null,
            EstimatedCostImpact: null));

        var contosoHotspots = await store.ListTokenHotspotsAsync(
            contoso.Organization.CustomerOrganizationId,
            contosoSession.AgentSessionId);
        Assert.Equal(3, contosoHotspots.Count);
        var hotspot = Assert.Single(contosoHotspots, candidate => candidate.HotspotType == TokenHotspotType.LargeContext);
        Assert.Equal(confirmed.TokenHotspotId, hotspot.TokenHotspotId);
        Assert.Equal("codex-cli", hotspot.Harness);
        Assert.Equal("gpt-5", hotspot.ModelName);
        Assert.Equal(TokenMetricStatus.Observed, hotspot.MetricStatus);
        Assert.Equal(TokenHotspotFindingState.Confirmed, hotspot.FindingState);
        Assert.Equal(TokenHotspotAttributionType.Direct, hotspot.AttributionType);
        Assert.Equal(TokenHotspotConfidence.High, hotspot.Confidence);
        Assert.Equal(PromptCacheEvidenceState.NotApplicable, hotspot.PromptCacheEvidenceState);
        Assert.DoesNotContain("fabrikam", hotspot.EvidenceSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(contosoHotspots, candidate => candidate.PromptCacheEvidenceState == PromptCacheEvidenceState.Unavailable);
        Assert.Contains(contosoHotspots, candidate => candidate.PromptCacheEvidenceState == PromptCacheEvidenceState.KnownReason);

        await Assert.ThrowsAsync<ArgumentException>(() => store.RecordTokenHotspotAsync(new CreateTokenHotspotRecordRequest(
            contoso.Organization.CustomerOrganizationId,
            contosoSession.AgentSessionId,
            TokenHotspotType.PromptCacheBreakage,
            TokenHotspotFindingState.Confirmed,
            TokenHotspotAttributionType.LlmInferred,
            TokenHotspotConfidence.Medium,
            TokenMetricStatus.Estimated,
            TokenMetricConfidence.LlmInferred,
            PromptCacheEvidenceState.InferredCandidate,
            ModelName: "gpt-5",
            EvidenceSummary: "LLM says this is confirmed.",
            EvidenceReferenceIds: [contosoSession.AgentSessionId],
            TokenBurnScore: null,
            EstimatedCostImpact: null)));
        await Assert.ThrowsAsync<ArgumentException>(() => store.RecordTokenHotspotAsync(new CreateTokenHotspotRecordRequest(
            contoso.Organization.CustomerOrganizationId,
            contosoSession.AgentSessionId,
            TokenHotspotType.LargeContext,
            TokenHotspotFindingState.Confirmed,
            TokenHotspotAttributionType.Direct,
            TokenHotspotConfidence.Medium,
            TokenMetricStatus.Estimated,
            TokenMetricConfidence.LlmInferred,
            PromptCacheEvidenceState.NotApplicable,
            ModelName: "gpt-5",
            EvidenceSummary: "Input tokens exceeded configured threshold using estimated token evidence.",
            EvidenceReferenceIds: [contosoSession.AgentSessionId],
            TokenBurnScore: null,
            EstimatedCostImpact: null)));
        await Assert.ThrowsAsync<ArgumentException>(() => store.RecordTokenHotspotAsync(new CreateTokenHotspotRecordRequest(
            contoso.Organization.CustomerOrganizationId,
            contosoSession.AgentSessionId,
            TokenHotspotType.PromptCacheBreakage,
            TokenHotspotFindingState.CandidateLlmInferred,
            TokenHotspotAttributionType.LlmInferred,
            TokenHotspotConfidence.Low,
            TokenMetricStatus.Estimated,
            TokenMetricConfidence.LlmInferred,
            PromptCacheEvidenceState.InferredCandidate,
            ModelName: "gpt-5",
            EvidenceSummary: "The user made an obvious error.",
            EvidenceReferenceIds: [contosoSession.AgentSessionId],
            TokenBurnScore: null,
            EstimatedCostImpact: null)));
        await Assert.ThrowsAsync<ArgumentException>(() => store.RecordTokenHotspotAsync(new CreateTokenHotspotRecordRequest(
            contoso.Organization.CustomerOrganizationId,
            contosoSession.AgentSessionId,
            TokenHotspotType.PromptCacheBreakage,
            TokenHotspotFindingState.CandidateLlmInferred,
            TokenHotspotAttributionType.LlmInferred,
            TokenHotspotConfidence.Low,
            TokenMetricStatus.Estimated,
            TokenMetricConfidence.LlmInferred,
            PromptCacheEvidenceState.InferredCandidate,
            ModelName: "gpt-5",
            EvidenceSummary: "Candidate cache breakage inferred from supplied evidence.",
            EvidenceReferenceIds: [],
            TokenBurnScore: null,
            EstimatedCostImpact: null)));
    }

    [Fact]
    public async Task SessionsRouteReturnsSafeTokenHotspotsWithoutPeopleRanking()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var adminClaims = CreateClaims(subject: "admin-subject", groupObjectIds: ["entra-admin-group"]);
        var admin = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            adminClaims);
        await SeedRoleMappingAsync(
            store,
            seed,
            admin.ProductUserId,
            "entra-admin-group",
            ProductRole.PlatformAdmin);
        var credential = await IssueCredentialAsync(store, seed, "profile-contoso-codex");
        var session = await SeedTokenSessionAsync(
            store,
            credential.Credential,
            "api-hotspot-session",
            Now,
            [(TokenMetricName.CachedInputTokens, null, TokenMetricStatus.Unavailable, TokenMetricConfidence.Unavailable, null)]);
        await store.RecordTokenHotspotAsync(new CreateTokenHotspotRecordRequest(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            TokenHotspotType.PromptCacheBreakage,
            TokenHotspotFindingState.CandidateCorrelated,
            TokenHotspotAttributionType.Correlated,
            TokenHotspotConfidence.Medium,
            TokenMetricStatus.Unavailable,
            TokenMetricConfidence.Unavailable,
            PromptCacheEvidenceState.Unknown,
            ModelName: "gpt-5",
            EvidenceSummary: "Cache cause is unknown because provider cache fields were unavailable.",
            EvidenceReferenceIds: [session.AgentSessionId],
            TokenBurnScore: null,
            EstimatedCostImpact: null));
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/sessions", "contoso", adminClaims);

        using var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var sessionJson = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
        var tokenHotspot = Assert.Single(sessionJson.GetProperty("tokenHotspots").EnumerateArray());
        Assert.Equal("prompt_cache_breakage", tokenHotspot.GetProperty("hotspotType").GetString());
        Assert.Equal("candidate_correlated", tokenHotspot.GetProperty("findingState").GetString());
        Assert.Equal("correlated", tokenHotspot.GetProperty("attributionType").GetString());
        Assert.Equal("medium", tokenHotspot.GetProperty("confidence").GetString());
        Assert.Equal("unavailable", tokenHotspot.GetProperty("metricStatus").GetString());
        Assert.Equal("unavailable", tokenHotspot.GetProperty("metricConfidence").GetString());
        Assert.Equal("unknown", tokenHotspot.GetProperty("promptCacheEvidenceState").GetString());
        Assert.Equal("codex-cli", tokenHotspot.GetProperty("harness").GetString());
        Assert.Equal("gpt-5", tokenHotspot.GetProperty("modelName").GetString());
        Assert.Contains("unknown", tokenHotspot.GetProperty("evidenceSummary").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(tokenHotspot.TryGetProperty("productUserId", out _));
        Assert.False(tokenHotspot.TryGetProperty("developerRank", out _));
        var json = body.RootElement.ToString();
        Assert.DoesNotContain("raw_prompt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prompt_text", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("command_output", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tool_result", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("blame", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ranking", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SessionsRouteAllowsDeveloperSelfViewWithoutCrossUserLeakage()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var developerClaims = CreateClaims(subject: "developer-subject", groupObjectIds: ["developer-group"]);
        var otherDeveloperClaims = CreateClaims(subject: "other-developer-subject", groupObjectIds: ["developer-group"]);
        var developer = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            developerClaims);
        var otherDeveloper = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            otherDeveloperClaims);
        await SeedRoleMappingAsync(
            store,
            seed,
            developer.ProductUserId,
            "developer-group",
            ProductRole.Developer,
            ProductScopeKind.Self);
        var developerCredential = await IssueCredentialAsync(
            store,
            seed,
            "profile-contoso-codex",
            developer.ProductUserId);
        var otherCredential = await IssueCredentialAsync(
            store,
            seed,
            "profile-contoso-codex-other",
            otherDeveloper.ProductUserId);
        await SeedUnavailableSessionAsync(store, developerCredential.Credential, "developer-session-001");
        await SeedUnavailableSessionAsync(store, otherCredential.Credential, "other-developer-session-001");
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/sessions", "contoso", developerClaims);

        using var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var session = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(developer.ProductUserId.ToString(), session.GetProperty("productUserId").GetString());
        Assert.DoesNotContain(otherDeveloper.ProductUserId.ToString(), body.RootElement.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SessionsRouteRejectsReadOnlyViewerWithoutSessionPermission()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var viewerClaims = CreateClaims(subject: "viewer-subject", groupObjectIds: ["viewer-group"]);
        var viewer = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            viewerClaims);
        await SeedRoleMappingAsync(
            store,
            seed,
            viewer.ProductUserId,
            "viewer-group",
            ProductRole.ReadOnlyViewer);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/sessions", "contoso", viewerClaims);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertProblemCodeAsync(response, "authorization_denied");
    }

    [Fact]
    public async Task OverviewTokenTimelineRouteReturnsAuthorizedDenseTenantBuckets()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var contoso = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var fabrikam = await CreateTenantAsync(store, "fabrikam", "fabrikam-tenant");
        var viewerClaims = CreateClaims(subject: "viewer-subject", groupObjectIds: ["viewer-group"]);
        var viewer = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            contoso.Organization.CustomerOrganizationId,
            contoso.IdentityTenant.IdentityTenantId,
            viewerClaims);
        await SeedRoleMappingAsync(
            store,
            contoso,
            viewer.ProductUserId,
            "viewer-group",
            ProductRole.ReadOnlyViewer);
        var contosoCredential = await IssueCredentialAsync(store, contoso, "profile-contoso-codex");
        var fabrikamCredential = await IssueCredentialAsync(store, fabrikam, "profile-fabrikam-codex");
        await SeedTokenSessionAsync(
            store,
            contosoCredential.Credential,
            "contoso-timeline-001",
            new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero),
            [
                (TokenMetricName.InputTokens, 10, TokenMetricStatus.Observed, TokenMetricConfidence.Observed, "invocation-001"),
                (TokenMetricName.OutputTokens, 20, TokenMetricStatus.Estimated, TokenMetricConfidence.Estimated, "invocation-001")
            ]);
        await SeedTokenSessionAsync(
            store,
            contosoCredential.Credential,
            "contoso-timeline-002",
            new DateTimeOffset(2026, 6, 12, 9, 0, 0, TimeSpan.Zero),
            [
                (TokenMetricName.InputTokens, null, TokenMetricStatus.Unavailable, TokenMetricConfidence.Unavailable, null)
            ]);
        await SeedTokenSessionAsync(
            store,
            fabrikamCredential.Credential,
            "fabrikam-timeline-001",
            new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero),
            [
                (TokenMetricName.InputTokens, 999, TokenMetricStatus.Observed, TokenMetricConfidence.Observed, null)
            ]);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateAuthorizedRequest(
            HttpMethod.Get,
            "/api/v1/overview/token-timeline?from=2026-06-10&to=2026-06-12&movingAverageWindowDays=2",
            "contoso",
            viewerClaims);

        using var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Collection(
            items,
            item =>
            {
                Assert.Equal("2026-06-10", item.GetProperty("bucketDateUtc").GetString());
                Assert.Equal(30, item.GetProperty("tokenBurn").GetInt64());
                Assert.Equal("mixed", item.GetProperty("metricStatus").GetString());
                Assert.Equal(30, item.GetProperty("movingAverageTokenBurn").GetDouble());
                Assert.False(item.GetProperty("isDenseZeroBurn").GetBoolean());
            },
            item =>
            {
                Assert.Equal("2026-06-11", item.GetProperty("bucketDateUtc").GetString());
                Assert.Equal(0, item.GetProperty("tokenBurn").GetInt64());
                Assert.Equal("not_applicable", item.GetProperty("metricStatus").GetString());
                Assert.Equal(15, item.GetProperty("movingAverageTokenBurn").GetDouble());
                Assert.True(item.GetProperty("isDenseZeroBurn").GetBoolean());
            },
            item =>
            {
                Assert.Equal("2026-06-12", item.GetProperty("bucketDateUtc").GetString());
                Assert.Equal(JsonValueKind.Null, item.GetProperty("tokenBurn").ValueKind);
                Assert.Equal("unavailable", item.GetProperty("metricStatus").GetString());
                Assert.Equal(0, item.GetProperty("movingAverageTokenBurn").GetDouble());
                Assert.False(item.GetProperty("isDenseZeroBurn").GetBoolean());
            });
        var json = body.RootElement.ToString();
        Assert.DoesNotContain("999", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("productUserId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("traceId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prompt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("commandOutput", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("toolResult", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OverviewRouteReturnsAggregateCostMixWithoutPersonOrSessionFields()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var viewerClaims = CreateClaims(subject: "viewer-subject", groupObjectIds: ["viewer-group"]);
        var viewer = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            viewerClaims);
        await SeedRoleMappingAsync(
            store,
            seed,
            viewer.ProductUserId,
            "viewer-group",
            ProductRole.ReadOnlyViewer);
        var credential = await IssueCredentialAsync(store, seed, "profile-contoso-codex", viewer.ProductUserId);
        await SeedTokenSessionAsync(
            store,
            credential.Credential,
            "contoso-cost-mix-001",
            Now,
            [
                (TokenMetricName.InputTokens, 1_000_000, TokenMetricStatus.Observed, TokenMetricConfidence.Observed, "invocation-001")
            ]);
        var session = Assert.Single(await store.ListAgentSessionsAsync(seed.Organization.CustomerOrganizationId));
        await store.RecordCostEstimateAsync(
            new CreateCostEstimateRecordRequest(
                seed.Organization.CustomerOrganizationId,
                session.AgentSessionId,
                ModelInvocationId: null,
                PricingBasisId: null,
                PricingVersion: null,
                "USD",
                EstimatedCost: null,
                CostEstimateStatus.Unavailable,
                CostEstimateSourceKind.Unavailable,
                TokenMetricStatus.Unavailable,
                TokenMetricConfidence.Unavailable,
                "openai",
                "gpt-5",
                "standard",
                PricingTokenType.Input));
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/overview", "contoso", viewerClaims);

        using var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var bucket = Assert.Single(body.RootElement.GetProperty("costMix").EnumerateArray());
        Assert.Equal("openai", bucket.GetProperty("providerName").GetString());
        Assert.Equal("gpt-5", bucket.GetProperty("modelName").GetString());
        Assert.Equal("input", bucket.GetProperty("tokenType").GetString());
        Assert.Equal("unavailable", bucket.GetProperty("costStatus").GetString());
        Assert.Equal(JsonValueKind.Null, bucket.GetProperty("estimatedCost").ValueKind);
        var json = body.RootElement.ToString();
        Assert.DoesNotContain("sessionId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("productUserId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("providerSessionId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prompt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("command", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tool", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PricingBasisRoutesRequireIdempotencyKeyAndAuditCustomerOverrides()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var adminClaims = CreateClaims(subject: "admin-subject", groupObjectIds: ["admin-group"]);
        var admin = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            adminClaims);
        await SeedRoleMappingAsync(
            store,
            seed,
            admin.ProductUserId,
            "admin-group",
            ProductRole.PlatformAdmin);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var missingIdempotencyRequest = CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/v1/pricing/basis",
            "contoso",
            adminClaims);
        missingIdempotencyRequest.Content = new StringContent(CreatePricingOverrideJson(), Encoding.UTF8, "application/json");

        using var missingIdempotencyResponse = await client.SendAsync(missingIdempotencyRequest);

        Assert.Equal(HttpStatusCode.BadRequest, missingIdempotencyResponse.StatusCode);
        await AssertProblemCodeAsync(missingIdempotencyResponse, "idempotency_key_required");

        using var createRequest = CreateAuthorizedRequest(HttpMethod.Post, "/api/v1/pricing/basis", "contoso", adminClaims);
        createRequest.Headers.Add("Idempotency-Key", "pricing-override-001");
        createRequest.Content = new StringContent(CreatePricingOverrideJson(), Encoding.UTF8, "application/json");

        using var createResponse = await client.SendAsync(createRequest);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        using var createBody = await JsonDocument.ParseAsync(await createResponse.Content.ReadAsStreamAsync());
        Assert.Equal("admin_override", createBody.RootElement.GetProperty("sourceKind").GetString());
        Assert.Equal("approved", createBody.RootElement.GetProperty("reviewState").GetString());
        var createdPricingBasisId = createBody.RootElement.GetProperty("pricingBasisId").GetString();

        using var replayRequest = CreateAuthorizedRequest(HttpMethod.Post, "/api/v1/pricing/basis", "contoso", adminClaims);
        replayRequest.Headers.Add("Idempotency-Key", "pricing-override-001");
        replayRequest.Content = new StringContent(CreatePricingOverrideJson(), Encoding.UTF8, "application/json");

        using var replayResponse = await client.SendAsync(replayRequest);

        Assert.Equal(HttpStatusCode.Created, replayResponse.StatusCode);
        using var replayBody = await JsonDocument.ParseAsync(await replayResponse.Content.ReadAsStreamAsync());
        Assert.Equal(createdPricingBasisId, replayBody.RootElement.GetProperty("pricingBasisId").GetString());

        using var conflictingReplayRequest = CreateAuthorizedRequest(HttpMethod.Post, "/api/v1/pricing/basis", "contoso", adminClaims);
        conflictingReplayRequest.Headers.Add("Idempotency-Key", "pricing-override-001");
        conflictingReplayRequest.Content = new StringContent(CreatePricingOverrideJson(pricePerMillionTokens: 0.80m), Encoding.UTF8, "application/json");

        using var conflictingReplayResponse = await client.SendAsync(conflictingReplayRequest);

        Assert.Equal(HttpStatusCode.Conflict, conflictingReplayResponse.StatusCode);
        await AssertProblemCodeAsync(conflictingReplayResponse, "idempotency_key_conflict");

        using var listRequest = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/pricing/basis", "contoso", adminClaims);
        using var listResponse = await client.SendAsync(listRequest);

        listResponse.EnsureSuccessStatusCode();
        using var listBody = await JsonDocument.ParseAsync(await listResponse.Content.ReadAsStreamAsync());
        var item = Assert.Single(listBody.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("gpt-5", item.GetProperty("modelName").GetString());
        Assert.Equal("input", item.GetProperty("tokenType").GetString());
        Assert.Equal("enterprise_contract", item.GetProperty("billingRoute").GetString());

        var auditEvent = Assert.Single(
            await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId),
            audit => audit.Action == ProductAuthorizationAction.PricingManage);
        Assert.Equal("pricing_override_create", auditEvent.EvidenceMetadata["operation"]);
        Assert.Equal("admin_override", auditEvent.EvidenceMetadata["source_kind"]);
    }

    [Fact]
    public async Task PricingBasisOverrideConcurrentSameKeyRequestsCreateSingleRecord()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var adminClaims = CreateClaims(subject: "admin-subject", groupObjectIds: ["admin-group"]);
        var admin = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            adminClaims);
        await SeedRoleMappingAsync(
            store,
            seed,
            admin.ProductUserId,
            "admin-group",
            ProductRole.PlatformAdmin);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        async Task<HttpResponseMessage> SendRequestAsync()
        {
            var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/v1/pricing/basis", "contoso", adminClaims);
            request.Headers.Add("Idempotency-Key", "pricing-override-concurrent");
            request.Content = new StringContent(CreatePricingOverrideJson(), Encoding.UTF8, "application/json");
            return await client.SendAsync(request);
        }

        var responses = await Task.WhenAll(SendRequestAsync(), SendRequestAsync());
        using var first = responses[0];
        using var second = responses[1];

        Assert.True(first.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict);
        Assert.True(second.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict);
        var records = await store.ListPricingBasisRecordsAsync(seed.Organization.CustomerOrganizationId);
        Assert.Single(records);
        var pricingAuditEvents = await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId);
        Assert.Single(pricingAuditEvents, audit => audit.EvidenceMetadata.GetValueOrDefault("operation") == "pricing_override_create");
    }

    [Fact]
    public async Task PricingReviewRoutesReplayIdempotentApproveAndRejectMutations()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var adminClaims = CreateClaims(subject: "admin-subject", groupObjectIds: ["admin-group"]);
        var admin = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            adminClaims);
        await SeedRoleMappingAsync(
            store,
            seed,
            admin.ProductUserId,
            "admin-group",
            ProductRole.PlatformAdmin);
        var approveCandidate = await SeedPricingCandidateAsync(store, seed, "gpt-5", "audit-pricing-seed-approve");
        var rejectCandidate = await SeedPricingCandidateAsync(store, seed, "gpt-5-mini", "audit-pricing-seed-reject");
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        using var approveRequest = CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/v1/pricing/basis/{approveCandidate.PricingBasisId}/approve",
            "contoso",
            adminClaims);
        approveRequest.Headers.Add("Idempotency-Key", "pricing-approve-001");
        approveRequest.Content = new StringContent("""{"decisionReason":"looks_current"}""", Encoding.UTF8, "application/json");

        using var approveResponse = await client.SendAsync(approveRequest);

        approveResponse.EnsureSuccessStatusCode();
        using var approveBody = await JsonDocument.ParseAsync(await approveResponse.Content.ReadAsStreamAsync());
        Assert.Equal("approved", approveBody.RootElement.GetProperty("reviewState").GetString());

        using var approveReplayRequest = CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/v1/pricing/basis/{approveCandidate.PricingBasisId}/approve",
            "contoso",
            adminClaims);
        approveReplayRequest.Headers.Add("Idempotency-Key", "pricing-approve-001");
        approveReplayRequest.Content = new StringContent("""{"decisionReason":"looks_current"}""", Encoding.UTF8, "application/json");

        using var approveReplayResponse = await client.SendAsync(approveReplayRequest);

        approveReplayResponse.EnsureSuccessStatusCode();
        using var approveReplayBody = await JsonDocument.ParseAsync(await approveReplayResponse.Content.ReadAsStreamAsync());
        Assert.Equal(approveCandidate.PricingBasisId, approveReplayBody.RootElement.GetProperty("pricingBasisId").GetString());

        using var rejectRequest = CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/v1/pricing/basis/{rejectCandidate.PricingBasisId}/reject",
            "contoso",
            adminClaims);
        rejectRequest.Headers.Add("Idempotency-Key", "pricing-reject-001");
        rejectRequest.Content = new StringContent("""{"decisionReason":"stale"}""", Encoding.UTF8, "application/json");

        using var rejectResponse = await client.SendAsync(rejectRequest);

        rejectResponse.EnsureSuccessStatusCode();
        using var rejectBody = await JsonDocument.ParseAsync(await rejectResponse.Content.ReadAsStreamAsync());
        Assert.Equal("rejected", rejectBody.RootElement.GetProperty("reviewState").GetString());

        using var rejectReplayRequest = CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/v1/pricing/basis/{rejectCandidate.PricingBasisId}/reject",
            "contoso",
            adminClaims);
        rejectReplayRequest.Headers.Add("Idempotency-Key", "pricing-reject-001");
        rejectReplayRequest.Content = new StringContent("""{"decisionReason":"stale"}""", Encoding.UTF8, "application/json");

        using var rejectReplayResponse = await client.SendAsync(rejectReplayRequest);

        rejectReplayResponse.EnsureSuccessStatusCode();
        using var rejectReplayBody = await JsonDocument.ParseAsync(await rejectReplayResponse.Content.ReadAsStreamAsync());
        Assert.Equal(rejectCandidate.PricingBasisId, rejectReplayBody.RootElement.GetProperty("pricingBasisId").GetString());

        var pricingAuditEvents = await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId);
        Assert.Single(pricingAuditEvents, audit => audit.EvidenceMetadata.GetValueOrDefault("operation") == "pricing_basis_approved");
        Assert.Single(pricingAuditEvents, audit => audit.EvidenceMetadata.GetValueOrDefault("operation") == "pricing_basis_rejected");
    }

    [Fact]
    public async Task PricingSupersedeRouteReviewsCandidateAndApprovedRecordsWithIdempotency()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var adminClaims = CreateClaims(subject: "admin-subject", groupObjectIds: ["admin-group"]);
        var admin = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            adminClaims);
        await SeedRoleMappingAsync(
            store,
            seed,
            admin.ProductUserId,
            "admin-group",
            ProductRole.PlatformAdmin);
        var candidate = await SeedPricingCandidateAsync(store, seed, "gpt-5", "audit-pricing-seed-supersede-candidate");
        var approved = await SeedPricingCandidateAsync(store, seed, "gpt-5-mini", "audit-pricing-seed-supersede-approved");
        await store.ApprovePricingBasisAsync(
            new PricingBasisReviewRequest(
                seed.Organization.CustomerOrganizationId,
                approved.PricingBasisId,
                "audit-pricing-approve-before-supersede",
                "pricing-approve-before-supersede",
                "reviewed"),
            admin.ProductUserId,
            ProductRole.PlatformAdmin);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        using var candidateRequest = CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/v1/pricing/basis/{candidate.PricingBasisId}/supersede",
            "contoso",
            adminClaims);
        candidateRequest.Headers.Add("Idempotency-Key", "pricing-supersede-candidate");
        candidateRequest.Content = new StringContent("""{"decisionReason":"stale_candidate"}""", Encoding.UTF8, "application/json");

        using var candidateResponse = await client.SendAsync(candidateRequest);

        candidateResponse.EnsureSuccessStatusCode();
        using var candidateBody = await JsonDocument.ParseAsync(await candidateResponse.Content.ReadAsStreamAsync());
        Assert.Equal("superseded", candidateBody.RootElement.GetProperty("reviewState").GetString());

        using var approvedRequest = CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/v1/pricing/basis/{approved.PricingBasisId}/supersede",
            "contoso",
            adminClaims);
        approvedRequest.Headers.Add("Idempotency-Key", "pricing-supersede-approved");
        approvedRequest.Content = new StringContent("""{"decisionReason":"contract_replaced"}""", Encoding.UTF8, "application/json");

        using var approvedResponse = await client.SendAsync(approvedRequest);

        approvedResponse.EnsureSuccessStatusCode();
        using var approvedBody = await JsonDocument.ParseAsync(await approvedResponse.Content.ReadAsStreamAsync());
        Assert.Equal("superseded", approvedBody.RootElement.GetProperty("reviewState").GetString());
        Assert.NotEqual(JsonValueKind.Null, approvedBody.RootElement.GetProperty("effectiveToUtc").ValueKind);

        using var replayRequest = CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/v1/pricing/basis/{approved.PricingBasisId}/supersede",
            "contoso",
            adminClaims);
        replayRequest.Headers.Add("Idempotency-Key", "pricing-supersede-approved");
        replayRequest.Content = new StringContent("""{"decisionReason":"contract_replaced"}""", Encoding.UTF8, "application/json");

        using var replayResponse = await client.SendAsync(replayRequest);

        replayResponse.EnsureSuccessStatusCode();
        using var replayBody = await JsonDocument.ParseAsync(await replayResponse.Content.ReadAsStreamAsync());
        Assert.Equal(approved.PricingBasisId, replayBody.RootElement.GetProperty("pricingBasisId").GetString());

        var pricingAuditEvents = await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId);
        Assert.Equal(2, pricingAuditEvents.Count(audit => audit.EvidenceMetadata.GetValueOrDefault("operation") == "pricing_basis_superseded"));
    }

    [Fact]
    public async Task OverviewTokenTimelineRouteRejectsInvalidRangeAndUnauthorizedCaller()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var unmappedClaims = CreateClaims(subject: "unmapped-subject", groupObjectIds: ["unmapped-group"]);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var unauthorizedRequest = CreateAuthorizedRequest(
            HttpMethod.Get,
            "/api/v1/overview/token-timeline?from=2026-06-10&to=2026-06-12&movingAverageWindowDays=2",
            "contoso",
            unmappedClaims);

        using var unauthorizedResponse = await client.SendAsync(unauthorizedRequest);

        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedResponse.StatusCode);
        await AssertProblemCodeAsync(unauthorizedResponse, "authorization_denied");

        var adminClaims = CreateClaims(subject: "admin-subject", groupObjectIds: ["admin-group"]);
        var admin = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            adminClaims);
        await SeedRoleMappingAsync(
            store,
            seed,
            admin.ProductUserId,
            "admin-group",
            ProductRole.PlatformAdmin);
        using var invalidRequest = CreateAuthorizedRequest(
            HttpMethod.Get,
            "/api/v1/overview/token-timeline?from=2026-06-12&to=2026-06-10&movingAverageWindowDays=2",
            "contoso",
            adminClaims);

        using var invalidResponse = await client.SendAsync(invalidRequest);

        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        await AssertProblemCodeAsync(invalidResponse, "invalid_token_timeline_query");
    }

    [Fact]
    public async Task GrafanaDrilldownGateAllowsOnlyAggregateFiltersAfterAuthorization()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var adminClaims = CreateClaims(subject: "admin-subject", groupObjectIds: ["admin-group"]);
        var viewerClaims = CreateClaims(subject: "viewer-subject", groupObjectIds: ["viewer-group"]);
        var developerClaims = CreateClaims(subject: "developer-subject", groupObjectIds: ["developer-group"]);
        var admin = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            adminClaims);
        var viewer = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            viewerClaims);
        var developer = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            developerClaims);
        await SeedRoleMappingAsync(
            store,
            seed,
            admin.ProductUserId,
            "admin-group",
            ProductRole.PlatformAdmin);
        await SeedRoleMappingAsync(
            store,
            seed,
            viewer.ProductUserId,
            "viewer-group",
            ProductRole.ReadOnlyViewer);
        await SeedRoleMappingAsync(
            store,
            seed,
            developer.ProductUserId,
            "developer-group",
            ProductRole.Developer,
            ProductScopeKind.Self);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var allowedRequest = CreateAuthorizedRequest(
            HttpMethod.Get,
            "/api/v1/grafana/drilldown?route=/overview&from=2026-06-10&to=2026-06-12&environment=dv&region=eastus2&harness=codex&model=gpt-5&modelProvider=openai",
            "contoso",
            viewerClaims);

        using var allowedResponse = await client.SendAsync(allowedRequest);

        allowedResponse.EnsureSuccessStatusCode();
        using var allowedBody = await JsonDocument.ParseAsync(await allowedResponse.Content.ReadAsStreamAsync());
        Assert.Equal("/overview", allowedBody.RootElement.GetProperty("route").GetString());
        var filters = allowedBody.RootElement.GetProperty("filters");
        Assert.Equal("dv", filters.GetProperty("environment").GetString());
        Assert.Equal("openai", filters.GetProperty("modelProvider").GetString());
        Assert.DoesNotContain("sessionId", allowedBody.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);

        foreach (var forbiddenParameter in new[]
        {
            "sessionId",
            "developer",
            "credentialId",
            "traceId",
            "filePath",
            "contentReferenceId",
            "blobUri",
            "prompt",
            "commandOutput",
            "toolResult",
            "returnUrl"
        })
        {
            using var forbiddenParameterRequest = CreateAuthorizedRequest(
                HttpMethod.Get,
                $"/api/v1/grafana/drilldown?route=/overview&from=2026-06-10&{forbiddenParameter}=forbidden",
                "contoso",
                adminClaims);
            using var forbiddenParameterResponse = await client.SendAsync(forbiddenParameterRequest);
            Assert.Equal(HttpStatusCode.BadRequest, forbiddenParameterResponse.StatusCode);
            await AssertProblemCodeAsync(forbiddenParameterResponse, "invalid_grafana_drilldown_filter");
        }

        using var unknownParameterRequest = CreateAuthorizedRequest(
            HttpMethod.Get,
            "/api/v1/grafana/drilldown?route=/overview&from=2026-06-10&workspaceId=unknown",
            "contoso",
            adminClaims);
        using var unknownParameterResponse = await client.SendAsync(unknownParameterRequest);
        Assert.Equal(HttpStatusCode.BadRequest, unknownParameterResponse.StatusCode);
        await AssertProblemCodeAsync(unknownParameterResponse, "invalid_grafana_drilldown_filter");

        using var absoluteRouteRequest = CreateAuthorizedRequest(
            HttpMethod.Get,
            "/api/v1/grafana/drilldown?route=https://example.test/overview&from=2026-06-10",
            "contoso",
            adminClaims);
        using var absoluteRouteResponse = await client.SendAsync(absoluteRouteRequest);
        Assert.Equal(HttpStatusCode.BadRequest, absoluteRouteResponse.StatusCode);
        await AssertProblemCodeAsync(absoluteRouteResponse, "invalid_grafana_drilldown_filter");

        using var absoluteFilterRequest = CreateAuthorizedRequest(
            HttpMethod.Get,
            "/api/v1/grafana/drilldown?route=/overview&from=2026-06-10&model=https://example.test/model",
            "contoso",
            adminClaims);
        using var absoluteFilterResponse = await client.SendAsync(absoluteFilterRequest);
        Assert.Equal(HttpStatusCode.BadRequest, absoluteFilterResponse.StatusCode);
        await AssertProblemCodeAsync(absoluteFilterResponse, "invalid_grafana_drilldown_filter");

        using var sessionsRouteRequest = CreateAuthorizedRequest(
            HttpMethod.Get,
            "/api/v1/grafana/drilldown?route=/sessions&from=2026-06-10",
            "contoso",
            viewerClaims);
        using var sessionsRouteResponse = await client.SendAsync(sessionsRouteRequest);
        Assert.Equal(HttpStatusCode.Forbidden, sessionsRouteResponse.StatusCode);
        await AssertProblemCodeAsync(sessionsRouteResponse, "authorization_denied");

        using var selfSessionsRouteRequest = CreateAuthorizedRequest(
            HttpMethod.Get,
            "/api/v1/grafana/drilldown?route=/sessions&from=2026-06-10",
            "contoso",
            developerClaims);
        using var selfSessionsRouteResponse = await client.SendAsync(selfSessionsRouteRequest);
        selfSessionsRouteResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task IngestionRejectionsRouteRejectsUnauthorizedCaller()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var developerClaims = CreateClaims(subject: "developer-subject", groupObjectIds: ["developer-group"]);
        var developer = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            developerClaims);
        await SeedRoleMappingAsync(
            store,
            seed,
            developer.ProductUserId,
            "developer-group",
            ProductRole.Developer);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/ingestion-rejections", "contoso", developerClaims);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertProblemCodeAsync(response, "authorization_denied");
    }

    [Fact]
    public async Task IngestionRejectionStoreRejectsCrossTenantCredentialLink()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var contoso = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var fabrikam = await CreateTenantAsync(store, "fabrikam", "fabrikam-tenant");
        var fabrikamCredential = await IssueCredentialAsync(store, fabrikam, "profile-fabrikam-codex");

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RecordIngestionRejectionAsync(
            new CreateIngestionRejectionRecordRequest(
                CustomerOrganizationId: contoso.Organization.CustomerOrganizationId,
                HarnessSetupProfileId: "profile-fabrikam-codex",
                ScopedIngestionCredentialId: fabrikamCredential.Credential.ScopedIngestionCredentialId,
                DeclaredHarness: "codex-cli",
                SignalType: "logs",
                RequestRoute: "/v1/logs",
                ReasonCode: "malformed_otlp",
                HttpStatus: StatusCodes.Status400BadRequest,
                CorrelationId: "cross-tenant-credential-rejection-001",
                AuditEventId: null,
                EvidenceMetadata: CreateRejectionEvidence("malformed_otlp", "profile-fabrikam-codex"))));
    }

    [Fact]
    public async Task IngestionRejectionStoreRejectsCrossTenantAuditLink()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var contoso = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var fabrikam = await CreateTenantAsync(store, "fabrikam", "fabrikam-tenant");
        var fabrikamAuditEvent = await RecordRejectionAuditEventAsync(
            store,
            fabrikam,
            "audit-fabrikam-cross-tenant-rejection-001",
            "cross-tenant-audit-rejection-001",
            "malformed_otlp",
            "profile-fabrikam-codex");

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RecordIngestionRejectionAsync(
            new CreateIngestionRejectionRecordRequest(
                CustomerOrganizationId: contoso.Organization.CustomerOrganizationId,
                HarnessSetupProfileId: "profile-fabrikam-codex",
                ScopedIngestionCredentialId: null,
                DeclaredHarness: "codex-cli",
                SignalType: "logs",
                RequestRoute: "/v1/logs",
                ReasonCode: "malformed_otlp",
                HttpStatus: StatusCodes.Status400BadRequest,
                CorrelationId: "cross-tenant-audit-rejection-001",
                AuditEventId: fabrikamAuditEvent.AuditEventId,
                EvidenceMetadata: CreateRejectionEvidence("malformed_otlp", "profile-fabrikam-codex"))));
    }

    [Theory]
    [InlineData("prompt-text")]
    [InlineData("token-abc123")]
    [InlineData("profile with spaces")]
    public async Task IngestionRejectionStoreRejectsUnsafeHarnessSetupProfileId(string harnessSetupProfileId)
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var contoso = await CreateTenantAsync(store, "contoso", "contoso-tenant");

        await Assert.ThrowsAsync<ArgumentException>(() => store.RecordIngestionRejectionAsync(
            new CreateIngestionRejectionRecordRequest(
                CustomerOrganizationId: contoso.Organization.CustomerOrganizationId,
                HarnessSetupProfileId: harnessSetupProfileId,
                ScopedIngestionCredentialId: null,
                DeclaredHarness: "codex-cli",
                SignalType: "logs",
                RequestRoute: "/v1/logs",
                ReasonCode: "malformed_otlp",
                HttpStatus: StatusCodes.Status400BadRequest,
                CorrelationId: "unsafe-profile-rejection-001",
                AuditEventId: null,
                EvidenceMetadata: CreateRejectionEvidence("malformed_otlp", "unknown"))));
    }

    [Theory]
    [InlineData("claude-code")]
    [InlineData("codex-cli-secret")]
    [InlineData("codex cli")]
    public async Task IngestionRejectionStoreRejectsUnsafeDeclaredHarness(string declaredHarness)
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var contoso = await CreateTenantAsync(store, "contoso", "contoso-tenant");

        await Assert.ThrowsAsync<ArgumentException>(() => store.RecordIngestionRejectionAsync(
            new CreateIngestionRejectionRecordRequest(
                CustomerOrganizationId: contoso.Organization.CustomerOrganizationId,
                HarnessSetupProfileId: null,
                ScopedIngestionCredentialId: null,
                DeclaredHarness: declaredHarness,
                SignalType: "logs",
                RequestRoute: "/v1/logs",
                ReasonCode: "malformed_otlp",
                HttpStatus: StatusCodes.Status400BadRequest,
                CorrelationId: "unsafe-harness-rejection-001",
                AuditEventId: null,
                EvidenceMetadata: CreateRejectionEvidence("malformed_otlp", "unknown"))));
    }

    [Fact]
    public async Task ContentReviewRoutesRequireReviewerScopeAndReturnSanitizedMetadata()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var adminClaims = CreateClaims(subject: "admin-subject", groupObjectIds: ["admin-group"]);
        var reviewerClaims = CreateClaims(subject: "reviewer-subject", groupObjectIds: ["reviewer-group"]);
        var admin = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            adminClaims);
        var reviewer = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            reviewerClaims);
        await SeedRoleMappingAsync(
            store,
            seed,
            admin.ProductUserId,
            "admin-group",
            ProductRole.PlatformAdmin);
        await SeedRoleMappingAsync(
            store,
            seed,
            reviewer.ProductUserId,
            "reviewer-group",
            ProductRole.SecurityReviewer,
            ProductScopeKind.ContentReviewQueue,
            "content-review");
        var reference = await SeedReviewRequiredContentReferenceAsync(store, seed);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        using var adminRequest = CreateAuthorizedRequest(
            HttpMethod.Get,
            "/api/v1/content-review/items",
            "contoso",
            adminClaims);
        using var adminResponse = await client.SendAsync(adminRequest);

        Assert.Equal(HttpStatusCode.Forbidden, adminResponse.StatusCode);

        using var reviewerRequest = CreateAuthorizedRequest(
            HttpMethod.Get,
            "/api/v1/content-review/items",
            "contoso",
            reviewerClaims);
        using var reviewerResponse = await client.SendAsync(reviewerRequest);

        reviewerResponse.EnsureSuccessStatusCode();
        var json = await reviewerResponse.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(json);
        var item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(reference.ContentReferenceId.ToString(), item.GetProperty("contentReferenceId").GetString());
        Assert.Equal("review_required", item.GetProperty("captureState").GetString());
        Assert.Equal("review_required", item.GetProperty("redactionStatus").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("blob").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("approvedExcerpt").ValueKind);
        Assert.DoesNotContain("hello Ada", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw_prompt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("command_output", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ContentReviewRetryAndApproveExcerptAreAuditedAndBounded()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var reviewerClaims = CreateClaims(subject: "reviewer-subject", groupObjectIds: ["reviewer-group"]);
        var reviewer = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            reviewerClaims);
        await SeedRoleMappingAsync(
            store,
            seed,
            reviewer.ProductUserId,
            "reviewer-group",
            ProductRole.SecurityReviewer,
            ProductScopeKind.ContentReviewQueue,
            "content-review");
        var reference = await SeedReviewRequiredContentReferenceAsync(store, seed, providerSessionId: "content-review-api-session-1");
        var oversizedReference = await SeedReviewRequiredContentReferenceAsync(store, seed, providerSessionId: "content-review-api-session-2");
        var boundaryReference = await SeedReviewRequiredContentReferenceAsync(store, seed, providerSessionId: "content-review-api-session-3");
        var ineligibleReference = await SeedReviewRequiredContentReferenceAsync(store, seed, providerSessionId: "content-review-api-session-4");
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        using var retryRequest = CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/v1/content-review/items/{reference.ContentReferenceId}/retry-redaction",
            "contoso",
            reviewerClaims);
        retryRequest.Content = new StringContent("""{"decisionReason":"recognizer_updated"}""", Encoding.UTF8, "application/json");
        using var missingIdempotencyRequest = CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/v1/content-review/items/{reference.ContentReferenceId}/retry-redaction",
            "contoso",
            reviewerClaims);
        missingIdempotencyRequest.Content = new StringContent("""{"decisionReason":"recognizer_updated"}""", Encoding.UTF8, "application/json");

        using var missingIdempotencyResponse = await client.SendAsync(missingIdempotencyRequest);

        Assert.Equal(HttpStatusCode.BadRequest, missingIdempotencyResponse.StatusCode);
        await AssertProblemCodeAsync(missingIdempotencyResponse, "idempotency_key_required");

        retryRequest.Headers.Add("Idempotency-Key", "content-review-retry-1");
        using var retryResponse = await client.SendAsync(retryRequest);

        retryResponse.EnsureSuccessStatusCode();
        using var retryBody = await JsonDocument.ParseAsync(await retryResponse.Content.ReadAsStreamAsync());
        Assert.Equal("retry", retryBody.RootElement.GetProperty("decision").GetString());
        var retryReviewId = retryBody.RootElement.GetProperty("redactionReviewId").GetString();
        var retriedReference = retryBody.RootElement.GetProperty("contentReference");
        Assert.Equal("review_required", retriedReference.GetProperty("captureState").GetString());
        Assert.Equal(JsonValueKind.Null, retriedReference.GetProperty("approvedExcerpt").ValueKind);

        using var retryReplayRequest = CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/v1/content-review/items/{reference.ContentReferenceId}/retry-redaction",
            "contoso",
            reviewerClaims);
        retryReplayRequest.Headers.Add("Idempotency-Key", "content-review-retry-1");
        retryReplayRequest.Content = new StringContent("""{"decisionReason":"recognizer_updated"}""", Encoding.UTF8, "application/json");
        using var retryReplayResponse = await client.SendAsync(retryReplayRequest);

        retryReplayResponse.EnsureSuccessStatusCode();
        using var retryReplayBody = await JsonDocument.ParseAsync(await retryReplayResponse.Content.ReadAsStreamAsync());
        Assert.Equal(retryReviewId, retryReplayBody.RootElement.GetProperty("redactionReviewId").GetString());

        using var retryConflictRequest = CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/v1/content-review/items/{reference.ContentReferenceId}/retry-redaction",
            "contoso",
            reviewerClaims);
        retryConflictRequest.Headers.Add("Idempotency-Key", "content-review-retry-1");
        retryConflictRequest.Content = new StringContent("""{"decisionReason":"different_reason"}""", Encoding.UTF8, "application/json");
        using var retryConflictResponse = await client.SendAsync(retryConflictRequest);

        Assert.Equal(HttpStatusCode.Conflict, retryConflictResponse.StatusCode);
        await AssertProblemCodeAsync(retryConflictResponse, "idempotency_key_conflict");

        using var approveRequest = CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/v1/content-review/items/{reference.ContentReferenceId}/approve-excerpt",
            "contoso",
            reviewerClaims);
        approveRequest.Headers.Add("Idempotency-Key", "content-review-approve-1");
        approveRequest.Content = new StringContent(
            """{"decisionReason":"manual_redaction_complete","approvedExcerpt":"Customer-safe excerpt with identifiers removed."}""",
            Encoding.UTF8,
            "application/json");
        using var approveResponse = await client.SendAsync(approveRequest);

        approveResponse.EnsureSuccessStatusCode();
        var approveJson = await approveResponse.Content.ReadAsStringAsync();
        using var approveBody = JsonDocument.Parse(approveJson);
        var approvedReference = approveBody.RootElement.GetProperty("contentReference");
        Assert.Equal("approved_excerpt", approvedReference.GetProperty("captureState").GetString());
        Assert.Equal("manually_approved", approvedReference.GetProperty("redactionStatus").GetString());
        Assert.Equal("Customer-safe excerpt with identifiers removed.", approvedReference.GetProperty("approvedExcerpt").GetString());
        Assert.Equal("content-review-artifacts", approvedReference.GetProperty("blob").GetProperty("container").GetString());
        Assert.DoesNotContain("hello Ada", approveJson, StringComparison.OrdinalIgnoreCase);

        using var boundaryApproveRequest = CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/v1/content-review/items/{boundaryReference.ContentReferenceId}/approve-excerpt",
            "contoso",
            reviewerClaims);
        boundaryApproveRequest.Headers.Add("Idempotency-Key", "content-review-approve-boundary");
        boundaryApproveRequest.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                decisionReason = "manual_redaction_complete",
                approvedExcerpt = new string('b', ContentRedactionLimits.MaxApprovedExcerptUtf8Bytes)
            }),
            Encoding.UTF8,
            "application/json");
        using var boundaryApproveResponse = await client.SendAsync(boundaryApproveRequest);

        boundaryApproveResponse.EnsureSuccessStatusCode();

        using var oversizedApproveRequest = CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/v1/content-review/items/{oversizedReference.ContentReferenceId}/approve-excerpt",
            "contoso",
            reviewerClaims);
        oversizedApproveRequest.Headers.Add("Idempotency-Key", "content-review-approve-oversized");
        oversizedApproveRequest.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                decisionReason = "manual_redaction_complete",
                approvedExcerpt = new string('a', ContentRedactionLimits.MaxApprovedExcerptUtf8Bytes + 1)
            }),
            Encoding.UTF8,
            "application/json");
        using var oversizedApproveResponse = await client.SendAsync(oversizedApproveRequest);

        Assert.Equal(HttpStatusCode.BadRequest, oversizedApproveResponse.StatusCode);
        await AssertProblemCodeAsync(oversizedApproveResponse, "validation_failed");

        using var ineligibleRequest = CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/v1/content-review/items/{ineligibleReference.ContentReferenceId}/mark-recommendation-ineligible",
            "contoso",
            reviewerClaims);
        ineligibleRequest.Headers.Add("Idempotency-Key", "content-review-ineligible-1");
        ineligibleRequest.Content = new StringContent("""{"decisionReason":"not_relevant_to_recommendations"}""", Encoding.UTF8, "application/json");
        using var ineligibleResponse = await client.SendAsync(ineligibleRequest);

        ineligibleResponse.EnsureSuccessStatusCode();
        using var ineligibleBody = await JsonDocument.ParseAsync(await ineligibleResponse.Content.ReadAsStreamAsync());
        Assert.Equal("mark_recommendation_ineligible", ineligibleBody.RootElement.GetProperty("decision").GetString());
        Assert.False(ineligibleBody.RootElement.GetProperty("contentReference").GetProperty("recommendationEligible").GetBoolean());

        var auditEvents = await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId);
        Assert.Contains(auditEvents, audit =>
            audit.Action == ProductAuthorizationAction.ContentReviewDecide &&
            audit.EvidenceMetadata["operation"] == "content_review_retry");
        Assert.Contains(auditEvents, audit =>
            audit.Action == ProductAuthorizationAction.ContentReviewDecide &&
            audit.EvidenceMetadata["operation"] == "content_review_approve_excerpt");
        Assert.Contains(auditEvents, audit =>
            audit.Action == ProductAuthorizationAction.ContentReviewDecide &&
            audit.EvidenceMetadata["operation"] == "content_review_mark_recommendation_ineligible");
    }

    [Fact]
    public async Task RecommendationRoutesReturnSanitizedSessionRecommendationsAndDetail()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var adminClaims = CreateClaims(subject: "admin-subject", groupObjectIds: ["admin-group"]);
        var admin = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            adminClaims);
        await SeedRoleMappingAsync(
            store,
            seed,
            admin.ProductUserId,
            "admin-group",
            ProductRole.PlatformAdmin);
        var credential = await IssueCredentialAsync(store, seed, "profile-contoso-codex");
        var session = await SeedTokenSessionAsync(
            store,
            credential.Credential,
            "recommendation-api-session",
            Now,
            [(TokenMetricName.InputTokens, 120_000, TokenMetricStatus.Observed, TokenMetricConfidence.Observed, null)]);
        var recommendation = await store.CreateRecommendationAsync(new CreateRecommendationRecordRequest(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId,
            TokenHotspotId: null,
            RuleId: "rec.high_input_tokens.narrow_context",
            RecommendationKind.Deterministic,
            RecommendationState.Accepted,
            RecommendationAuthorityState.Deterministic,
            RecommendationConfidence.High,
            RecommendationValidationState.Validated,
            RecommendationVisibilityScope.TeamScoped,
            EvidencePacketVersion: "recommendation.evidence.v1",
            EvidencePacketJson: "{\"schemaVersion\":\"recommendation.evidence.v1\",\"hiddenEvidence\":[]}",
            EvidencePacketHash: new string('D', 64),
            Summary: "Reduce unnecessary context and prefer targeted files.",
            Rationale: "Observed input token evidence exceeded the configured threshold.",
            RecommendedAction: "Pass targeted files and summaries.",
            ExpectedBenefit: "Lower input token use.",
            ModelPolicyVersionId: null,
            PromptTemplateVersion: null,
            EvidenceReferenceIds: [session.AgentSessionId],
            PolicyMetadata: new Dictionary<string, string> { ["content_capture_policy_version"] = "metadata_only" },
            AuditEventId: "audit-api-recommendation",
            CorrelationId: "api-recommendation"));
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        using var listRequest = CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/v1/sessions/{session.AgentSessionId}/recommendations",
            "contoso",
            adminClaims);
        using var listResponse = await client.SendAsync(listRequest);
        listResponse.EnsureSuccessStatusCode();
        using var listBody = await JsonDocument.ParseAsync(await listResponse.Content.ReadAsStreamAsync());
        var item = Assert.Single(listBody.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(recommendation.RecommendationId.ToString(), item.GetProperty("recommendationId").GetString());
        Assert.Equal("deterministic", item.GetProperty("kind").GetString());
        Assert.Equal("accepted", item.GetProperty("state").GetString());
        Assert.Equal("high", item.GetProperty("confidence").GetString());

        using var detailRequest = CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/v1/recommendations/{recommendation.RecommendationId}",
            "contoso",
            adminClaims);
        using var detailResponse = await client.SendAsync(detailRequest);
        detailResponse.EnsureSuccessStatusCode();
        var detailJson = await detailResponse.Content.ReadAsStringAsync();
        Assert.Contains("Reduce unnecessary context", detailJson);
        Assert.DoesNotContain("raw_prompt", detailJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("command_output", detailJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("developerRank", detailJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("blame", detailJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecommendationRegenerationRequiresIdempotencyAndCreatesAuditEvent()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var adminClaims = CreateClaims(subject: "admin-subject", groupObjectIds: ["admin-group"]);
        var admin = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            adminClaims);
        await SeedRoleMappingAsync(
            store,
            seed,
            admin.ProductUserId,
            "admin-group",
            ProductRole.PlatformAdmin);
        var credential = await IssueCredentialAsync(store, seed, "profile-contoso-codex");
        var session = await SeedTokenSessionAsync(
            store,
            credential.Credential,
            "recommendation-regen-session",
            Now,
            [(TokenMetricName.InputTokens, 120_000, TokenMetricStatus.Observed, TokenMetricConfidence.Observed, null)]);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        using var missingIdempotency = CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/v1/recommendations/regeneration-requests",
            "contoso",
            adminClaims);
        missingIdempotency.Content = new StringContent(
            $$"""{"agentSessionId":"{{session.AgentSessionId}}","reason":"policy_changed"}""",
            Encoding.UTF8,
            "application/json");
        using var missingResponse = await client.SendAsync(missingIdempotency);
        Assert.Equal(HttpStatusCode.BadRequest, missingResponse.StatusCode);

        using var createRequest = CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/v1/recommendations/regeneration-requests",
            "contoso",
            adminClaims);
        createRequest.Headers.Add("Idempotency-Key", "regen-key-1");
        createRequest.Content = new StringContent(
            $$"""{"agentSessionId":"{{session.AgentSessionId}}","reason":"policy_changed"}""",
            Encoding.UTF8,
            "application/json");
        using var createResponse = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        using var body = await JsonDocument.ParseAsync(await createResponse.Content.ReadAsStreamAsync());
        Assert.Equal("queued", body.RootElement.GetProperty("state").GetString());

        var auditEvents = await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId);
        Assert.Single(auditEvents, audit => audit.EvidenceMetadata["operation"] == "recommendation_regeneration_request");
    }

    private static WebApplicationFactory<TokenObservabilityApiAssemblyMarker> CreateFactory(
        InMemoryTenantMetadataStore store,
        bool configureReadiness = false)
    {
        return new WebApplicationFactory<TokenObservabilityApiAssemblyMarker>()
            .WithWebHostBuilder(builder =>
            {
                if (configureReadiness)
                {
                    builder.ConfigureAppConfiguration((_, configuration) =>
                    {
                        configuration.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["CUSTOMER_ORGANIZATION_SLUG"] = "contoso",
                            ["ProductMetadataStore:ConnectionString"] = "Host=postgresql.internal;Database=product_metadata;Username=readiness;Password=not-used",
                            ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = "InstrumentationKey=00000000-0000-0000-0000-000000000000",
                            ["TOKENOBSERVABILITY_STORAGE_ACCOUNT_NAME"] = "sttokenobservability",
                            ["TOKENOBSERVABILITY_RECOMMENDATION_DEPLOYMENT_COUNT"] = "1"
                        });
                    });
                }

                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<InMemoryTenantMetadataStore>();
                    services.RemoveAll<ITenantMetadataStore>();
                    services.RemoveAll<IProductApiIdempotencyStore>();
                    services.AddSingleton(store);
                    services.AddSingleton<ITenantMetadataStore>(store);
                    services.AddSingleton<IProductApiIdempotencyStore>(store);
                });
            });
    }

    private static async Task AssertResponseContainsRoleAsync(HttpResponseMessage response, string expectedRole)
    {
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Contains(
            expectedRole,
            body.RootElement.GetProperty("roles").EnumerateArray().Select(role => role.GetString()));
    }

    private static HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        string path,
        string? tenantSlug,
        AuthenticatedTokenClaims claims)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL", EncodePrincipal(claims));

        if (!string.IsNullOrWhiteSpace(tenantSlug))
        {
            request.Headers.Add("X-Customer-Organization-Slug", tenantSlug);
        }

        return request;
    }

    private static string EncodePrincipal(AuthenticatedTokenClaims claims)
    {
        var principal = new
        {
            claims = BuildClaims(claims).Select(static claim => new
            {
                typ = claim.Type,
                val = claim.Value
            })
        };
        var json = JsonSerializer.Serialize(principal);

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private static IEnumerable<(string Type, string Value)> BuildClaims(AuthenticatedTokenClaims claims)
    {
        yield return ("iss", claims.Issuer);
        yield return ("tid", claims.ExternalTenantId);
        yield return ("aud", claims.Audience);
        yield return ("sub", claims.Subject);
        yield return ("name", claims.DisplayLabel);

        if (!string.IsNullOrWhiteSpace(claims.Email))
        {
            yield return ("preferred_username", claims.Email);
        }

        foreach (var groupObjectId in claims.GroupObjectIds)
        {
            yield return ("groups", groupObjectId);
        }

        foreach (var appRole in claims.AppRoles)
        {
            yield return ("roles", appRole);
        }
    }

    private static AuthenticatedTokenClaims CreateClaims(
        string subject,
        IReadOnlyList<string> groupObjectIds,
        string externalTenantId = "contoso-tenant",
        string audience = "api://token-observability",
        IReadOnlyList<string>? appRoles = null)
    {
        return new AuthenticatedTokenClaims(
            Issuer: $"https://sts.windows.net/{externalTenantId}/",
            ExternalTenantId: externalTenantId,
            Audience: audience,
            Subject: subject,
            DisplayLabel: subject,
            Email: $"{subject}@example.test",
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

    private static Task<ProductRoleMapping> SeedRoleMappingAsync(
        InMemoryTenantMetadataStore store,
        TenantSeed seed,
        ProductUserId changedByProductUserId,
        string externalPrincipalId,
        ProductRole role,
        ProductScopeKind scopeKind = ProductScopeKind.Organization,
        string? scopeId = null)
    {
        return store.SeedProductRoleMappingAsync(
            seed.Organization.CustomerOrganizationId,
            new SeedProductRoleMappingRequest(
                IdentityTenantId: seed.IdentityTenant.IdentityTenantId,
                ExternalPrincipalType: ExternalPrincipalType.GroupObjectId,
                ExternalPrincipalId: externalPrincipalId,
                ProductRole: role,
                ScopeKind: scopeKind,
                ScopeId: scopeId,
                EffectiveFromUtc: Now,
                EffectiveToUtc: null,
                ChangedByProductUserId: changedByProductUserId,
                ActorEffectiveRole: ProductRole.PlatformAdmin,
                CorrelationId: $"seed-{Guid.NewGuid():N}",
                AuditEventId: $"audit-seed-{Guid.NewGuid():N}"));
    }

    private static async Task<PricingBasisRecord> SeedPricingCandidateAsync(
        InMemoryTenantMetadataStore store,
        TenantSeed seed,
        string modelName,
        string auditEventId)
    {
        await store.RecordGovernanceAuditEventAsync(
            seed.Organization.CustomerOrganizationId,
            new CreateGovernanceAuditEventRequest(
                auditEventId,
                ActorProductUserId: null,
                EffectiveRole: null,
                ProductAuthorizationAction.PricingManage,
                new ProductScope(ProductScopeKind.Pricing, ScopeId: "pricing-refresh"),
                Decision: "created",
                DenialReason: null,
                CorrelationId: "pricing-refresh-test",
                EvidenceMetadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["evidence_kind"] = "admin_operation",
                    ["operation"] = "pricing_seed_refresh",
                    ["result"] = "candidate",
                    ["pricing_basis_id"] = "pending",
                    ["pricing_version"] = "openai-20260615",
                    ["provider_name"] = "openai",
                    ["model_name"] = modelName,
                    ["token_type"] = "input",
                    ["billing_route"] = "standard",
                    ["source_kind"] = "automated_seed",
                    ["review_state"] = "candidate"
                }));

        return await store.CreatePricingBasisRecordAsync(
            new CreatePricingBasisRecordRequest(
                seed.Organization.CustomerOrganizationId,
                "codex-cli",
                "openai",
                modelName,
                PricingTokenType.Input,
                "standard",
                "USD",
                1.25m,
                "openai-20260615",
                PricingSourceKind.AutomatedSeed,
                PricingReviewState.Candidate,
                Now,
                EffectiveToUtc: null,
                auditEventId,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["source_url"] = "https://developers.openai.com/api/docs/pricing",
                    ["source_retrieved_at_utc"] = Now.ToString("O"),
                    ["provider_document_version"] = "openai-api-pricing",
                    ["provider_sku_name"] = modelName,
                    ["billing_route"] = "standard"
                }));
    }

    private static async Task<IssuedScopedIngestionCredential> IssueCredentialAsync(
        InMemoryTenantMetadataStore store,
        TenantSeed seed,
        string harnessSetupProfileId,
        ProductUserId? productUserId = null)
    {
        var lifecycle = new ScopedIngestionCredentialLifecycleService(store, new StaticTenantMetadataClock(Now));
        var admin = await store.CreateProductUserAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            new CreateProductUserRequest(
                ExternalSubjectId: $"admin-{Guid.NewGuid():N}",
                DisplayLabel: "admin",
                Email: "admin@example.test"));
        var resolvedProductUserId = productUserId ?? (await store.CreateProductUserAsync(
                seed.Organization.CustomerOrganizationId,
                seed.IdentityTenant.IdentityTenantId,
                new CreateProductUserRequest(
                    ExternalSubjectId: $"developer-{Guid.NewGuid():N}",
                    DisplayLabel: "developer",
                    Email: "developer@example.test")))
            .ProductUserId;

        return await lifecycle.CreateAsync(
            seed.Organization.CustomerOrganizationId,
            new IssueScopedIngestionCredentialRequest(
                HarnessSetupProfileId: harnessSetupProfileId,
                ProductUserId: resolvedProductUserId,
                AllowedHarness: CodingAgentHarness.CodexCli,
                AllowedScopes: [new ProductScope(ProductScopeKind.Organization, ScopeId: null)],
                ExpiresAtUtc: Now.AddDays(30),
                CreatedByProductUserId: admin.ProductUserId,
                ActorEffectiveRole: ProductRole.PlatformAdmin,
                CorrelationId: $"credential-create-{Guid.NewGuid():N}",
                AuditEventId: $"audit-credential-create-{Guid.NewGuid():N}"));
    }

    private static async Task SeedUnavailableSessionAsync(
        InMemoryTenantMetadataStore store,
        ScopedIngestionCredential credential,
        string providerSessionIdHash)
    {
        var normalizedProviderSessionIdHash = ComputeSha256Hex(providerSessionIdHash);
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
            ConversationIdHash: normalizedProviderSessionIdHash,
            TurnIdHash: null,
            SourceEventId: null,
            TraceIdHash: null,
            SpanIdHash: null,
            ModelName: null,
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
            MetricState: "unavailable",
            MetricStatus: TokenMetricStatus.Unavailable,
            MetricConfidence: TokenMetricConfidence.Unavailable,
            SourceEvidenceKind: "harness_emitted",
            CorrelationId: $"correlation-{providerSessionIdHash}",
            DedupeKeyHash: ComputeSha256Hex($"dedupe-{providerSessionIdHash}"),
            IngestionVersionMetadata: new Dictionary<string, string>
            {
                ["schema_version"] = "2026-06-01",
                ["harness_version"] = "unavailable",
                ["contract_version"] = "2026-06-01"
            }));
        var session = await store.UpsertAgentSessionAsync(new CreateAgentSessionRecordRequest(
            CustomerOrganizationId: credential.CustomerOrganizationId,
            ProductUserId: credential.ProductUserId,
            HarnessSetupProfileId: credential.HarnessSetupProfileId,
            Harness: credential.AllowedHarness,
            ProviderSessionIdHash: normalizedProviderSessionIdHash,
            StartedAtUtc: Now,
            EndedAtUtc: null,
            SessionStatus: AgentSessionStatus.Active,
            RepositoryEvidenceState: RepositoryEvidenceState.Unavailable,
            ContentCaptureSummary: ContentCaptureSummary.MetadataOnly,
            RecommendationStatus: RecommendationStatus.NotStarted,
            TokenMetricStatus: TokenMetricStatus.Unavailable,
            TokenMetricConfidence: TokenMetricConfidence.Unavailable));

        await store.RecordTokenObservationAsync(new CreateTokenObservationRecordRequest(
            credential.CustomerOrganizationId,
            session.AgentSessionId,
            ModelInvocationId: null,
            TokenMetricName.InputTokens,
            Value: null,
            TokenMetricStatus.Unavailable,
            TokenMetricConfidence.Unavailable,
            TokenObservationSourceKind.Missing,
            envelope.TelemetryEnvelopeId));
    }

    private static async Task<AgentSessionRecord> SeedTokenSessionAsync(
        InMemoryTenantMetadataStore store,
        ScopedIngestionCredential credential,
        string providerSessionId,
        DateTimeOffset sourceEventTimestampUtc,
        IReadOnlyList<(TokenMetricName MetricName, long? Value, TokenMetricStatus Status, TokenMetricConfidence Confidence, string? ModelInvocationId)> observations)
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
            SourceEventTimestampUtc: sourceEventTimestampUtc,
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
            MetricState: observations.Any(observation => observation.Value is null) ? "unavailable" : "observed",
            MetricStatus: observations.Any(observation => observation.Status != TokenMetricStatus.Observed)
                ? TokenMetricStatus.Mixed
                : TokenMetricStatus.Observed,
            MetricConfidence: observations.Any(observation => observation.Confidence != TokenMetricConfidence.Observed)
                ? TokenMetricConfidence.Estimated
                : TokenMetricConfidence.Observed,
            SourceEvidenceKind: "harness_emitted",
            CorrelationId: $"correlation-{providerSessionId}",
            DedupeKeyHash: ComputeSha256Hex($"dedupe-{providerSessionId}"),
            IngestionVersionMetadata: new Dictionary<string, string>
            {
                ["schema_version"] = "2026-06-01",
                ["harness_version"] = "unavailable",
                ["contract_version"] = "2026-06-01"
            }));
        var session = await store.UpsertAgentSessionAsync(new CreateAgentSessionRecordRequest(
            CustomerOrganizationId: credential.CustomerOrganizationId,
            ProductUserId: credential.ProductUserId,
            HarnessSetupProfileId: credential.HarnessSetupProfileId,
            Harness: credential.AllowedHarness,
            ProviderSessionIdHash: providerSessionIdHash,
            StartedAtUtc: sourceEventTimestampUtc,
            EndedAtUtc: null,
            SessionStatus: AgentSessionStatus.Active,
            RepositoryEvidenceState: RepositoryEvidenceState.Unavailable,
            ContentCaptureSummary: ContentCaptureSummary.MetadataOnly,
            RecommendationStatus: RecommendationStatus.NotStarted,
            TokenMetricStatus: observations.Any(observation => observation.Status != TokenMetricStatus.Observed)
                ? TokenMetricStatus.Mixed
                : TokenMetricStatus.Observed,
            TokenMetricConfidence: observations.Any(observation => observation.Confidence != TokenMetricConfidence.Observed)
                ? TokenMetricConfidence.Estimated
                : TokenMetricConfidence.Observed));

        foreach (var observation in observations)
        {
            await store.RecordTokenObservationAsync(new CreateTokenObservationRecordRequest(
                credential.CustomerOrganizationId,
                session.AgentSessionId,
                observation.ModelInvocationId,
                observation.MetricName,
                observation.Value,
                observation.Status,
                observation.Confidence,
                observation.Value.HasValue ? TokenObservationSourceKind.CodexEvent : TokenObservationSourceKind.Missing,
                envelope.TelemetryEnvelopeId));
        }

        return session;
    }

    private static async Task<ContentReferenceRecord> SeedReviewRequiredContentReferenceAsync(
        InMemoryTenantMetadataStore store,
        TenantSeed seed,
        string providerSessionId = "content-review-api-session")
    {
        var credential = await IssueCredentialAsync(store, seed, $"profile-{providerSessionId}");
        var session = await SeedTokenSessionAsync(
            store,
            credential.Credential,
            providerSessionId,
            Now,
            [(TokenMetricName.InputTokens, 1_024, TokenMetricStatus.Observed, TokenMetricConfidence.Observed, null)]);
        var telemetryEnvelopeId = Assert.Single(session.SourceTelemetryEnvelopeIds);

        await store.RecordContentCandidateMetadataAsync(new CreateContentCandidateMetadataRequest(
            seed.Organization.CustomerOrganizationId,
            PolicyVersionId: "policy-content-v1",
            credential.Credential.ScopedIngestionCredentialId,
            credential.Credential.HarnessSetupProfileId,
            session.AgentSessionId,
            $"{telemetryEnvelopeId}:otlp.log.body",
            ContentClass.PromptSnippet,
            OriginalLength: "hello Ada".Length,
            ContentCapturePolicyDecision.CaptureAllowed,
            ContentCandidateEvidenceState.ReviewRequired,
            ContentRedactionStatus.ReviewRequired,
            ContentRetentionClass.Short,
            RecommendationUse.Disabled,
            ContentRedactionOutcome.ReviewRequired,
            RedactionDecisionReason: "pii_low_confidence",
            RedactionPipelineVersion: "content-redaction-pipeline-v1",
            ProductRuleVersion: "product-redaction-rules-v1"));

        return Assert.Single(await store.ListContentReferencesForSessionAsync(
            seed.Organization.CustomerOrganizationId,
            session.AgentSessionId));
    }

    private static string CreatePricingOverrideJson(decimal pricePerMillionTokens = 0.75m)
    {
        return $$"""
            {
              "harness": "codex-cli",
              "providerName": "openai",
              "modelName": "gpt-5",
              "tokenType": "input",
              "billingRoute": "enterprise_contract",
              "currency": "USD",
              "pricePerMillionTokens": {{pricePerMillionTokens}},
              "pricingVersion": "contoso-contract-2026",
              "effectiveFromUtc": "2026-06-15T12:00:00Z",
              "effectiveToUtc": null,
              "sourceMetadata": {
                "source_url": "https://contoso.example/pricing-contract",
                "source_retrieved_at_utc": "2026-06-15T12:00:00Z",
                "provider_document_version": "contoso-contract",
                "provider_sku_name": "gpt-5",
                "billing_route": "enterprise_contract"
              }
            }
            """;
    }

    private static Task<GovernanceAuditEvent> RecordRejectionAuditEventAsync(
        InMemoryTenantMetadataStore store,
        TenantSeed seed,
        string auditEventId,
        string correlationId,
        string reasonCode,
        string harnessSetupProfileId)
    {
        return store.RecordGovernanceAuditEventAsync(
            seed.Organization.CustomerOrganizationId,
            new CreateGovernanceAuditEventRequest(
                AuditEventId: auditEventId,
                ActorProductUserId: null,
                EffectiveRole: null,
                Action: ProductAuthorizationAction.TelemetryIngest,
                TargetScope: new ProductScope(ProductScopeKind.HarnessProfile, harnessSetupProfileId),
                Decision: "denied",
                DenialReason: ProductAuthorizationDenialReason.ScopeMismatch,
                CorrelationId: correlationId,
                EvidenceMetadata: CreateRejectionEvidence(reasonCode, harnessSetupProfileId)));
    }

    private static IReadOnlyDictionary<string, string> CreateRejectionEvidence(
        string reasonCode,
        string harnessSetupProfileId)
    {
        return new Dictionary<string, string>
        {
            ["evidence_kind"] = "ingestion_decision",
            ["operation"] = "ingestion_rejection",
            ["result"] = reasonCode,
            ["request_route"] = "/v1/logs",
            ["scope_kind"] = ProductScopeKind.HarnessProfile.ToString(),
            ["scope_id"] = harnessSetupProfileId
        };
    }

    private static string ComputeSha256Hex(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static async Task AssertProblemCodeAsync(HttpResponseMessage response, string expectedCode)
    {
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(expectedCode, body.RootElement.GetProperty("code").GetString());
    }

    private sealed record TenantSeed(CustomerOrganization Organization, IdentityTenant IdentityTenant);
}
