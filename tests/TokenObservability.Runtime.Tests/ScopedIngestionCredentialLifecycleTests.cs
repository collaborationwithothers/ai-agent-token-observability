using TokenObservability.Domain.Authorization;
using TokenObservability.Domain.Ingestion;
using TokenObservability.Domain.Tenancy;
using TokenObservability.Infrastructure.Ingestion;
using TokenObservability.Infrastructure.Persistence;

namespace TokenObservability.Runtime.Tests;

public sealed class ScopedIngestionCredentialLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 17, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateIssuesOneTimeSecretAndStoresOnlyVerifierMetadata()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var lifecycle = new ScopedIngestionCredentialLifecycleService(store);
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var admin = await CreateProductUserAsync(store, seed, "admin-subject");
        var developer = await CreateProductUserAsync(store, seed, "developer-subject");

        var issued = await lifecycle.CreateAsync(
            seed.Organization.CustomerOrganizationId,
            new IssueScopedIngestionCredentialRequest(
                HarnessSetupProfileId: "profile-codex-cli-eastus2",
                ProductUserId: developer.ProductUserId,
                AllowedHarness: CodingAgentHarness.CodexCli,
                AllowedScopes: [new ProductScope(ProductScopeKind.Repository, "repo-001")],
                ExpiresAtUtc: Now.AddDays(30),
                CreatedByProductUserId: admin.ProductUserId,
                ActorEffectiveRole: ProductRole.PlatformAdmin,
                CorrelationId: "credential-create-001",
                AuditEventId: "audit-credential-create-001"));

        var stored = await store.FindScopedIngestionCredentialAsync(
            seed.Organization.CustomerOrganizationId,
            issued.Credential.ScopedIngestionCredentialId);
        var auditEvent = Assert.Single(await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId));

        Assert.NotNull(stored);
        Assert.StartsWith("aito_live_", issued.Secret, StringComparison.Ordinal);
        Assert.StartsWith("sha256:", stored.CredentialHash, StringComparison.Ordinal);
        Assert.NotEqual(issued.Secret, stored.CredentialHash);
        Assert.Equal(issued.Secret[..16], stored.CredentialPrefix);
        Assert.DoesNotContain(issued.Secret, stored.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(issued.Secret, auditEvent.ToString(), StringComparison.Ordinal);
        Assert.Equal(ProductAuthorizationAction.IngestionCredentialManage, auditEvent.Action);
        Assert.Equal("created", auditEvent.Decision);
    }

    [Fact]
    public async Task ValidateAcceptsActiveCredentialForExpectedTenantHarnessAndProfile()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var lifecycle = new ScopedIngestionCredentialLifecycleService(store);
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var admin = await CreateProductUserAsync(store, seed, "admin-subject");
        var developer = await CreateProductUserAsync(store, seed, "developer-subject");
        var issued = await IssueCredentialAsync(store, lifecycle, seed, admin, developer);

        var result = await lifecycle.ValidateAsync(new ValidateScopedIngestionCredentialRequest(
            CustomerOrganizationId: seed.Organization.CustomerOrganizationId,
            Secret: issued.Secret,
            DeclaredHarness: "codex-cli",
            HarnessSetupProfileId: "profile-codex-cli-eastus2",
            CorrelationId: "credential-validate-001"));

        Assert.True(result.IsValid);
        Assert.Equal(ScopedIngestionCredentialValidationFailureReason.None, result.FailureReason);
        Assert.Equal(issued.Credential.ScopedIngestionCredentialId, result.Credential?.ScopedIngestionCredentialId);
        Assert.Equal(developer.ProductUserId, result.ProductUserId);
    }

    [Theory]
    [InlineData(ScopedIngestionCredentialStatus.Disabled, ScopedIngestionCredentialValidationFailureReason.Disabled)]
    [InlineData(ScopedIngestionCredentialStatus.Revoked, ScopedIngestionCredentialValidationFailureReason.Revoked)]
    [InlineData(ScopedIngestionCredentialStatus.PendingRotation, ScopedIngestionCredentialValidationFailureReason.Inactive)]
    public async Task ValidateFailsClosedForInactiveLifecycleStates(
        ScopedIngestionCredentialStatus status,
        ScopedIngestionCredentialValidationFailureReason expectedFailureReason)
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var lifecycle = new ScopedIngestionCredentialLifecycleService(store);
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var admin = await CreateProductUserAsync(store, seed, "admin-subject");
        var developer = await CreateProductUserAsync(store, seed, "developer-subject");
        var issued = await IssueCredentialAsync(store, lifecycle, seed, admin, developer);
        await SetStatusAsync(lifecycle, store, seed, admin, issued.Credential.ScopedIngestionCredentialId, status);

        var result = await lifecycle.ValidateAsync(new ValidateScopedIngestionCredentialRequest(
            CustomerOrganizationId: seed.Organization.CustomerOrganizationId,
            Secret: issued.Secret,
            DeclaredHarness: "codex-cli",
            HarnessSetupProfileId: "profile-codex-cli-eastus2",
            CorrelationId: $"credential-validate-{status}"));

        Assert.False(result.IsValid);
        Assert.Equal(expectedFailureReason, result.FailureReason);
        Assert.Null(result.ProductUserId);
        Assert.Contains(
            await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId),
            auditEvent => auditEvent.Decision == "denied" &&
                auditEvent.EvidenceMetadata["operation"] == "scoped_ingestion_credential_failed_access");
    }

    [Fact]
    public async Task ValidateFailsClosedForExpiredCredential()
    {
        var clock = new MutableTenantMetadataClock(Now);
        var store = new InMemoryTenantMetadataStore(clock);
        var lifecycle = new ScopedIngestionCredentialLifecycleService(store, clock);
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var admin = await CreateProductUserAsync(store, seed, "admin-subject");
        var developer = await CreateProductUserAsync(store, seed, "developer-subject");
        var issued = await IssueCredentialAsync(store, lifecycle, seed, admin, developer, expiresAtUtc: Now.AddMinutes(5));
        clock.UtcNow = Now.AddMinutes(6);

        var result = await lifecycle.ValidateAsync(new ValidateScopedIngestionCredentialRequest(
            CustomerOrganizationId: seed.Organization.CustomerOrganizationId,
            Secret: issued.Secret,
            DeclaredHarness: "codex-cli",
            HarnessSetupProfileId: "profile-codex-cli-eastus2",
            CorrelationId: "credential-validate-expired"));

        Assert.False(result.IsValid);
        Assert.Equal(ScopedIngestionCredentialValidationFailureReason.Expired, result.FailureReason);
    }

    [Fact]
    public async Task ValidateFailsClosedForWrongTenantWrongHarnessMalformedAndMissingCredential()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var lifecycle = new ScopedIngestionCredentialLifecycleService(store);
        var contoso = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var fabrikam = await CreateTenantAsync(store, "fabrikam", "fabrikam-tenant");
        var admin = await CreateProductUserAsync(store, contoso, "admin-subject");
        var developer = await CreateProductUserAsync(store, contoso, "developer-subject");
        var issued = await IssueCredentialAsync(store, lifecycle, contoso, admin, developer);

        var wrongTenant = await lifecycle.ValidateAsync(new ValidateScopedIngestionCredentialRequest(
            CustomerOrganizationId: fabrikam.Organization.CustomerOrganizationId,
            Secret: issued.Secret,
            DeclaredHarness: "codex-cli",
            HarnessSetupProfileId: "profile-codex-cli-eastus2",
            CorrelationId: "credential-validate-wrong-tenant"));
        var wrongHarness = await lifecycle.ValidateAsync(new ValidateScopedIngestionCredentialRequest(
            CustomerOrganizationId: contoso.Organization.CustomerOrganizationId,
            Secret: issued.Secret,
            DeclaredHarness: "claude-code",
            HarnessSetupProfileId: "profile-codex-cli-eastus2",
            CorrelationId: "credential-validate-wrong-harness"));
        var malformed = await lifecycle.ValidateAsync(new ValidateScopedIngestionCredentialRequest(
            CustomerOrganizationId: contoso.Organization.CustomerOrganizationId,
            Secret: "not-a-token",
            DeclaredHarness: "codex-cli",
            HarnessSetupProfileId: "profile-codex-cli-eastus2",
            CorrelationId: "credential-validate-malformed"));
        var missing = await lifecycle.ValidateAsync(new ValidateScopedIngestionCredentialRequest(
            CustomerOrganizationId: contoso.Organization.CustomerOrganizationId,
            Secret: "aito_live_unknownCredentialValue000000000000",
            DeclaredHarness: "codex-cli",
            HarnessSetupProfileId: "profile-codex-cli-eastus2",
            CorrelationId: "credential-validate-missing"));

        Assert.False(wrongTenant.IsValid);
        Assert.False(wrongHarness.IsValid);
        Assert.False(malformed.IsValid);
        Assert.False(missing.IsValid);
        Assert.Equal(ScopedIngestionCredentialValidationFailureReason.Missing, wrongTenant.FailureReason);
        Assert.Equal(ScopedIngestionCredentialValidationFailureReason.WrongHarness, wrongHarness.FailureReason);
        Assert.Equal(ScopedIngestionCredentialValidationFailureReason.Malformed, malformed.FailureReason);
        Assert.Equal(ScopedIngestionCredentialValidationFailureReason.Missing, missing.FailureReason);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ValidateFailsClosedForMalformedHarnessContext(string malformedValue)
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var lifecycle = new ScopedIngestionCredentialLifecycleService(store);
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var admin = await CreateProductUserAsync(store, seed, "admin-subject");
        var developer = await CreateProductUserAsync(store, seed, "developer-subject");
        var issued = await IssueCredentialAsync(store, lifecycle, seed, admin, developer);

        var missingHarness = await lifecycle.ValidateAsync(new ValidateScopedIngestionCredentialRequest(
            CustomerOrganizationId: seed.Organization.CustomerOrganizationId,
            Secret: issued.Secret,
            DeclaredHarness: malformedValue,
            HarnessSetupProfileId: "profile-codex-cli-eastus2",
            CorrelationId: "credential-validate-missing-harness"));
        var missingProfile = await lifecycle.ValidateAsync(new ValidateScopedIngestionCredentialRequest(
            CustomerOrganizationId: seed.Organization.CustomerOrganizationId,
            Secret: issued.Secret,
            DeclaredHarness: "codex-cli",
            HarnessSetupProfileId: malformedValue,
            CorrelationId: "credential-validate-missing-profile"));

        Assert.False(missingHarness.IsValid);
        Assert.False(missingProfile.IsValid);
        Assert.Equal(ScopedIngestionCredentialValidationFailureReason.Malformed, missingHarness.FailureReason);
        Assert.Equal(ScopedIngestionCredentialValidationFailureReason.Malformed, missingProfile.FailureReason);
    }

    [Fact]
    public async Task RotateCreatesAuditableLifecycleStateWithoutInvalidatingOtherCredentials()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var lifecycle = new ScopedIngestionCredentialLifecycleService(store);
        var seed = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var admin = await CreateProductUserAsync(store, seed, "admin-subject");
        var developer = await CreateProductUserAsync(store, seed, "developer-subject");
        var first = await IssueCredentialAsync(store, lifecycle, seed, admin, developer);
        var second = await IssueCredentialAsync(
            store,
            lifecycle,
            seed,
            admin,
            developer,
            profileId: "profile-codex-cli-eastus2-secondary",
            auditSuffix: "secondary");

        var rotated = await lifecycle.RotateAsync(
            seed.Organization.CustomerOrganizationId,
            first.Credential.ScopedIngestionCredentialId,
            new RotateScopedIngestionCredentialCommand(
                ExpiresAtUtc: Now.AddDays(60),
                ChangedByProductUserId: admin.ProductUserId,
                ActorEffectiveRole: ProductRole.PlatformAdmin,
                CorrelationId: "credential-rotate-001",
                AuditEventId: "audit-credential-rotate-001"));

        var oldSecretValidation = await lifecycle.ValidateAsync(new ValidateScopedIngestionCredentialRequest(
            CustomerOrganizationId: seed.Organization.CustomerOrganizationId,
            Secret: first.Secret,
            DeclaredHarness: "codex-cli",
            HarnessSetupProfileId: "profile-codex-cli-eastus2",
            CorrelationId: "credential-validate-old-secret"));
        var rotatedValidation = await lifecycle.ValidateAsync(new ValidateScopedIngestionCredentialRequest(
            CustomerOrganizationId: seed.Organization.CustomerOrganizationId,
            Secret: rotated.Secret,
            DeclaredHarness: "codex-cli",
            HarnessSetupProfileId: "profile-codex-cli-eastus2",
            CorrelationId: "credential-validate-rotated-secret"));
        var unrelatedValidation = await lifecycle.ValidateAsync(new ValidateScopedIngestionCredentialRequest(
            CustomerOrganizationId: seed.Organization.CustomerOrganizationId,
            Secret: second.Secret,
            DeclaredHarness: "codex-cli",
            HarnessSetupProfileId: "profile-codex-cli-eastus2-secondary",
            CorrelationId: "credential-validate-unrelated-secret"));

        Assert.False(oldSecretValidation.IsValid);
        Assert.Equal(ScopedIngestionCredentialValidationFailureReason.Missing, oldSecretValidation.FailureReason);
        Assert.True(rotatedValidation.IsValid);
        Assert.True(unrelatedValidation.IsValid);
        Assert.Equal(first.Credential.ScopedIngestionCredentialId, rotated.Credential.ScopedIngestionCredentialId);
        Assert.NotEqual(first.Secret, rotated.Secret);
        Assert.Contains(
            await store.ListGovernanceAuditEventsAsync(seed.Organization.CustomerOrganizationId),
            auditEvent => auditEvent.EvidenceMetadata["operation"] == "scoped_ingestion_credential_rotate");
    }

    [Fact]
    public async Task DisableAndRevokeRejectWrongTenantMutationWithoutChangingCredential()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var lifecycle = new ScopedIngestionCredentialLifecycleService(store);
        var contoso = await CreateTenantAsync(store, "contoso", "contoso-tenant");
        var fabrikam = await CreateTenantAsync(store, "fabrikam", "fabrikam-tenant");
        var admin = await CreateProductUserAsync(store, contoso, "admin-subject");
        var developer = await CreateProductUserAsync(store, contoso, "developer-subject");
        var issued = await IssueCredentialAsync(store, lifecycle, contoso, admin, developer);
        var lifecycleRequest = new ScopedIngestionCredentialLifecycleRequest(
            ChangedByProductUserId: admin.ProductUserId,
            ActorEffectiveRole: ProductRole.PlatformAdmin,
            CorrelationId: "credential-cross-tenant-mutation",
            AuditEventId: "audit-credential-cross-tenant-mutation");

        await Assert.ThrowsAsync<InvalidOperationException>(() => lifecycle.DisableAsync(
            fabrikam.Organization.CustomerOrganizationId,
            issued.Credential.ScopedIngestionCredentialId,
            lifecycleRequest));
        await Assert.ThrowsAsync<InvalidOperationException>(() => lifecycle.RevokeAsync(
            fabrikam.Organization.CustomerOrganizationId,
            issued.Credential.ScopedIngestionCredentialId,
            lifecycleRequest));

        var stored = await store.FindScopedIngestionCredentialAsync(
            contoso.Organization.CustomerOrganizationId,
            issued.Credential.ScopedIngestionCredentialId);

        Assert.NotNull(stored);
        Assert.Equal(ScopedIngestionCredentialStatus.Active, stored.Status);
        Assert.Empty(await store.ListGovernanceAuditEventsAsync(fabrikam.Organization.CustomerOrganizationId));
    }

    private static async Task<IssuedScopedIngestionCredential> IssueCredentialAsync(
        InMemoryTenantMetadataStore store,
        ScopedIngestionCredentialLifecycleService lifecycle,
        TenantSeed seed,
        ProductUser admin,
        ProductUser developer,
        string profileId = "profile-codex-cli-eastus2",
        string auditSuffix = "primary",
        DateTimeOffset? expiresAtUtc = null)
    {
        return await lifecycle.CreateAsync(
            seed.Organization.CustomerOrganizationId,
            new IssueScopedIngestionCredentialRequest(
                HarnessSetupProfileId: profileId,
                ProductUserId: developer.ProductUserId,
                AllowedHarness: CodingAgentHarness.CodexCli,
                AllowedScopes: [new ProductScope(ProductScopeKind.Repository, "repo-001")],
                ExpiresAtUtc: expiresAtUtc ?? Now.AddDays(30),
                CreatedByProductUserId: admin.ProductUserId,
                ActorEffectiveRole: ProductRole.PlatformAdmin,
                CorrelationId: $"credential-create-{auditSuffix}",
                AuditEventId: $"audit-credential-create-{auditSuffix}"));
    }

    private static Task SetStatusAsync(
        ScopedIngestionCredentialLifecycleService lifecycle,
        InMemoryTenantMetadataStore store,
        TenantSeed seed,
        ProductUser admin,
        ScopedIngestionCredentialId credentialId,
        ScopedIngestionCredentialStatus status)
    {
        var request = new ScopedIngestionCredentialLifecycleRequest(
            ChangedByProductUserId: admin.ProductUserId,
            ActorEffectiveRole: ProductRole.PlatformAdmin,
            CorrelationId: $"credential-{status}-001",
            AuditEventId: $"audit-credential-{status}-001");

        return status switch
        {
            ScopedIngestionCredentialStatus.Disabled => lifecycle.DisableAsync(
                seed.Organization.CustomerOrganizationId,
                credentialId,
                request),
            ScopedIngestionCredentialStatus.Revoked => lifecycle.RevokeAsync(
                seed.Organization.CustomerOrganizationId,
                credentialId,
                request),
            ScopedIngestionCredentialStatus.PendingRotation => store.MarkScopedIngestionCredentialPendingRotationAsync(
                seed.Organization.CustomerOrganizationId,
                credentialId,
                request),
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
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

    private sealed record TenantSeed(CustomerOrganization Organization, IdentityTenant IdentityTenant);

    private sealed class MutableTenantMetadataClock(DateTimeOffset utcNow) : ITenantMetadataClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
