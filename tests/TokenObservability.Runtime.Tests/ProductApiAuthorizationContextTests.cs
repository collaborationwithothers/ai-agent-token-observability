using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TokenObservability.Api;
using TokenObservability.Domain.Authorization;
using TokenObservability.Domain.Tenancy;
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
