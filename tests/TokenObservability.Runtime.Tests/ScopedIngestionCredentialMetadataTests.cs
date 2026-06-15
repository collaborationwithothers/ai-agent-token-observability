using TokenObservability.Domain.Authorization;
using TokenObservability.Domain.Ingestion;
using TokenObservability.Domain.Tenancy;
using TokenObservability.Infrastructure.Persistence;

namespace TokenObservability.Runtime.Tests;

public sealed class ScopedIngestionCredentialMetadataTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 16, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateScopedIngestionCredentialStoresMetadataAndAuditsCreationWithoutSecretMaterial()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var admin = await CreateProductUserAsync(store, seed, "admin-subject");
        var developer = await CreateProductUserAsync(store, seed, "developer-subject");

        var credential = await store.CreateScopedIngestionCredentialAsync(
            seed.Organization.CustomerOrganizationId,
            new CreateScopedIngestionCredentialRequest(
                HarnessSetupProfileId: "profile-codex-cli-eastus2",
                ProductUserId: developer.ProductUserId,
                CredentialHash: "sha256:credential-verifier-001",
                CredentialPrefix: "aito_live_1234",
                AllowedHarness: CodingAgentHarness.CodexCli,
                AllowedScopes: [new ProductScope(ProductScopeKind.Repository, "repo-001")],
                ExpiresAtUtc: Now.AddDays(30),
                CreatedByProductUserId: admin.ProductUserId,
                ActorEffectiveRole: ProductRole.PlatformAdmin,
                CorrelationId: "credential-create-001",
                AuditEventId: "audit-credential-create-001"));

        var found = await store.FindScopedIngestionCredentialAsync(
            seed.Organization.CustomerOrganizationId,
            credential.ScopedIngestionCredentialId);
        var auditEvent = await store.FindGovernanceAuditEventAsync(
            seed.Organization.CustomerOrganizationId,
            credential.AuditEventIds.Single());

        Assert.NotNull(found);
        Assert.Equal(seed.Organization.CustomerOrganizationId, credential.CustomerOrganizationId);
        Assert.Equal(developer.ProductUserId, credential.ProductUserId);
        Assert.Equal("profile-codex-cli-eastus2", credential.HarnessSetupProfileId);
        Assert.Equal(CodingAgentHarness.CodexCli, credential.AllowedHarness);
        Assert.Equal(ScopedIngestionCredentialStatus.Active, credential.Status);
        Assert.Equal(Now, credential.CreatedAtUtc);
        Assert.Equal(Now.AddDays(30), credential.ExpiresAtUtc);
        Assert.Null(credential.LastUsedAtUtc);
        Assert.Null(credential.RotatedAtUtc);
        Assert.Null(credential.RevokedAtUtc);
        Assert.DoesNotContain("secret", credential.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(auditEvent);
        Assert.Equal(ProductAuthorizationAction.IngestionCredentialManage, auditEvent.Action);
        Assert.Equal("created", auditEvent.Decision);
        Assert.Equal("scoped_ingestion_credential_create", auditEvent.EvidenceMetadata["operation"]);
    }

    [Fact]
    public async Task ScopedIngestionCredentialQueriesAreTenantScoped()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var contoso = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var fabrikam = await CreateTenantAsync(store, "fabrikam", "fabrikam-tenant");
        var contosoAdmin = await CreateProductUserAsync(store, contoso, "contoso-admin");
        var contosoDeveloper = await CreateProductUserAsync(store, contoso, "contoso-developer");

        var credential = await store.CreateScopedIngestionCredentialAsync(
            contoso.Organization.CustomerOrganizationId,
            new CreateScopedIngestionCredentialRequest(
                HarnessSetupProfileId: "profile-contoso-codex",
                ProductUserId: contosoDeveloper.ProductUserId,
                CredentialHash: "sha256:contoso-credential",
                CredentialPrefix: "aito_live_contoso",
                AllowedHarness: CodingAgentHarness.CodexCli,
                AllowedScopes: [new ProductScope(ProductScopeKind.Team, "team-contoso")],
                ExpiresAtUtc: Now.AddDays(10),
                CreatedByProductUserId: contosoAdmin.ProductUserId,
                ActorEffectiveRole: ProductRole.PlatformAdmin,
                CorrelationId: "credential-create-contoso",
                AuditEventId: "audit-credential-create-contoso"));

        var crossTenantFind = await store.FindScopedIngestionCredentialAsync(
            fabrikam.Organization.CustomerOrganizationId,
            credential.ScopedIngestionCredentialId);
        var crossTenantList = await store.ListScopedIngestionCredentialsAsync(
            fabrikam.Organization.CustomerOrganizationId);

        Assert.Null(crossTenantFind);
        Assert.Empty(crossTenantList);
    }

    [Fact]
    public async Task ScopedIngestionCredentialLifecycleTransitionsRecordAuditEvents()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var admin = await CreateProductUserAsync(store, seed, "admin-subject");
        var developer = await CreateProductUserAsync(store, seed, "developer-subject");
        var credential = await CreateCredentialAsync(store, seed, admin, developer);

        var pendingRotation = await store.MarkScopedIngestionCredentialPendingRotationAsync(
            seed.Organization.CustomerOrganizationId,
            credential.ScopedIngestionCredentialId,
            new ScopedIngestionCredentialLifecycleRequest(
                ChangedByProductUserId: admin.ProductUserId,
                ActorEffectiveRole: ProductRole.PlatformAdmin,
                CorrelationId: "credential-pending-rotation-001",
                AuditEventId: "audit-credential-pending-rotation-001"));
        var rotated = await store.RotateScopedIngestionCredentialAsync(
            seed.Organization.CustomerOrganizationId,
            credential.ScopedIngestionCredentialId,
            new RotateScopedIngestionCredentialRequest(
                CredentialHash: "sha256:credential-verifier-rotated",
                CredentialPrefix: "aito_live_rotated",
                ExpiresAtUtc: Now.AddDays(60),
                ChangedByProductUserId: admin.ProductUserId,
                ActorEffectiveRole: ProductRole.PlatformAdmin,
                CorrelationId: "credential-rotate-001",
                AuditEventId: "audit-credential-rotate-001"));
        var disabled = await store.DisableScopedIngestionCredentialAsync(
            seed.Organization.CustomerOrganizationId,
            credential.ScopedIngestionCredentialId,
            new ScopedIngestionCredentialLifecycleRequest(
                ChangedByProductUserId: admin.ProductUserId,
                ActorEffectiveRole: ProductRole.PlatformAdmin,
                CorrelationId: "credential-disable-001",
                AuditEventId: "audit-credential-disable-001"));
        var revoked = await store.RevokeScopedIngestionCredentialAsync(
            seed.Organization.CustomerOrganizationId,
            credential.ScopedIngestionCredentialId,
            new ScopedIngestionCredentialLifecycleRequest(
                ChangedByProductUserId: admin.ProductUserId,
                ActorEffectiveRole: ProductRole.PlatformAdmin,
                CorrelationId: "credential-revoke-001",
                AuditEventId: "audit-credential-revoke-001"));

        Assert.Equal(ScopedIngestionCredentialStatus.PendingRotation, pendingRotation.Status);
        Assert.Equal(ScopedIngestionCredentialStatus.Active, rotated.Status);
        Assert.Equal(Now, rotated.RotatedAtUtc);
        Assert.Equal(ScopedIngestionCredentialStatus.Disabled, disabled.Status);
        Assert.Equal(ScopedIngestionCredentialStatus.Revoked, revoked.Status);
        Assert.Equal(Now, revoked.RevokedAtUtc);
        Assert.Equal(
            [
                "audit-credential-create-001",
                "audit-credential-pending-rotation-001",
                "audit-credential-rotate-001",
                "audit-credential-disable-001",
                "audit-credential-revoke-001"
            ],
            revoked.AuditEventIds);
    }

    [Fact]
    public async Task ScopedIngestionCredentialRejectsRawCredentialMaterialAsHash()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var admin = await CreateProductUserAsync(store, seed, "admin-subject");
        var developer = await CreateProductUserAsync(store, seed, "developer-subject");

        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateScopedIngestionCredentialAsync(
            seed.Organization.CustomerOrganizationId,
            new CreateScopedIngestionCredentialRequest(
                HarnessSetupProfileId: "profile-codex-cli-eastus2",
                ProductUserId: developer.ProductUserId,
                CredentialHash: "Bearer aito_live_secret_value",
                CredentialPrefix: "aito_live_1234",
                AllowedHarness: CodingAgentHarness.CodexCli,
                AllowedScopes: [new ProductScope(ProductScopeKind.Repository, "repo-001")],
                ExpiresAtUtc: Now.AddDays(30),
                CreatedByProductUserId: admin.ProductUserId,
                ActorEffectiveRole: ProductRole.PlatformAdmin,
                CorrelationId: "credential-create-raw-secret",
                AuditEventId: "audit-credential-create-raw-secret")));
        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateScopedIngestionCredentialAsync(
            seed.Organization.CustomerOrganizationId,
            new CreateScopedIngestionCredentialRequest(
                HarnessSetupProfileId: "profile-codex-cli-eastus2",
                ProductUserId: developer.ProductUserId,
                CredentialHash: "aito_live_1234",
                CredentialPrefix: "aito_live_1234",
                AllowedHarness: CodingAgentHarness.CodexCli,
                AllowedScopes: [new ProductScope(ProductScopeKind.Repository, "repo-001")],
                ExpiresAtUtc: Now.AddDays(30),
                CreatedByProductUserId: admin.ProductUserId,
                ActorEffectiveRole: ProductRole.PlatformAdmin,
                CorrelationId: "credential-create-prefix-as-hash",
                AuditEventId: "audit-credential-create-prefix-as-hash")));
    }

    [Fact]
    public async Task ScopedIngestionCredentialRejectsUnsupportedOrAmbiguousScopes()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var admin = await CreateProductUserAsync(store, seed, "admin-subject");
        var developer = await CreateProductUserAsync(store, seed, "developer-subject");

        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateScopedIngestionCredentialAsync(
            seed.Organization.CustomerOrganizationId,
            new CreateScopedIngestionCredentialRequest(
                HarnessSetupProfileId: "profile-codex-cli-eastus2",
                ProductUserId: developer.ProductUserId,
                CredentialHash: "sha256:credential-verifier-unsupported-scope",
                CredentialPrefix: "aito_live_unsupported",
                AllowedHarness: CodingAgentHarness.CodexCli,
                AllowedScopes: [new ProductScope(ProductScopeKind.Self, null)],
                ExpiresAtUtc: Now.AddDays(30),
                CreatedByProductUserId: admin.ProductUserId,
                ActorEffectiveRole: ProductRole.PlatformAdmin,
                CorrelationId: "credential-create-self-scope",
                AuditEventId: "audit-credential-create-self-scope")));
        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateScopedIngestionCredentialAsync(
            seed.Organization.CustomerOrganizationId,
            new CreateScopedIngestionCredentialRequest(
                HarnessSetupProfileId: "profile-codex-cli-eastus2",
                ProductUserId: developer.ProductUserId,
                CredentialHash: "sha256:credential-verifier-missing-repo",
                CredentialPrefix: "aito_live_missing",
                AllowedHarness: CodingAgentHarness.CodexCli,
                AllowedScopes: [new ProductScope(ProductScopeKind.Repository, null)],
                ExpiresAtUtc: Now.AddDays(30),
                CreatedByProductUserId: admin.ProductUserId,
                ActorEffectiveRole: ProductRole.PlatformAdmin,
                CorrelationId: "credential-create-missing-repo",
                AuditEventId: "audit-credential-create-missing-repo")));
        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateScopedIngestionCredentialAsync(
            seed.Organization.CustomerOrganizationId,
            new CreateScopedIngestionCredentialRequest(
                HarnessSetupProfileId: "profile-codex-cli-eastus2",
                ProductUserId: developer.ProductUserId,
                CredentialHash: "sha256:credential-verifier-org-id",
                CredentialPrefix: "aito_live_org",
                AllowedHarness: CodingAgentHarness.CodexCli,
                AllowedScopes: [new ProductScope(ProductScopeKind.Organization, "tenant-id-should-not-be-here")],
                ExpiresAtUtc: Now.AddDays(30),
                CreatedByProductUserId: admin.ProductUserId,
                ActorEffectiveRole: ProductRole.PlatformAdmin,
                CorrelationId: "credential-create-org-id",
                AuditEventId: "audit-credential-create-org-id")));
    }

    [Fact]
    public async Task RevokedScopedIngestionCredentialCannotBeRotatedBackToActive()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var admin = await CreateProductUserAsync(store, seed, "admin-subject");
        var developer = await CreateProductUserAsync(store, seed, "developer-subject");
        var credential = await CreateCredentialAsync(store, seed, admin, developer);

        await store.RevokeScopedIngestionCredentialAsync(
            seed.Organization.CustomerOrganizationId,
            credential.ScopedIngestionCredentialId,
            new ScopedIngestionCredentialLifecycleRequest(
                ChangedByProductUserId: admin.ProductUserId,
                ActorEffectiveRole: ProductRole.PlatformAdmin,
                CorrelationId: "credential-revoke-before-rotate",
                AuditEventId: "audit-credential-revoke-before-rotate"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RotateScopedIngestionCredentialAsync(
            seed.Organization.CustomerOrganizationId,
            credential.ScopedIngestionCredentialId,
            new RotateScopedIngestionCredentialRequest(
                CredentialHash: "sha256:credential-verifier-after-revoke",
                CredentialPrefix: "aito_live_after_revoke",
                ExpiresAtUtc: Now.AddDays(60),
                ChangedByProductUserId: admin.ProductUserId,
                ActorEffectiveRole: ProductRole.PlatformAdmin,
                CorrelationId: "credential-rotate-after-revoke",
                AuditEventId: "audit-credential-rotate-after-revoke")));
    }

    [Fact]
    public async Task ExpiredScopedIngestionCredentialAndFailedAccessAreAuditedAtMetadataLevel()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var admin = await CreateProductUserAsync(store, seed, "admin-subject");
        var developer = await CreateProductUserAsync(store, seed, "developer-subject");
        var credential = await CreateCredentialAsync(store, seed, admin, developer);

        var expired = await store.MarkScopedIngestionCredentialExpiredAsync(
            seed.Organization.CustomerOrganizationId,
            credential.ScopedIngestionCredentialId,
            new ScopedIngestionCredentialLifecycleRequest(
                ChangedByProductUserId: admin.ProductUserId,
                ActorEffectiveRole: ProductRole.PlatformAdmin,
                CorrelationId: "credential-expired-001",
                AuditEventId: "audit-credential-expired-001"));
        await store.RecordScopedIngestionCredentialFailedAccessAsync(
            seed.Organization.CustomerOrganizationId,
            credential.ScopedIngestionCredentialId,
            "expired",
            "credential-failed-access-001");

        var auditEvents = await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId);

        Assert.Equal(ScopedIngestionCredentialStatus.Expired, expired.Status);
        Assert.Contains(auditEvents, auditEvent =>
            auditEvent.CorrelationId == "credential-failed-access-001" &&
            auditEvent.Decision == "denied" &&
            auditEvent.Action == ProductAuthorizationAction.TelemetryIngest);
    }

    [Fact]
    public async Task ActiveScopedIngestionCredentialHashMustBeGloballyUnique()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var contoso = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var fabrikam = await CreateTenantAsync(store, "fabrikam", "fabrikam-tenant");
        var contosoAdmin = await CreateProductUserAsync(store, contoso, "contoso-admin");
        var contosoDeveloper = await CreateProductUserAsync(store, contoso, "contoso-developer");
        var fabrikamAdmin = await CreateProductUserAsync(store, fabrikam, "fabrikam-admin");
        var fabrikamDeveloper = await CreateProductUserAsync(store, fabrikam, "fabrikam-developer");
        var duplicateHash = "sha256:globally-duplicate-credential";

        await store.CreateScopedIngestionCredentialAsync(
            contoso.Organization.CustomerOrganizationId,
            new CreateScopedIngestionCredentialRequest(
                HarnessSetupProfileId: "profile-contoso-codex",
                ProductUserId: contosoDeveloper.ProductUserId,
                CredentialHash: duplicateHash,
                CredentialPrefix: "aito_live_global_one",
                AllowedHarness: CodingAgentHarness.CodexCli,
                AllowedScopes: [new ProductScope(ProductScopeKind.Organization, ScopeId: null)],
                ExpiresAtUtc: Now.AddDays(30),
                CreatedByProductUserId: contosoAdmin.ProductUserId,
                ActorEffectiveRole: ProductRole.PlatformAdmin,
                CorrelationId: "credential-global-hash-001",
                AuditEventId: "audit-credential-global-hash-001"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.CreateScopedIngestionCredentialAsync(
                fabrikam.Organization.CustomerOrganizationId,
                new CreateScopedIngestionCredentialRequest(
                    HarnessSetupProfileId: "profile-fabrikam-codex",
                    ProductUserId: fabrikamDeveloper.ProductUserId,
                    CredentialHash: duplicateHash,
                    CredentialPrefix: "aito_live_global_two",
                    AllowedHarness: CodingAgentHarness.CodexCli,
                    AllowedScopes: [new ProductScope(ProductScopeKind.Organization, ScopeId: null)],
                    ExpiresAtUtc: Now.AddDays(30),
                    CreatedByProductUserId: fabrikamAdmin.ProductUserId,
                    ActorEffectiveRole: ProductRole.PlatformAdmin,
                    CorrelationId: "credential-global-hash-002",
                    AuditEventId: "audit-credential-global-hash-002")));

        Assert.Equal("Active scoped ingestion credential hash already exists.", exception.Message);
    }

    [Fact]
    public void PostgreSqlTenantMetadataMigrationIncludesScopedIngestionCredentialMetadataWithoutPlainSecret()
    {
        var migration = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "TokenObservability.Infrastructure",
            "Persistence",
            "PostgreSql",
            "Migrations",
            "0001_tenant_metadata.sql"));

        Assert.Contains("CREATE TABLE IF NOT EXISTS scoped_ingestion_credential", migration);
        Assert.Contains("scoped_ingestion_credential_id uuid PRIMARY KEY", migration);
        Assert.Contains("customer_organization_id uuid NOT NULL", migration);
        Assert.Contains("harness_setup_profile_id text NOT NULL", migration);
        Assert.Contains("product_user_id uuid NOT NULL", migration);
        Assert.Contains("credential_hash text NOT NULL", migration);
        Assert.Contains("credential_prefix text NULL", migration);
        Assert.Contains("allowed_harness text NOT NULL", migration);
        Assert.Contains("allowed_scopes_json jsonb NOT NULL", migration);
        Assert.Contains("status text NOT NULL", migration);
        Assert.Contains("rotated_at_utc timestamptz NULL", migration);
        Assert.Contains("revoked_at_utc timestamptz NULL", migration);
        Assert.Contains("audit_event_ids_json jsonb NOT NULL", migration);
        Assert.Contains("ck_scoped_ingestion_credential_status CHECK (status IN ('active', 'disabled', 'revoked', 'expired', 'pending_rotation'))", migration);
        Assert.Contains("ck_scoped_ingestion_credential_allowed_harness CHECK (allowed_harness IN ('codex_cli'))", migration);
        Assert.Contains("ix_scoped_ingestion_credential_customer_status", migration);
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS ux_scoped_ingestion_credential_active_hash", migration);
        Assert.Contains("ON scoped_ingestion_credential (credential_hash)", migration);
        Assert.DoesNotContain("credential_secret", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret_value", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("plain", migration, StringComparison.OrdinalIgnoreCase);
    }

    private static Task<ScopedIngestionCredential> CreateCredentialAsync(
        InMemoryTenantMetadataStore store,
        TenantSeed seed,
        ProductUser admin,
        ProductUser developer,
        DateTimeOffset? expiresAtUtc = null)
    {
        return store.CreateScopedIngestionCredentialAsync(
            seed.Organization.CustomerOrganizationId,
            new CreateScopedIngestionCredentialRequest(
                HarnessSetupProfileId: "profile-codex-cli-eastus2",
                ProductUserId: developer.ProductUserId,
                CredentialHash: "sha256:credential-verifier-001",
                CredentialPrefix: "aito_live_1234",
                AllowedHarness: CodingAgentHarness.CodexCli,
                AllowedScopes: [new ProductScope(ProductScopeKind.Repository, "repo-001")],
                ExpiresAtUtc: expiresAtUtc ?? Now.AddDays(30),
                CreatedByProductUserId: admin.ProductUserId,
                ActorEffectiveRole: ProductRole.PlatformAdmin,
                CorrelationId: "credential-create-001",
                AuditEventId: "audit-credential-create-001"));
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
