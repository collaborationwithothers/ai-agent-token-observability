using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TokenObservability.Api;
using TokenObservability.Domain.Authorization;
using TokenObservability.Domain.Ingestion;
using TokenObservability.Domain.Tenancy;
using TokenObservability.Infrastructure.Persistence;

namespace TokenObservability.Runtime.Tests;

public sealed class TenantScopeRejectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 17, 10, 45, 0, TimeSpan.Zero);

    [Fact]
    public async Task ProductApiRejectionUsesRequestCorrelationIdAndDoesNotLeakTenantIdentityOrSecretInputs()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        await CreateTenantAsync(store, "contoso", "contoso-tenant");
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateAuthorizedRequest(
            HttpMethod.Get,
            "/api/v1/me",
            "missing-tenant",
            CreateClaims(
                subject: "developer-subject-secret",
                groupObjectIds: ["secret-group-object"],
                appRoles: ["secret-app-role"]));
        request.Headers.Add("X-Correlation-Id", "tenant-rejection-correlation-001");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("tenant_context_required", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("tenant-rejection-correlation-001", body.RootElement.GetProperty("correlationId").GetString());

        var serializedBody = body.RootElement.GetRawText();
        Assert.DoesNotContain("missing-tenant", serializedBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("developer-subject-secret", serializedBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-group-object", serializedBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-app-role", serializedBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prompt", serializedBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("command", serializedBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("code fragment", serializedBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer", serializedBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProductApiRejectionDoesNotEchoUnsafeCorrelationId()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        await CreateTenantAsync(store, "contoso", "contoso-tenant");
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateAuthorizedRequest(
            HttpMethod.Get,
            "/api/v1/me",
            "missing-tenant",
            CreateClaims(subject: "developer-subject", groupObjectIds: ["developer-group"]));
        request.Headers.Add("X-Correlation-Id", "Bearer aito_live_secret prompt code command output");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var correlationId = body.RootElement.GetProperty("correlationId").GetString();
        var serializedBody = body.RootElement.GetRawText();

        Assert.NotEqual("Bearer aito_live_secret prompt code command output", correlationId);
        Assert.DoesNotContain("aito_live_secret", serializedBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prompt code command", serializedBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProductApiRejectsUnknownAndInactiveCustomerOrganizations()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var claims = CreateClaims(subject: "admin-subject", groupObjectIds: ["entra-admin-group"]);
        await store.SetCustomerOrganizationStatusAsync(
            seed.Organization.CustomerOrganizationId,
            CustomerOrganizationStatus.Deleted);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var unknownTenantRequest = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/me", "unknown", claims);
        using var deletedTenantRequest = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/me", "contoso", claims);

        using var unknownTenantResponse = await client.SendAsync(unknownTenantRequest);
        using var deletedTenantResponse = await client.SendAsync(deletedTenantRequest);

        Assert.Equal(HttpStatusCode.Forbidden, unknownTenantResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deletedTenantResponse.StatusCode);
        await AssertProblemCodeAsync(unknownTenantResponse, "tenant_context_required");
        await AssertProblemCodeAsync(deletedTenantResponse, "tenant_context_required");
    }

    [Fact]
    public async Task ProductApiRejectsMappedIdentityWhenTenantContextBelongsToAnotherCustomerOrganization()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var contoso = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var fabrikam = await CreateTenantAsync(store, "fabrikam", "fabrikam-tenant");
        var contosoAdminClaims = CreateClaims(subject: "admin-subject", groupObjectIds: ["shared-admin-group"]);
        var contosoAdmin = await store.ResolveProductUserFromAuthenticatedClaimsAsync(
            contoso.Organization.CustomerOrganizationId,
            contoso.IdentityTenant.IdentityTenantId,
            contosoAdminClaims);
        await SeedRoleMappingAsync(
            store,
            contoso,
            contosoAdmin.ProductUserId,
            "shared-admin-group",
            ProductRole.PlatformAdmin);
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/me", "fabrikam", contosoAdminClaims);
        request.Headers.Add("X-Correlation-Id", "wrong-tenant-context-001");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertProblemCodeAsync(response, "authorization_denied");

        var fabrikamAuditEvents = await store.ListGovernanceAuditEventsAsync(fabrikam.Organization.CustomerOrganizationId);
        var denial = Assert.Single(fabrikamAuditEvents, auditEvent => auditEvent.CorrelationId == "wrong-tenant-context-001");
        Assert.Equal(ProductAuthorizationDenialReason.InvalidTenant, denial.DenialReason);
        Assert.Equal(ProductAuthorizationAction.CurrentUserRead, denial.Action);
        Assert.Null(denial.ActorProductUserId);
    }

    [Fact]
    public async Task ProductApiRejectsMissingAndInsufficientProductRoleMappings()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var unmappedClaims = CreateClaims(subject: "unmapped-subject", groupObjectIds: ["unmapped-group"]);
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
        using var missingRoleRequest = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/me", "contoso", unmappedClaims);
        missingRoleRequest.Headers.Add("X-Correlation-Id", "missing-role-mapping-001");
        using var insufficientRoleRequest = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/audit-events", "contoso", developerClaims);
        insufficientRoleRequest.Headers.Add("X-Correlation-Id", "insufficient-role-mapping-001");

        using var missingRoleResponse = await client.SendAsync(missingRoleRequest);
        using var insufficientRoleResponse = await client.SendAsync(insufficientRoleRequest);

        Assert.Equal(HttpStatusCode.Forbidden, missingRoleResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, insufficientRoleResponse.StatusCode);
        await AssertProblemCodeAsync(missingRoleResponse, "authorization_denied");
        await AssertProblemCodeAsync(insufficientRoleResponse, "authorization_denied");

        var auditEvents = await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId);
        Assert.Contains(auditEvents, auditEvent =>
            auditEvent.CorrelationId == "missing-role-mapping-001" &&
            auditEvent.DenialReason == ProductAuthorizationDenialReason.MissingRoleMapping);
        Assert.Contains(auditEvents, auditEvent =>
            auditEvent.CorrelationId == "insufficient-role-mapping-001" &&
            auditEvent.DenialReason == ProductAuthorizationDenialReason.InsufficientRole);
    }

    [Fact]
    public async Task TenantScopedMetadataLookupsRejectCrossTenantAccess()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var contoso = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var fabrikam = await CreateTenantAsync(store, "fabrikam", "fabrikam-tenant");
        var contosoAdmin = await CreateProductUserAsync(store, contoso, "contoso-admin");
        var contosoDeveloper = await CreateProductUserAsync(store, contoso, "contoso-developer");
        await store.RecordGovernanceAuditEventAsync(
            contoso.Organization.CustomerOrganizationId,
            new CreateGovernanceAuditEventRequest(
                AuditEventId: "audit-contoso-only-001",
                ActorProductUserId: contosoAdmin.ProductUserId,
                EffectiveRole: ProductRole.PlatformAdmin,
                Action: ProductAuthorizationAction.IdentityManage,
                TargetScope: new ProductScope(ProductScopeKind.Organization, ScopeId: null),
                Decision: "created",
                DenialReason: null,
                CorrelationId: "tenant-scoped-audit-001",
                EvidenceMetadata: new Dictionary<string, string>
                {
                    ["evidence_kind"] = "admin_operation",
                    ["operation"] = "tenant_scope_rejection_test"
                }));
        var credential = await store.CreateScopedIngestionCredentialAsync(
            contoso.Organization.CustomerOrganizationId,
            new CreateScopedIngestionCredentialRequest(
                HarnessSetupProfileId: "profile-contoso-codex",
                ProductUserId: contosoDeveloper.ProductUserId,
                CredentialHash: "sha256:tenant-scope-credential",
                CredentialPrefix: "aito_live_tenant_scope",
                AllowedHarness: CodingAgentHarness.CodexCli,
                AllowedScopes: [new ProductScope(ProductScopeKind.Repository, "repo-contoso")],
                ExpiresAtUtc: Now.AddDays(30),
                CreatedByProductUserId: contosoAdmin.ProductUserId,
                ActorEffectiveRole: ProductRole.PlatformAdmin,
                CorrelationId: "tenant-scoped-credential-001",
                AuditEventId: "audit-credential-contoso-only-001"));

        var crossTenantProductUser = await store.FindProductUserByExternalSubjectAsync(
            fabrikam.Organization.CustomerOrganizationId,
            contoso.IdentityTenant.IdentityTenantId,
            "contoso-developer");
        var crossTenantAuditEvent = await store.FindGovernanceAuditEventAsync(
            fabrikam.Organization.CustomerOrganizationId,
            "audit-contoso-only-001");
        var crossTenantCredential = await store.FindScopedIngestionCredentialAsync(
            fabrikam.Organization.CustomerOrganizationId,
            credential.ScopedIngestionCredentialId);
        var crossTenantCredentialList = await store.ListScopedIngestionCredentialsAsync(
            fabrikam.Organization.CustomerOrganizationId);

        Assert.Null(crossTenantProductUser);
        Assert.Null(crossTenantAuditEvent);
        Assert.Null(crossTenantCredential);
        Assert.Empty(crossTenantCredentialList);
    }

    [Fact]
    public async Task AuthorizationDenialAuditMetadataDoesNotStoreSecretsOrRawContent()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");

        var decision = await store.AuthorizeProductActionAsync(
            seed.Organization.CustomerOrganizationId,
            seed.IdentityTenant.IdentityTenantId,
            CreateClaims(
                subject: "unmapped-subject-with-secret",
                groupObjectIds: ["unmapped-group-with-secret"]),
            ProductAuthorizationAction.IdentityManage,
            new ProductScope(ProductScopeKind.Organization, ScopeId: null),
            "authorization-denial-sanitized-001");

        Assert.False(decision.IsAllowed);
        var auditEvent = Assert.Single(await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId));
        Assert.Equal(ProductAuthorizationDenialReason.MissingRoleMapping, auditEvent.DenialReason);
        Assert.Equal("authorization_decision", auditEvent.EvidenceMetadata["evidence_kind"]);
        Assert.Equal("denied", auditEvent.EvidenceMetadata["result"]);
        Assert.Equal("MissingRoleMapping", auditEvent.EvidenceMetadata["denial_reason"]);
        Assert.DoesNotContain(auditEvent.EvidenceMetadata.Keys, key => key.Contains("prompt", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(auditEvent.EvidenceMetadata.Keys, key => key.Contains("code", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(auditEvent.EvidenceMetadata.Keys, key => key.Contains("command", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(auditEvent.EvidenceMetadata.Keys, key => key.Contains("tool", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(auditEvent.EvidenceMetadata.Values, value => value.Contains("unmapped-subject-with-secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(auditEvent.EvidenceMetadata.Values, value => value.Contains("unmapped-group-with-secret", StringComparison.OrdinalIgnoreCase));
    }

    private static WebApplicationFactory<TokenObservabilityApiAssemblyMarker> CreateFactory(InMemoryTenantMetadataStore store)
    {
        return new WebApplicationFactory<TokenObservabilityApiAssemblyMarker>()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<InMemoryTenantMetadataStore>();
                services.AddSingleton(store);
            }));
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

    private static Task<ProductRoleMapping> SeedRoleMappingAsync(
        InMemoryTenantMetadataStore store,
        TenantSeed seed,
        ProductUserId changedByProductUserId,
        string externalPrincipalId,
        ProductRole role)
    {
        return store.SeedProductRoleMappingAsync(
            seed.Organization.CustomerOrganizationId,
            new SeedProductRoleMappingRequest(
                IdentityTenantId: seed.IdentityTenant.IdentityTenantId,
                ExternalPrincipalType: ExternalPrincipalType.GroupObjectId,
                ExternalPrincipalId: externalPrincipalId,
                ProductRole: role,
                ScopeKind: ProductScopeKind.Organization,
                ScopeId: null,
                EffectiveFromUtc: Now,
                EffectiveToUtc: null,
                ChangedByProductUserId: changedByProductUserId,
                ActorEffectiveRole: ProductRole.PlatformAdmin,
                CorrelationId: $"seed-{Guid.NewGuid():N}",
                AuditEventId: $"audit-seed-{Guid.NewGuid():N}"));
    }

    private static async Task AssertProblemCodeAsync(HttpResponseMessage response, string expectedCode)
    {
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(expectedCode, body.RootElement.GetProperty("code").GetString());
    }

    private sealed record TenantSeed(CustomerOrganization Organization, IdentityTenant IdentityTenant);
}
