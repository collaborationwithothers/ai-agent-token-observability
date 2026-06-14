using TokenObservability.Domain.Tenancy;
using TokenObservability.Infrastructure.Persistence;
using System.Text.RegularExpressions;

namespace TokenObservability.Runtime.Tests;

public sealed class TenantMetadataSchemaTests
{
    [Fact]
    public async Task TenantMetadataStoreCreatesAndLooksUpTenantGraphWithoutLocalIdentity()
    {
        var now = new DateTimeOffset(2026, 6, 14, 12, 30, 0, TimeSpan.Zero);
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(now));

        var organization = await store.CreateCustomerOrganizationAsync(new CreateCustomerOrganizationRequest(
            Slug: "internal",
            DisplayName: "Internal Engineering",
            DataResidencyRegion: "eastus2",
            IsolationTier: CustomerOrganizationIsolationTier.Shared));

        var identityTenant = await store.CreateIdentityTenantAsync(
            organization.CustomerOrganizationId,
            new CreateIdentityTenantRequest(
                Provider: IdentityTenantProvider.MicrosoftEntra,
                Issuer: "https://login.microsoftonline.com/contoso-tenant-id/v2.0",
                ExternalTenantId: "contoso-tenant-id",
                AllowedAudiences: ["api://token-observability"],
                JwksUri: new Uri("https://login.microsoftonline.com/contoso-tenant-id/discovery/v2.0/keys"),
                DisplayName: "Contoso Entra ID"));

        var user = await store.CreateProductUserAsync(
            organization.CustomerOrganizationId,
            identityTenant.IdentityTenantId,
            new CreateProductUserRequest(
                ExternalSubjectId: "aad-subject-123",
                DisplayLabel: "Hari Praghash",
                Email: "hari@example.com"));

        var found = await store.FindProductUserByExternalSubjectAsync(
            organization.CustomerOrganizationId,
            identityTenant.IdentityTenantId,
            "aad-subject-123");

        Assert.NotEqual(CustomerOrganizationId.Empty, organization.CustomerOrganizationId);
        Assert.NotEqual(IdentityTenantId.Empty, identityTenant.IdentityTenantId);
        Assert.NotEqual(ProductUserId.Empty, user.ProductUserId);
        Assert.Equal("internal", organization.Slug);
        Assert.Equal("Internal Engineering", organization.DisplayName);
        Assert.Equal(now, organization.CreatedAtUtc);
        Assert.Equal(now, organization.UpdatedAtUtc);
        Assert.Equal(organization.CustomerOrganizationId, identityTenant.CustomerOrganizationId);
        Assert.Equal(organization.CustomerOrganizationId, user.CustomerOrganizationId);
        Assert.Equal(identityTenant.IdentityTenantId, user.IdentityTenantId);
        Assert.Equal("aad-subject-123", user.ExternalSubjectId);
        Assert.Equal("Hari Praghash", user.DisplayLabel);
        Assert.Equal(now, user.CreatedAtUtc);
        Assert.Equal(now, user.UpdatedAtUtc);
        Assert.NotNull(found);
        Assert.Equal(user.ProductUserId, found.ProductUserId);
    }

    [Fact]
    public async Task TenantMetadataLookupDoesNotCrossCustomerOrganizationBoundary()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(DateTimeOffset.UnixEpoch));

        var firstOrganization = await store.CreateCustomerOrganizationAsync(new CreateCustomerOrganizationRequest(
            Slug: "contoso",
            DisplayName: "Contoso",
            DataResidencyRegion: "eastus2",
            IsolationTier: CustomerOrganizationIsolationTier.Shared));

        var secondOrganization = await store.CreateCustomerOrganizationAsync(new CreateCustomerOrganizationRequest(
            Slug: "fabrikam",
            DisplayName: "Fabrikam",
            DataResidencyRegion: "westeurope",
            IsolationTier: CustomerOrganizationIsolationTier.Shared));

        var identityTenant = await store.CreateIdentityTenantAsync(
            firstOrganization.CustomerOrganizationId,
            new CreateIdentityTenantRequest(
                Provider: IdentityTenantProvider.MicrosoftEntra,
                Issuer: "https://login.microsoftonline.com/contoso/v2.0",
                ExternalTenantId: "contoso",
                AllowedAudiences: ["api://token-observability"],
                JwksUri: null,
                DisplayName: "Contoso Entra ID"));

        await store.CreateProductUserAsync(
            firstOrganization.CustomerOrganizationId,
            identityTenant.IdentityTenantId,
            new CreateProductUserRequest(
                ExternalSubjectId: "shared-subject-id",
                DisplayLabel: "Contoso User",
                Email: null));

        var crossTenantLookup = await store.FindProductUserByExternalSubjectAsync(
            secondOrganization.CustomerOrganizationId,
            identityTenant.IdentityTenantId,
            "shared-subject-id");

        Assert.Null(crossTenantLookup);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateProductUserAsync(
            secondOrganization.CustomerOrganizationId,
            identityTenant.IdentityTenantId,
            new CreateProductUserRequest(
                ExternalSubjectId: "shared-subject-id",
                DisplayLabel: "Wrong Tenant User",
                Email: null)));
    }

    [Fact]
    public void PostgreSqlTenantMetadataMigrationDefinesTenantAwareBaseline()
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

        Assert.True(File.Exists(migrationPath), "Tenant metadata migration baseline must exist.");

        var migration = File.ReadAllText(migrationPath);

        Assert.Contains("CREATE TABLE IF NOT EXISTS customer_organization", migration);
        Assert.Contains("CREATE TABLE IF NOT EXISTS identity_tenant", migration);
        Assert.Contains("CREATE TABLE IF NOT EXISTS product_user", migration);
        Assert.Contains("customer_organization_id uuid", migration);
        Assert.Contains("identity_tenant_id uuid", migration);
        Assert.Contains("product_user_id uuid", migration);
        Assert.Contains("created_at_utc timestamptz NOT NULL", migration);
        Assert.Contains("updated_at_utc timestamptz NOT NULL", migration);
        Assert.Contains("UNIQUE (customer_organization_id, identity_tenant_id, external_subject_id)", migration);
        Assert.DoesNotContain("local_os", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("git_config", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("local_file_system", migration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PostgreSqlCustomerOrganizationSlugConstraintMatchesTerraformSlugValidation()
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
        var match = Regex.Match(migration, @"slug ~ '([^']+)'");

        Assert.True(match.Success, "Customer organization slug regex must be defined in the migration.");

        var slugRegex = new Regex(match.Groups[1].Value, RegexOptions.CultureInvariant);

        Assert.Matches(slugRegex, "a");
        Assert.Matches(slugRegex, "7");
        Assert.Matches(slugRegex, "internal");
        Assert.Matches(slugRegex, "team-7");
        Assert.DoesNotMatch(slugRegex, "-team");
        Assert.DoesNotMatch(slugRegex, "team-");
        Assert.DoesNotMatch(slugRegex, "Team");
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
}

public sealed class StaticTenantMetadataClock(DateTimeOffset utcNow) : ITenantMetadataClock
{
    public DateTimeOffset UtcNow => utcNow;
}
