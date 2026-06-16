using System.Net;
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
using TokenObservability.Domain.Tenancy;
using TokenObservability.Infrastructure.Ingestion;
using TokenObservability.Infrastructure.Persistence;

namespace TokenObservability.Runtime.Tests;

public sealed class ProductApiAuthorizationContextTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

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
        var envelope = await store.RecordTelemetryEnvelopeAsync(new CreateTelemetryEnvelopeRecordRequest(
            CustomerOrganizationId: seed.Organization.CustomerOrganizationId,
            HarnessSetupProfileId: credential.Credential.HarnessSetupProfileId,
            ScopedIngestionCredentialId: credential.Credential.ScopedIngestionCredentialId,
            ProductUserId: credential.Credential.ProductUserId,
            Harness: CodingAgentHarness.CodexCli,
            SchemaVersion: "2026-06-01",
            SignalType: "metrics",
            SourceEventName: "codex.api_request",
            SourceEventTimestampUtc: Now,
            ConversationIdHash: "conversation-api-001",
            ModelName: "gpt-5",
            ContentPolicyDecision: "metadata_only",
            RoutingDecision: "metadata_store",
            MetricStatus: TokenMetricStatus.Mixed,
            MetricConfidence: TokenMetricConfidence.Estimated,
            DedupeKeyHash: "dedupe-api-001"));
        var session = await store.UpsertAgentSessionAsync(new CreateAgentSessionRecordRequest(
            CustomerOrganizationId: seed.Organization.CustomerOrganizationId,
            ProductUserId: credential.Credential.ProductUserId,
            HarnessSetupProfileId: credential.Credential.HarnessSetupProfileId,
            Harness: CodingAgentHarness.CodexCli,
            ProviderSessionIdHash: "conversation-api-001",
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
                            ["ProductApi:Readiness:ProductMetadataStore"] = "true",
                            ["ProductApi:Readiness:TelemetryBackends"] = "true",
                            ["ProductApi:Readiness:ContentStore"] = "true",
                            ["ProductApi:Readiness:RecommendationDependencies"] = "true",
                            ["ProductApi:Readiness:AuthorizationEnforcement"] = "true"
                        });
                    });
                }

                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<InMemoryTenantMetadataStore>();
                    services.AddSingleton(store);
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
        var envelope = await store.RecordTelemetryEnvelopeAsync(new CreateTelemetryEnvelopeRecordRequest(
            CustomerOrganizationId: credential.CustomerOrganizationId,
            HarnessSetupProfileId: credential.HarnessSetupProfileId,
            ScopedIngestionCredentialId: credential.ScopedIngestionCredentialId,
            ProductUserId: credential.ProductUserId,
            Harness: credential.AllowedHarness,
            SchemaVersion: "2026-06-01",
            SignalType: "metrics",
            SourceEventName: null,
            SourceEventTimestampUtc: Now,
            ConversationIdHash: providerSessionIdHash,
            ModelName: null,
            ContentPolicyDecision: "metadata_only",
            RoutingDecision: "metadata_store",
            MetricStatus: TokenMetricStatus.Unavailable,
            MetricConfidence: TokenMetricConfidence.Unavailable,
            DedupeKeyHash: $"dedupe-{providerSessionIdHash}"));
        var session = await store.UpsertAgentSessionAsync(new CreateAgentSessionRecordRequest(
            CustomerOrganizationId: credential.CustomerOrganizationId,
            ProductUserId: credential.ProductUserId,
            HarnessSetupProfileId: credential.HarnessSetupProfileId,
            Harness: credential.AllowedHarness,
            ProviderSessionIdHash: providerSessionIdHash,
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

    private static async Task AssertProblemCodeAsync(HttpResponseMessage response, string expectedCode)
    {
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(expectedCode, body.RootElement.GetProperty("code").GetString());
    }

    private sealed record TenantSeed(CustomerOrganization Organization, IdentityTenant IdentityTenant);
}
