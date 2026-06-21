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
        Assert.Contains("CREATE TABLE IF NOT EXISTS content_reference", migration);
        Assert.Contains("CREATE TABLE IF NOT EXISTS redaction_review", migration);
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
        Assert.Contains("CONSTRAINT fk_content_reference_agent_session FOREIGN KEY (customer_organization_id, agent_session_id) REFERENCES agent_session (customer_organization_id, agent_session_id)", migration);
        Assert.Contains("CONSTRAINT fk_content_reference_telemetry_envelope FOREIGN KEY (customer_organization_id, telemetry_envelope_id) REFERENCES telemetry_envelope (customer_organization_id, telemetry_envelope_id)", migration);
        Assert.Contains("CONSTRAINT fk_content_reference_audit_event FOREIGN KEY (customer_organization_id, audit_event_id) REFERENCES governance_audit_event (customer_organization_id, audit_event_id)", migration);
        Assert.Contains("CONSTRAINT fk_redaction_review_content_reference FOREIGN KEY (customer_organization_id, content_reference_id) REFERENCES content_reference (customer_organization_id, content_reference_id)", migration);
        Assert.Contains("CONSTRAINT fk_redaction_review_reviewer_product_user FOREIGN KEY (customer_organization_id, reviewer_product_user_id) REFERENCES product_user (customer_organization_id, product_user_id)", migration);
        Assert.Contains("CONSTRAINT fk_redaction_review_audit_event FOREIGN KEY (customer_organization_id, audit_event_id) REFERENCES governance_audit_event (customer_organization_id, audit_event_id)", migration);
        Assert.Contains("CONSTRAINT ck_ingestion_rejection_link_tenant_shape CHECK", migration);
        Assert.Contains("CONSTRAINT ck_ingestion_rejection_harness_setup_profile_id CHECK", migration);
        Assert.Contains("CONSTRAINT ck_ingestion_rejection_declared_harness CHECK (declared_harness IS NULL OR declared_harness IN ('codex-cli'))", migration);
        Assert.Contains("CONSTRAINT uq_telemetry_envelope_customer_dedupe UNIQUE (customer_organization_id, dedupe_key_hash)", migration);
        Assert.Contains("CONSTRAINT ck_telemetry_envelope_harness CHECK (harness IN ('codex-cli'))", migration);
        Assert.Contains("CONSTRAINT ck_telemetry_envelope_signal_type CHECK (signal_type IN ('log', 'trace', 'metric'))", migration);
        Assert.Contains("CONSTRAINT ck_telemetry_envelope_hashes CHECK", migration);
        Assert.Contains("CONSTRAINT ck_agent_session_status CHECK", migration);
        Assert.Contains("CONSTRAINT ck_content_reference_blob_pointer_state CHECK", migration);
        Assert.Contains("CONSTRAINT ck_content_reference_capture_state CHECK (capture_state IN ('not_allowed', 'metadata_only', 'captured', 'redaction_failed', 'review_required', 'discarded', 'approved_excerpt'))", migration);
        Assert.Contains("CONSTRAINT ck_redaction_review_decision CHECK (decision IN ('retry', 'discard', 'approve_excerpt', 'reject_excerpt', 'mark_recommendation_ineligible'))", migration);
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
        Assert.Contains("ix_content_reference_customer_session_state", migration);
        Assert.Contains("ix_content_reference_customer_review_state", migration);
        Assert.Contains("ix_content_reference_customer_expires", migration);
        Assert.Contains("ix_redaction_review_customer_content_reference", migration);
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

        var contentReferenceSection = migration.Substring(
            migration.IndexOf("CREATE TABLE IF NOT EXISTS content_reference", StringComparison.Ordinal),
            migration.IndexOf("CREATE TABLE IF NOT EXISTS token_observation", StringComparison.Ordinal) -
            migration.IndexOf("CREATE TABLE IF NOT EXISTS content_reference", StringComparison.Ordinal));
        Assert.DoesNotContain("approved_excerpt text", contentReferenceSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw_", contentReferenceSection, StringComparison.OrdinalIgnoreCase);
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
    public void PostgreSqlTenantMetadataMigrationPersistsPricingBasisAndCostEstimateSemantics()
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

        Assert.Contains("CREATE TABLE IF NOT EXISTS pricing_basis", migration);
        Assert.Contains("CREATE TABLE IF NOT EXISTS cost_estimate", migration);
        Assert.Contains("CREATE TABLE IF NOT EXISTS product_api_idempotency", migration);
        Assert.Contains("pricing_basis_id text PRIMARY KEY", migration);
        Assert.Contains("cost_estimate_id text PRIMARY KEY", migration);
        Assert.Contains("request_hash text NOT NULL", migration);
        Assert.Contains("operation_id text NULL", migration);
        Assert.Contains("response_json jsonb NULL", migration);
        Assert.Contains("expires_at_utc timestamptz NOT NULL", migration);
        Assert.Contains("completed_at_utc timestamptz NULL", migration);
        Assert.Contains("provider_name text NOT NULL", migration);
        Assert.Contains("model_name text NOT NULL", migration);
        Assert.Contains("token_type text NOT NULL", migration);
        Assert.Contains("billing_route text NOT NULL", migration);
        Assert.Contains("price_per_million_tokens numeric(18, 8) NOT NULL", migration);
        Assert.Contains("source_metadata_json jsonb NOT NULL", migration);
        Assert.Contains("estimated_cost numeric(18, 12) NULL", migration);
        Assert.Contains("pricing_basis_id text NULL", migration);
        Assert.Contains("ck_pricing_basis_token_type CHECK (token_type IN ('input', 'output', 'cached_input', 'reasoning_output'))", migration);
        Assert.Contains("ck_pricing_basis_source_kind CHECK (source_kind IN ('automated_seed', 'admin_override', 'provider_docs', 'enterprise_contract'))", migration);
        Assert.Contains("ck_pricing_basis_review_state CHECK (review_state IN ('candidate', 'approved', 'rejected', 'superseded'))", migration);
        Assert.Contains("ck_cost_estimate_status CHECK (cost_status IN ('estimated', 'unavailable', 'not_applicable', 'mixed'))", migration);
        Assert.Contains("ck_cost_estimate_unavailable_null_semantics", migration);
        Assert.Contains("fk_pricing_basis_audit_event", migration);
        Assert.Contains("fk_cost_estimate_pricing_basis", migration);
        Assert.Contains("fk_product_api_idempotency_product_user", migration);
        Assert.Contains("ck_product_api_idempotency_request_hash CHECK (request_hash ~ '^[A-F0-9]{64}$')", migration);
        Assert.Contains("ck_product_api_idempotency_expiry CHECK (expires_at_utc > created_at_utc)", migration);
        Assert.Contains("ck_product_api_idempotency_completion CHECK", migration);
        Assert.Contains("ix_pricing_basis_customer_review_model", migration);
        Assert.Contains("ix_cost_estimate_customer_mix", migration);
        Assert.Contains("ix_product_api_idempotency_expiry", migration);
    }

    [Fact]
    public void PostgreSqlIdempotencyStoreReclaimsExpiredKeysBeforeReservation()
    {
        var root = FindRepositoryRoot();
        var storePath = Path.Combine(
            root,
            "src",
            "TokenObservability.Infrastructure",
            "Persistence",
            "PostgreSqlProductApiIdempotencyStore.cs");

        var storeSource = File.ReadAllText(storePath);

        Assert.Contains("DELETE FROM product_api_idempotency", storeSource);
        Assert.Contains("expires_at_utc <= @now", storeSource);
        Assert.Contains("BeginTransactionAsync", storeSource);
        Assert.Contains("Product API idempotency reservation was not acquired.", storeSource);
    }

    [Fact]
    public void PostgreSqlTenantMetadataMigrationPersistsTokenHotspotEvidenceBoundaries()
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

        Assert.Contains("CREATE TABLE IF NOT EXISTS token_hotspot", migration);
        Assert.Contains("token_hotspot_id uuid PRIMARY KEY", migration);
        Assert.Contains("customer_organization_id uuid NOT NULL", migration);
        Assert.Contains("agent_session_id text NOT NULL", migration);
        Assert.Contains("harness text NOT NULL", migration);
        Assert.Contains("model_name text NULL", migration);
        Assert.Contains("hotspot_type text NOT NULL", migration);
        Assert.Contains("finding_state text NOT NULL", migration);
        Assert.Contains("attribution_type text NOT NULL", migration);
        Assert.Contains("confidence text NOT NULL", migration);
        Assert.Contains("metric_status text NOT NULL", migration);
        Assert.Contains("metric_confidence text NOT NULL", migration);
        Assert.Contains("prompt_cache_evidence_state text NOT NULL", migration);
        Assert.Contains("evidence_summary text NOT NULL", migration);
        Assert.Contains("evidence_refs_json jsonb NOT NULL", migration);
        Assert.Contains("detection_key text NULL", migration);
        Assert.Contains("CONSTRAINT fk_token_hotspot_agent_session FOREIGN KEY (customer_organization_id, agent_session_id) REFERENCES agent_session (customer_organization_id, agent_session_id)", migration);
        Assert.Contains("CONSTRAINT ck_token_hotspot_type CHECK", migration);
        Assert.Contains("'prompt_cache_breakage'", migration);
        Assert.Contains("'large_context'", migration);
        Assert.Contains("'tool_loop'", migration);
        Assert.Contains("'model_retry'", migration);
        Assert.Contains("'repo_context_bloat'", migration);
        Assert.Contains("'generated_artifact_bloat'", migration);
        Assert.Contains("'expensive_model_choice'", migration);
        Assert.Contains("'error_rework'", migration);
        Assert.Contains("'unknown'", migration);
        Assert.Contains("CONSTRAINT ck_token_hotspot_finding_state CHECK", migration);
        Assert.Contains("'confirmed'", migration);
        Assert.Contains("'candidate_llm_inferred'", migration);
        Assert.Contains("'candidate_correlated'", migration);
        Assert.Contains("'rejected'", migration);
        Assert.Contains("'superseded'", migration);
        Assert.Contains("CONSTRAINT ck_token_hotspot_attribution_type CHECK", migration);
        Assert.Contains("'direct'", migration);
        Assert.Contains("'correlated'", migration);
        Assert.Contains("'llm_inferred'", migration);
        Assert.Contains("'unavailable'", migration);
        Assert.Contains("CONSTRAINT ck_token_hotspot_prompt_cache_evidence_state CHECK", migration);
        Assert.Contains("'known_reason'", migration);
        Assert.Contains("'inferred_candidate'", migration);
        Assert.Contains("'unknown'", migration);
        Assert.Contains("'unavailable'", migration);
        Assert.Contains("'not_applicable'", migration);
        Assert.Contains("CONSTRAINT ck_token_hotspot_llm_candidate_boundary", migration);
        Assert.Contains("CONSTRAINT ck_token_hotspot_confirmed_authority", migration);
        Assert.Contains("CONSTRAINT ck_token_hotspot_confirmed_metric_authority", migration);
        Assert.Contains("CONSTRAINT ck_token_hotspot_evidence_refs_json", migration);
        Assert.Contains("CONSTRAINT ck_token_hotspot_detection_key", migration);
        Assert.Contains("ix_token_hotspot_session_state", migration);
        Assert.Contains("ux_token_hotspot_customer_detection_key", migration);

        var hotspotSection = migration[migration.IndexOf(
            "CREATE TABLE IF NOT EXISTS token_hotspot",
            StringComparison.Ordinal)..];
        Assert.DoesNotContain("raw_prompt", hotspotSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prompt_text", hotspotSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("code_content", hotspotSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("command_output", hotspotSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tool_result", hotspotSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("developer_rank", hotspotSection, StringComparison.OrdinalIgnoreCase);
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
    public void PostgreSqlTenantMetadataMigrationPersistsRecommendationEvidenceWorkflow()
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

        Assert.Contains("CREATE TABLE IF NOT EXISTS recommendation", migration);
        Assert.Contains("CREATE TABLE IF NOT EXISTS recommendation_evidence", migration);
        Assert.Contains("CREATE TABLE IF NOT EXISTS recommendation_regeneration_request", migration);
        Assert.Contains("recommendation_id uuid PRIMARY KEY", migration);
        Assert.Contains("recommendation_evidence_id uuid PRIMARY KEY", migration);
        Assert.Contains("recommendation_regeneration_request_id uuid PRIMARY KEY", migration);
        Assert.Contains("customer_organization_id uuid NOT NULL", migration);
        Assert.Contains("agent_session_id text NOT NULL", migration);
        Assert.Contains("token_hotspot_id uuid NULL", migration);
        Assert.Contains("recommendation_kind text NOT NULL", migration);
        Assert.Contains("recommendation_state text NOT NULL", migration);
        Assert.Contains("authority_state text NOT NULL", migration);
        Assert.Contains("confidence text NOT NULL", migration);
        Assert.Contains("validation_state text NOT NULL", migration);
        Assert.Contains("visibility_scope text NOT NULL", migration);
        Assert.Contains("evidence_packet_json jsonb NOT NULL", migration);
        Assert.Contains("evidence_packet_hash text NOT NULL", migration);
        Assert.Contains("policy_metadata_json jsonb NOT NULL", migration);
        Assert.Contains("CONSTRAINT fk_recommendation_agent_session", migration);
        Assert.Contains("CONSTRAINT fk_recommendation_token_hotspot", migration);
        Assert.Contains("CONSTRAINT fk_recommendation_audit_event", migration);
        Assert.Contains("CONSTRAINT fk_recommendation_evidence_recommendation", migration);
        Assert.Contains("CONSTRAINT fk_recommendation_regeneration_audit_event", migration);
        Assert.Contains("ck_recommendation_kind CHECK (recommendation_kind IN ('deterministic', 'llm_assisted', 'mixed'))", migration);
        Assert.Contains("ck_recommendation_state CHECK (recommendation_state IN ('candidate', 'accepted', 'rejected', 'expired', 'superseded'))", migration);
        Assert.Contains("ck_recommendation_authority_state CHECK (authority_state IN ('deterministic', 'llm_assisted', 'llm_inferred_candidate', 'rejected'))", migration);
        Assert.Contains("ck_recommendation_confidence CHECK (confidence IN ('low', 'medium', 'high'))", migration);
        Assert.Contains("ck_recommendation_evidence_packet_json", migration);
        Assert.Contains("ix_recommendation_session_state", migration);
        Assert.Contains("ix_recommendation_regeneration_customer_state", migration);

        var recommendationSection = migration[migration.IndexOf(
            "CREATE TABLE IF NOT EXISTS recommendation",
            StringComparison.Ordinal)..];
        Assert.DoesNotContain("raw_prompt", recommendationSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prompt_text", recommendationSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("code_content", recommendationSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("command_output", recommendationSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tool_result", recommendationSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("developer_rank", recommendationSection, StringComparison.OrdinalIgnoreCase);
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
