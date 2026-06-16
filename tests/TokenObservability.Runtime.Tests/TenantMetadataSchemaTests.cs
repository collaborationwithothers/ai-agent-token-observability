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
        Assert.Contains("CREATE TABLE IF NOT EXISTS governance_audit_event", migration);
        Assert.Contains("CREATE TABLE IF NOT EXISTS ingestion_rejection", migration);
        Assert.Contains("CREATE TABLE IF NOT EXISTS telemetry_envelope", migration);
        Assert.Contains("CREATE TABLE IF NOT EXISTS agent_session", migration);
        Assert.Contains("customer_organization_id uuid", migration);
        Assert.Contains("identity_tenant_id uuid", migration);
        Assert.Contains("product_user_id uuid", migration);
        Assert.Contains("ingestion_rejection_id uuid PRIMARY KEY", migration);
        Assert.Contains("telemetry_envelope_id text PRIMARY KEY", migration);
        Assert.Contains("agent_session_id text PRIMARY KEY", migration);
        Assert.Contains("harness_setup_profile_id text NULL", migration);
        Assert.Contains("harness_setup_profile_id text NOT NULL", migration);
        Assert.Contains("scoped_ingestion_credential_id uuid NULL", migration);
        Assert.Contains("scoped_ingestion_credential_id uuid NOT NULL", migration);
        Assert.Contains("schema_version text NOT NULL", migration);
        Assert.Contains("source_event_name text NOT NULL", migration);
        Assert.Contains("conversation_id_hash text NULL", migration);
        Assert.Contains("dedupe_key_hash text NOT NULL", migration);
        Assert.Contains("source_evidence_kind text NOT NULL", migration);
        Assert.Contains("metric_state text NOT NULL", migration);
        Assert.Contains("provider_session_id_hash text NULL", migration);
        Assert.Contains("reason_code text NOT NULL", migration);
        Assert.Contains("http_status integer NOT NULL", migration);
        Assert.Contains("correlation_id text NOT NULL", migration);
        Assert.Contains("CONSTRAINT uq_scoped_ingestion_credential_customer_credential UNIQUE (customer_organization_id, scoped_ingestion_credential_id)", migration);
        Assert.Contains("CONSTRAINT fk_ingestion_rejection_scoped_credential FOREIGN KEY (customer_organization_id, scoped_ingestion_credential_id) REFERENCES scoped_ingestion_credential (customer_organization_id, scoped_ingestion_credential_id)", migration);
        Assert.Contains("CONSTRAINT fk_ingestion_rejection_audit_event FOREIGN KEY (customer_organization_id, audit_event_id) REFERENCES governance_audit_event (customer_organization_id, audit_event_id)", migration);
        Assert.Contains("CONSTRAINT fk_telemetry_envelope_scoped_credential FOREIGN KEY (customer_organization_id, scoped_ingestion_credential_id) REFERENCES scoped_ingestion_credential (customer_organization_id, scoped_ingestion_credential_id)", migration);
        Assert.Contains("CONSTRAINT fk_telemetry_envelope_product_user FOREIGN KEY (customer_organization_id, product_user_id) REFERENCES product_user (customer_organization_id, product_user_id)", migration);
        Assert.Contains("CONSTRAINT fk_agent_session_product_user FOREIGN KEY (customer_organization_id, product_user_id) REFERENCES product_user (customer_organization_id, product_user_id)", migration);
        Assert.Contains("CONSTRAINT ck_ingestion_rejection_link_tenant_shape CHECK", migration);
        Assert.Contains("CONSTRAINT ck_ingestion_rejection_harness_setup_profile_id CHECK", migration);
        Assert.Contains("CONSTRAINT ck_ingestion_rejection_declared_harness CHECK (declared_harness IS NULL OR declared_harness IN ('codex-cli'))", migration);
        Assert.Contains("CONSTRAINT uq_telemetry_envelope_customer_dedupe UNIQUE (customer_organization_id, dedupe_key_hash)", migration);
        Assert.Contains("CONSTRAINT ck_telemetry_envelope_harness CHECK (harness IN ('codex-cli'))", migration);
        Assert.Contains("CONSTRAINT ck_telemetry_envelope_signal_type CHECK (signal_type IN ('log', 'trace', 'metric'))", migration);
        Assert.Contains("CONSTRAINT ck_telemetry_envelope_hashes CHECK", migration);
        Assert.Contains("CONSTRAINT ck_agent_session_status CHECK", migration);
        Assert.Contains("evidence_metadata_json jsonb NOT NULL", migration);
        Assert.Contains("routing_decision_json jsonb NOT NULL", migration);
        Assert.Contains("ingestion_version_metadata_json jsonb NOT NULL", migration);
        Assert.Contains("model_names_json jsonb NOT NULL", migration);
        Assert.Contains("source_telemetry_envelope_ids_json jsonb NOT NULL", migration);
        Assert.Contains("ck_governance_audit_event_evidence_metadata_json", migration);
        Assert.Contains("ck_ingestion_rejection_evidence_metadata_json", migration);
        Assert.Contains("ck_telemetry_envelope_routing_decision_json", migration);
        Assert.Contains("ck_agent_session_source_envelopes_json", migration);
        Assert.Contains("ix_ingestion_rejection_customer_received", migration);
        Assert.Contains("ix_ingestion_rejection_reason_received", migration);
        Assert.Contains("ix_telemetry_envelope_customer_received", migration);
        Assert.Contains("ix_agent_session_customer_updated", migration);
        Assert.Contains("created_at_utc timestamptz NOT NULL", migration);
        Assert.Contains("updated_at_utc timestamptz NOT NULL", migration);
        Assert.Contains("UNIQUE (customer_organization_id, identity_tenant_id, external_subject_id)", migration);
        Assert.DoesNotContain("raw_prompt", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prompt_text", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("code_content", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("command_output", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tool_result", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("local_os", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("git_config", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("local_file_system", migration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PostgreSqlTenantMetadataMigrationPersistsTokenMetricQualitySemantics()
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

        Assert.Contains("CREATE TABLE IF NOT EXISTS telemetry_envelope", migration);
        Assert.Contains("CREATE TABLE IF NOT EXISTS agent_session", migration);
        Assert.Contains("CREATE TABLE IF NOT EXISTS token_observation", migration);
        Assert.Contains("metric_status text NOT NULL", migration);
        Assert.Contains("metric_confidence text NOT NULL", migration);
        Assert.Contains("value bigint NULL", migration);
        Assert.Contains("'observed'", migration);
        Assert.Contains("'estimated'", migration);
        Assert.Contains("'unavailable'", migration);
        Assert.Contains("'not_applicable'", migration);
        Assert.Contains("'mixed'", migration);
        Assert.Contains("ck_token_observation_null_semantics", migration);
        Assert.Contains("ck_token_observation_zero_semantics", migration);
        Assert.Contains("ix_token_observation_session_metric", migration);
    }

    [Fact]
    public void PostgreSqlTenantMetadataMigrationPersistsAggregateMetricExportShape()
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

        Assert.Contains("CREATE TABLE IF NOT EXISTS aggregate_metric_point", migration);
        Assert.Contains("CREATE TABLE IF NOT EXISTS aggregate_metric_export_failure", migration);
        Assert.Contains("aggregate_metric_point_id uuid PRIMARY KEY", migration);
        Assert.Contains("aggregate_metric_export_failure_id uuid PRIMARY KEY", migration);
        Assert.Contains("customer_organization_id uuid NOT NULL", migration);
        Assert.Contains("agent_session_id text NOT NULL", migration);
        Assert.Contains("metric_name text NOT NULL", migration);
        Assert.Contains("metric_value double precision NOT NULL", migration);
        Assert.Contains("unit text NOT NULL", migration);
        Assert.Contains("labels_json jsonb NOT NULL", migration);
        Assert.Contains("exported_at_utc timestamptz NOT NULL", migration);
        Assert.Contains("failure_reason text NOT NULL", migration);
        Assert.Contains("correlation_id text NOT NULL", migration);
        Assert.Contains("ck_aggregate_metric_point_name CHECK", migration);
        Assert.Contains("ck_aggregate_metric_point_labels_json", migration);
        Assert.Contains("ck_aggregate_metric_export_failure_reason CHECK", migration);
        Assert.Contains("tokenobs_token_metric_states_total", migration);
        Assert.Contains("'observations'", migration);
        Assert.Contains("ix_aggregate_metric_point_customer_exported", migration);
        Assert.Contains("ix_aggregate_metric_export_failure_customer_created", migration);

        var aggregateSection = migration[migration.IndexOf(
            "CREATE TABLE IF NOT EXISTS aggregate_metric_point",
            StringComparison.Ordinal)..];
        Assert.DoesNotContain("developer", aggregateSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential_id text", aggregateSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("trace_id text", aggregateSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prompt text", aggregateSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("command_output text", aggregateSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tool_result text", aggregateSection, StringComparison.OrdinalIgnoreCase);
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
