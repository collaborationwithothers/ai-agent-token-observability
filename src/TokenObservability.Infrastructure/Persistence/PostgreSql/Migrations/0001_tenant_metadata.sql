CREATE TABLE IF NOT EXISTS customer_organization (
    customer_organization_id uuid PRIMARY KEY,
    slug text NOT NULL,
    display_name text NOT NULL,
    data_residency_region text NOT NULL,
    isolation_tier text NOT NULL,
    status text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    CONSTRAINT uq_customer_organization_slug UNIQUE (slug),
    CONSTRAINT ck_customer_organization_slug CHECK (slug = lower(slug) AND slug ~ '^[a-z0-9][a-z0-9-]*[a-z0-9]$|^[a-z0-9]$'),
    CONSTRAINT ck_customer_organization_isolation_tier CHECK (isolation_tier IN ('shared', 'dedicated_data', 'dedicated_cell')),
    CONSTRAINT ck_customer_organization_status CHECK (status IN ('active', 'suspended', 'offboarding', 'deleted')),
    CONSTRAINT ck_customer_organization_timestamps CHECK (updated_at_utc >= created_at_utc)
);

CREATE TABLE IF NOT EXISTS identity_tenant (
    identity_tenant_id uuid PRIMARY KEY,
    customer_organization_id uuid NOT NULL REFERENCES customer_organization (customer_organization_id),
    provider text NOT NULL,
    issuer text NOT NULL,
    external_tenant_id text NOT NULL,
    allowed_audiences_json jsonb NOT NULL,
    jwks_uri text NULL,
    display_name text NOT NULL,
    status text NOT NULL,
    last_validated_at_utc timestamptz NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    CONSTRAINT uq_identity_tenant_customer_identity UNIQUE (customer_organization_id, provider, external_tenant_id),
    CONSTRAINT uq_identity_tenant_customer_identity_id UNIQUE (customer_organization_id, identity_tenant_id),
    CONSTRAINT ck_identity_tenant_provider CHECK (provider IN ('microsoft_entra')),
    CONSTRAINT ck_identity_tenant_status CHECK (status IN ('active', 'disabled', 'pending_validation')),
    CONSTRAINT ck_identity_tenant_timestamps CHECK (updated_at_utc >= created_at_utc)
);

CREATE TABLE IF NOT EXISTS product_user (
    product_user_id uuid PRIMARY KEY,
    customer_organization_id uuid NOT NULL,
    identity_tenant_id uuid NOT NULL,
    external_subject_id text NOT NULL,
    display_label text NOT NULL,
    email text NULL,
    status text NOT NULL,
    first_seen_at_utc timestamptz NOT NULL,
    last_seen_at_utc timestamptz NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    CONSTRAINT fk_product_user_customer_organization FOREIGN KEY (customer_organization_id) REFERENCES customer_organization (customer_organization_id),
    CONSTRAINT fk_product_user_identity_tenant FOREIGN KEY (customer_organization_id, identity_tenant_id) REFERENCES identity_tenant (customer_organization_id, identity_tenant_id),
    CONSTRAINT uq_product_user_external_subject UNIQUE (customer_organization_id, identity_tenant_id, external_subject_id),
    CONSTRAINT uq_product_user_customer_user_id UNIQUE (customer_organization_id, product_user_id),
    CONSTRAINT ck_product_user_status CHECK (status IN ('active', 'disabled', 'deleted')),
    CONSTRAINT ck_product_user_seen_timestamps CHECK (last_seen_at_utc IS NULL OR last_seen_at_utc >= first_seen_at_utc),
    CONSTRAINT ck_product_user_timestamps CHECK (updated_at_utc >= created_at_utc)
);

CREATE TABLE IF NOT EXISTS governance_audit_event (
    audit_event_id text NOT NULL,
    customer_organization_id uuid NOT NULL REFERENCES customer_organization (customer_organization_id),
    actor_product_user_id uuid NULL,
    effective_role text NULL,
    action text NOT NULL,
    target_resource_kind text NOT NULL,
    target_resource_id text NOT NULL,
    decision text NOT NULL,
    denial_reason text NULL,
    correlation_id text NOT NULL,
    evidence_metadata_json jsonb NOT NULL,
    created_at_utc timestamptz NOT NULL,
    CONSTRAINT pk_governance_audit_event PRIMARY KEY (customer_organization_id, audit_event_id),
    CONSTRAINT fk_governance_audit_event_actor FOREIGN KEY (customer_organization_id, actor_product_user_id) REFERENCES product_user (customer_organization_id, product_user_id),
    CONSTRAINT ck_governance_audit_event_effective_role CHECK (effective_role IS NULL OR effective_role IN ('PlatformAdmin', 'SecurityReviewer', 'EngineeringLead', 'Developer', 'ReadOnlyViewer')),
    CONSTRAINT ck_governance_audit_event_decision CHECK (decision IN ('created', 'updated', 'disabled', 'denied')),
    CONSTRAINT ck_governance_audit_event_denial_reason CHECK (denial_reason IS NULL OR denial_reason IN ('MissingRoleMapping', 'InsufficientRole', 'ScopeMismatch', 'InvalidTenant')),
    CONSTRAINT ck_governance_audit_event_evidence_metadata_json CHECK (jsonb_typeof(evidence_metadata_json) = 'object'),
    CONSTRAINT ck_governance_audit_event_denial_shape CHECK (
        (decision = 'denied' AND denial_reason IS NOT NULL)
        OR (decision <> 'denied' AND denial_reason IS NULL)
    )
);

CREATE TABLE IF NOT EXISTS product_role_mapping (
    product_role_mapping_id uuid PRIMARY KEY,
    customer_organization_id uuid NOT NULL,
    identity_tenant_id uuid NOT NULL,
    external_principal_type text NOT NULL,
    external_principal_id text NOT NULL,
    product_role text NOT NULL,
    scope_kind text NOT NULL,
    scope_id text NULL,
    status text NOT NULL,
    effective_from_utc timestamptz NOT NULL,
    effective_to_utc timestamptz NULL,
    created_by_product_user_id uuid NOT NULL,
    changed_by_product_user_id uuid NOT NULL,
    audit_event_id text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    CONSTRAINT fk_product_role_mapping_customer_organization FOREIGN KEY (customer_organization_id) REFERENCES customer_organization (customer_organization_id),
    CONSTRAINT fk_product_role_mapping_identity_tenant FOREIGN KEY (customer_organization_id, identity_tenant_id) REFERENCES identity_tenant (customer_organization_id, identity_tenant_id),
    CONSTRAINT fk_product_role_mapping_created_by_product_user FOREIGN KEY (customer_organization_id, created_by_product_user_id) REFERENCES product_user (customer_organization_id, product_user_id),
    CONSTRAINT fk_product_role_mapping_changed_by_product_user FOREIGN KEY (customer_organization_id, changed_by_product_user_id) REFERENCES product_user (customer_organization_id, product_user_id),
    CONSTRAINT fk_product_role_mapping_audit_event FOREIGN KEY (customer_organization_id, audit_event_id) REFERENCES governance_audit_event (customer_organization_id, audit_event_id),
    CONSTRAINT ck_product_role_mapping_external_principal_type CHECK (external_principal_type IN ('app_role', 'group_object_id', 'user_subject', 'service_principal')),
    CONSTRAINT ck_product_role_mapping_product_role CHECK (product_role IN ('PlatformAdmin', 'SecurityReviewer', 'EngineeringLead', 'Developer', 'ReadOnlyViewer')),
    CONSTRAINT ck_product_role_mapping_scope_kind CHECK (scope_kind IN ('organization', 'team', 'repository', 'harness_profile', 'self', 'content_review_queue', 'pricing', 'tenant_admin')),
    CONSTRAINT ck_product_role_mapping_status CHECK (status IN ('active', 'disabled', 'expired')),
    CONSTRAINT ck_product_role_mapping_effective_window CHECK (effective_to_utc IS NULL OR effective_to_utc > effective_from_utc),
    CONSTRAINT ck_product_role_mapping_scope_id_required CHECK (
        (scope_kind IN ('organization', 'self') AND scope_id IS NULL)
        OR (scope_kind NOT IN ('organization', 'self') AND scope_id IS NOT NULL)
    ),
    CONSTRAINT ck_product_role_mapping_timestamps CHECK (updated_at_utc >= created_at_utc)
);

CREATE TABLE IF NOT EXISTS scoped_ingestion_credential (
    scoped_ingestion_credential_id uuid PRIMARY KEY,
    customer_organization_id uuid NOT NULL,
    harness_setup_profile_id text NOT NULL,
    product_user_id uuid NOT NULL,
    credential_hash text NOT NULL,
    credential_prefix text NULL,
    allowed_harness text NOT NULL,
    allowed_scopes_json jsonb NOT NULL,
    status text NOT NULL,
    expires_at_utc timestamptz NOT NULL,
    last_used_at_utc timestamptz NULL,
    rotated_at_utc timestamptz NULL,
    revoked_at_utc timestamptz NULL,
    created_by_product_user_id uuid NOT NULL,
    changed_by_product_user_id uuid NOT NULL,
    audit_event_ids_json jsonb NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    CONSTRAINT fk_scoped_ingestion_credential_customer_organization FOREIGN KEY (customer_organization_id) REFERENCES customer_organization (customer_organization_id),
    CONSTRAINT fk_scoped_ingestion_credential_product_user FOREIGN KEY (customer_organization_id, product_user_id) REFERENCES product_user (customer_organization_id, product_user_id),
    CONSTRAINT fk_scoped_ingestion_credential_created_by_product_user FOREIGN KEY (customer_organization_id, created_by_product_user_id) REFERENCES product_user (customer_organization_id, product_user_id),
    CONSTRAINT fk_scoped_ingestion_credential_changed_by_product_user FOREIGN KEY (customer_organization_id, changed_by_product_user_id) REFERENCES product_user (customer_organization_id, product_user_id),
    CONSTRAINT uq_scoped_ingestion_credential_customer_credential UNIQUE (customer_organization_id, scoped_ingestion_credential_id),
    CONSTRAINT ck_scoped_ingestion_credential_allowed_harness CHECK (allowed_harness IN ('codex_cli')),
    CONSTRAINT ck_scoped_ingestion_credential_allowed_scopes_json CHECK (jsonb_typeof(allowed_scopes_json) = 'array'),
    CONSTRAINT ck_scoped_ingestion_credential_audit_event_ids_json CHECK (jsonb_typeof(audit_event_ids_json) = 'array'),
    CONSTRAINT ck_scoped_ingestion_credential_status CHECK (status IN ('active', 'disabled', 'revoked', 'expired', 'pending_rotation')),
    CONSTRAINT ck_scoped_ingestion_credential_expiry_window CHECK (expires_at_utc > created_at_utc),
    CONSTRAINT ck_scoped_ingestion_credential_timestamps CHECK (updated_at_utc >= created_at_utc)
);

CREATE TABLE IF NOT EXISTS ingestion_rejection (
    ingestion_rejection_id uuid PRIMARY KEY,
    customer_organization_id uuid NULL,
    harness_setup_profile_id text NULL,
    scoped_ingestion_credential_id uuid NULL,
    declared_harness text NULL,
    signal_type text NOT NULL,
    request_route text NOT NULL,
    reason_code text NOT NULL,
    http_status integer NOT NULL,
    correlation_id text NOT NULL,
    audit_event_id text NULL,
    evidence_metadata_json jsonb NOT NULL,
    received_at_utc timestamptz NOT NULL,
    CONSTRAINT fk_ingestion_rejection_customer_organization FOREIGN KEY (customer_organization_id) REFERENCES customer_organization (customer_organization_id),
    CONSTRAINT fk_ingestion_rejection_scoped_credential FOREIGN KEY (customer_organization_id, scoped_ingestion_credential_id) REFERENCES scoped_ingestion_credential (customer_organization_id, scoped_ingestion_credential_id),
    CONSTRAINT fk_ingestion_rejection_audit_event FOREIGN KEY (customer_organization_id, audit_event_id) REFERENCES governance_audit_event (customer_organization_id, audit_event_id),
    CONSTRAINT ck_ingestion_rejection_http_status CHECK (http_status BETWEEN 400 AND 599),
    CONSTRAINT ck_ingestion_rejection_evidence_metadata_json CHECK (jsonb_typeof(evidence_metadata_json) = 'object'),
    CONSTRAINT ck_ingestion_rejection_harness_setup_profile_id CHECK (
        harness_setup_profile_id IS NULL
        OR (
            harness_setup_profile_id ~ '^[A-Za-z0-9._:/-]+$'
            AND harness_setup_profile_id !~* '(bearer |sk-|accountkey=|password=|secret=|api_key=|access_token=|connection string|connectionstring|private key|raw prompt|prompt text|code content|command output|tool result|prompt|token|password|command|tool)'
        )
    ),
    CONSTRAINT ck_ingestion_rejection_declared_harness CHECK (declared_harness IS NULL OR declared_harness IN ('codex-cli')),
    CONSTRAINT ck_ingestion_rejection_link_tenant_shape CHECK (
        (customer_organization_id IS NOT NULL OR scoped_ingestion_credential_id IS NULL)
        AND (customer_organization_id IS NOT NULL OR audit_event_id IS NULL)
    )
);

CREATE TABLE IF NOT EXISTS telemetry_envelope (
    telemetry_envelope_id text PRIMARY KEY,
    customer_organization_id uuid NOT NULL,
    harness_setup_profile_id text NOT NULL,
    scoped_ingestion_credential_id uuid NOT NULL,
    product_user_id uuid NOT NULL,
    harness text NOT NULL,
    schema_version text NOT NULL,
    signal_type text NOT NULL,
    source_event_name text NOT NULL,
    source_event_timestamp_utc timestamptz NULL,
    received_at_utc timestamptz NOT NULL,
    conversation_id_hash text NULL,
    turn_id_hash text NULL,
    source_event_id text NULL,
    trace_id_hash text NULL,
    span_id_hash text NULL,
    model_name text NULL,
    harness_version text NULL,
    sandbox_setting text NULL,
    approval_setting text NULL,
    content_policy_decision text NOT NULL,
    content_capture_state text NOT NULL,
    redaction_state text NOT NULL,
    routing_decision_json jsonb NOT NULL,
    evidence_state text NOT NULL,
    metric_state text NOT NULL,
    metric_status text NOT NULL,
    metric_confidence text NOT NULL,
    source_evidence_kind text NOT NULL,
    correlation_id text NOT NULL,
    dedupe_key_hash text NOT NULL,
    ingestion_version_metadata_json jsonb NOT NULL,
    CONSTRAINT fk_telemetry_envelope_customer_organization FOREIGN KEY (customer_organization_id) REFERENCES customer_organization (customer_organization_id),
    CONSTRAINT fk_telemetry_envelope_scoped_credential FOREIGN KEY (customer_organization_id, scoped_ingestion_credential_id) REFERENCES scoped_ingestion_credential (customer_organization_id, scoped_ingestion_credential_id),
    CONSTRAINT fk_telemetry_envelope_product_user FOREIGN KEY (customer_organization_id, product_user_id) REFERENCES product_user (customer_organization_id, product_user_id),
    CONSTRAINT uq_telemetry_envelope_customer_envelope_id UNIQUE (customer_organization_id, telemetry_envelope_id),
    CONSTRAINT uq_telemetry_envelope_customer_dedupe UNIQUE (customer_organization_id, dedupe_key_hash),
    CONSTRAINT ck_telemetry_envelope_harness CHECK (harness IN ('codex-cli')),
    CONSTRAINT ck_telemetry_envelope_schema_version CHECK (schema_version ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$'),
    CONSTRAINT ck_telemetry_envelope_signal_type CHECK (signal_type IN ('log', 'trace', 'metric')),
    CONSTRAINT ck_telemetry_envelope_content_policy_decision CHECK (content_policy_decision IN ('metadata_only', 'capture_candidate', 'blocked', 'redaction_required')),
    CONSTRAINT ck_telemetry_envelope_content_capture_state CHECK (content_capture_state IN ('none', 'metadata_only', 'captured', 'review_required', 'redaction_failed', 'mixed')),
    CONSTRAINT ck_telemetry_envelope_redaction_state CHECK (redaction_state IN ('not_required', 'passed', 'failed', 'review_required')),
    CONSTRAINT ck_telemetry_envelope_evidence_state CHECK (evidence_state IN ('observed', 'derived', 'estimated', 'unavailable', 'not_applicable', 'mixed')),
    CONSTRAINT ck_telemetry_envelope_metric_state CHECK (metric_state IN ('observed', 'derived', 'estimated', 'unavailable', 'not_applicable', 'mixed')),
    CONSTRAINT ck_telemetry_envelope_metric_status CHECK (metric_status IN ('observed', 'derived', 'estimated', 'unavailable', 'not_applicable', 'mixed')),
    CONSTRAINT ck_telemetry_envelope_metric_confidence CHECK (metric_confidence IN ('observed', 'deterministic', 'estimated', 'llm_inferred', 'unavailable')),
    CONSTRAINT ck_telemetry_envelope_metric_quality CHECK (
        metric_status NOT IN ('unavailable', 'not_applicable')
        OR metric_confidence = 'unavailable'
    ),
    CONSTRAINT ck_telemetry_envelope_source_evidence_kind CHECK (source_evidence_kind IN ('harness_emitted', 'product_derived', 'scanner', 'manual_review')),
    CONSTRAINT ck_telemetry_envelope_hashes CHECK (
        (conversation_id_hash IS NULL OR conversation_id_hash ~ '^[a-f0-9]{64}$')
        AND (turn_id_hash IS NULL OR turn_id_hash ~ '^[a-f0-9]{64}$')
        AND (trace_id_hash IS NULL OR trace_id_hash ~ '^[a-f0-9]{64}$')
        AND (span_id_hash IS NULL OR span_id_hash ~ '^[a-f0-9]{64}$')
        AND dedupe_key_hash ~ '^[a-f0-9]{64}$'
    ),
    CONSTRAINT ck_telemetry_envelope_routing_decision_json CHECK (jsonb_typeof(routing_decision_json) = 'object'),
    CONSTRAINT ck_telemetry_envelope_ingestion_version_metadata_json CHECK (jsonb_typeof(ingestion_version_metadata_json) = 'object')
);

CREATE TABLE IF NOT EXISTS agent_session (
    agent_session_id text PRIMARY KEY,
    customer_organization_id uuid NOT NULL,
    product_user_id uuid NOT NULL,
    harness_setup_profile_id text NOT NULL,
    harness text NOT NULL,
    provider_session_id_hash text NULL,
    started_at_utc timestamptz NULL,
    ended_at_utc timestamptz NULL,
    session_status text NOT NULL,
    environment text NULL,
    sandbox_setting text NULL,
    approval_setting text NULL,
    repository_evidence_state text NOT NULL,
    content_capture_summary text NOT NULL,
    recommendation_status text NOT NULL,
    token_metric_status text NOT NULL,
    token_metric_confidence text NOT NULL,
    model_names_json jsonb NOT NULL,
    source_telemetry_envelope_ids_json jsonb NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    CONSTRAINT fk_agent_session_customer_organization FOREIGN KEY (customer_organization_id) REFERENCES customer_organization (customer_organization_id),
    CONSTRAINT fk_agent_session_product_user FOREIGN KEY (customer_organization_id, product_user_id) REFERENCES product_user (customer_organization_id, product_user_id),
    CONSTRAINT uq_agent_session_customer_session_id UNIQUE (customer_organization_id, agent_session_id),
    CONSTRAINT ck_agent_session_harness CHECK (harness IN ('codex-cli')),
    CONSTRAINT ck_agent_session_provider_hash CHECK (provider_session_id_hash IS NULL OR provider_session_id_hash ~ '^[a-f0-9]{64}$'),
    CONSTRAINT ck_agent_session_status CHECK (session_status IN ('active', 'completed', 'failed', 'partial', 'expired')),
    CONSTRAINT ck_agent_session_repository_evidence_state CHECK (repository_evidence_state IN ('observed', 'correlated', 'inferred', 'unavailable', 'mixed')),
    CONSTRAINT ck_agent_session_content_capture_summary CHECK (content_capture_summary IN ('none', 'metadata_only', 'captured', 'review_required', 'redaction_failed', 'mixed')),
    CONSTRAINT ck_agent_session_recommendation_status CHECK (recommendation_status IN ('not_started', 'queued', 'generated', 'failed', 'disabled')),
    CONSTRAINT ck_agent_session_token_metric_status CHECK (token_metric_status IN ('observed', 'derived', 'estimated', 'unavailable', 'not_applicable', 'mixed')),
    CONSTRAINT ck_agent_session_token_metric_confidence CHECK (token_metric_confidence IN ('observed', 'deterministic', 'estimated', 'llm_inferred', 'unavailable')),
    CONSTRAINT ck_agent_session_token_metric_quality CHECK (
        token_metric_status NOT IN ('unavailable', 'not_applicable')
        OR token_metric_confidence = 'unavailable'
    ),
    CONSTRAINT ck_agent_session_model_names_json CHECK (jsonb_typeof(model_names_json) = 'array'),
    CONSTRAINT ck_agent_session_source_envelopes_json CHECK (jsonb_typeof(source_telemetry_envelope_ids_json) = 'array'),
    CONSTRAINT ck_agent_session_timestamps CHECK (
        updated_at_utc >= created_at_utc
        AND (started_at_utc IS NULL OR ended_at_utc IS NULL OR ended_at_utc >= started_at_utc)
    )
);

CREATE TABLE IF NOT EXISTS token_observation (
    token_observation_id uuid PRIMARY KEY,
    customer_organization_id uuid NOT NULL,
    agent_session_id text NOT NULL,
    model_invocation_id text NULL,
    metric_name text NOT NULL,
    value bigint NULL,
    metric_status text NOT NULL,
    metric_confidence text NOT NULL,
    source_kind text NOT NULL,
    source_telemetry_envelope_id text NULL,
    created_at_utc timestamptz NOT NULL,
    CONSTRAINT fk_token_observation_agent_session FOREIGN KEY (customer_organization_id, agent_session_id) REFERENCES agent_session (customer_organization_id, agent_session_id),
    CONSTRAINT fk_token_observation_telemetry_envelope FOREIGN KEY (customer_organization_id, source_telemetry_envelope_id) REFERENCES telemetry_envelope (customer_organization_id, telemetry_envelope_id),
    CONSTRAINT ck_token_observation_metric_name CHECK (metric_name IN ('input_tokens', 'output_tokens', 'cached_input_tokens', 'reasoning_output_tokens', 'total_tokens')),
    CONSTRAINT ck_token_observation_metric_status CHECK (metric_status IN ('observed', 'derived', 'estimated', 'unavailable', 'not_applicable', 'mixed')),
    CONSTRAINT ck_token_observation_metric_confidence CHECK (metric_confidence IN ('observed', 'deterministic', 'estimated', 'llm_inferred', 'unavailable')),
    CONSTRAINT ck_token_observation_source_kind CHECK (source_kind IN ('codex_event', 'otel_metric', 'derived_summary', 'estimator', 'missing')),
    CONSTRAINT ck_token_observation_value_shape CHECK (
        value IS NULL
        OR value >= 0
    ),
    CONSTRAINT ck_token_observation_null_semantics CHECK (
        (metric_status IN ('unavailable', 'not_applicable') AND value IS NULL AND metric_confidence = 'unavailable')
        OR (metric_status NOT IN ('unavailable', 'not_applicable') AND value IS NOT NULL)
    ),
    CONSTRAINT ck_token_observation_zero_semantics CHECK (
        value IS DISTINCT FROM 0
        OR (
            metric_status IN ('observed', 'derived')
            AND metric_confidence IN ('observed', 'deterministic')
        )
    ),
    CONSTRAINT ck_token_observation_missing_source_semantics CHECK (
        source_kind <> 'missing'
        OR metric_status IN ('unavailable', 'not_applicable')
    )
);

CREATE TABLE IF NOT EXISTS aggregate_metric_point (
    aggregate_metric_point_id uuid PRIMARY KEY,
    customer_organization_id uuid NOT NULL,
    agent_session_id text NOT NULL,
    metric_name text NOT NULL,
    metric_value double precision NOT NULL,
    unit text NOT NULL,
    labels_json jsonb NOT NULL,
    exported_at_utc timestamptz NOT NULL,
    CONSTRAINT fk_aggregate_metric_point_agent_session FOREIGN KEY (customer_organization_id, agent_session_id) REFERENCES agent_session (customer_organization_id, agent_session_id),
    CONSTRAINT ck_aggregate_metric_point_name CHECK (metric_name IN ('tokenobs_tokens_total', 'tokenobs_sessions_started_total', 'tokenobs_token_metric_states_total')),
    CONSTRAINT ck_aggregate_metric_point_value CHECK (metric_value >= 0),
    CONSTRAINT ck_aggregate_metric_point_unit CHECK (unit IN ('tokens', 'sessions', 'observations')),
    CONSTRAINT ck_aggregate_metric_point_labels_json CHECK (
        jsonb_typeof(labels_json) = 'object'
        AND labels_json ? 'customer_organization_slug'
        AND labels_json ? 'environment'
        AND labels_json ? 'region'
        AND NOT labels_json ? 'agent_session_id'
        AND NOT labels_json ? 'product_user_id'
        AND NOT labels_json ? 'credential_id'
        AND NOT labels_json ? 'trace_id'
        AND NOT labels_json ? 'span_id'
        AND NOT labels_json ? 'repository_path'
        AND NOT labels_json ? 'file_path'
    )
);

CREATE TABLE IF NOT EXISTS aggregate_metric_export_failure (
    aggregate_metric_export_failure_id uuid PRIMARY KEY,
    customer_organization_id uuid NOT NULL,
    agent_session_id text NOT NULL,
    failure_reason text NOT NULL,
    correlation_id text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    CONSTRAINT fk_aggregate_metric_export_failure_agent_session FOREIGN KEY (customer_organization_id, agent_session_id) REFERENCES agent_session (customer_organization_id, agent_session_id),
    CONSTRAINT ck_aggregate_metric_export_failure_reason CHECK (failure_reason IN ('sink_failure', 'invalid_metric_shape'))
);

CREATE INDEX IF NOT EXISTS ix_customer_organization_status
    ON customer_organization (status);

CREATE INDEX IF NOT EXISTS ix_identity_tenant_customer_status
    ON identity_tenant (customer_organization_id, status);

CREATE INDEX IF NOT EXISTS ix_product_user_customer_status
    ON product_user (customer_organization_id, status);

CREATE INDEX IF NOT EXISTS ix_product_role_mapping_customer_status
    ON product_role_mapping (customer_organization_id, status);

CREATE INDEX IF NOT EXISTS ix_governance_audit_event_customer_created
    ON governance_audit_event (customer_organization_id, created_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_scoped_ingestion_credential_customer_status
    ON scoped_ingestion_credential (customer_organization_id, status);

CREATE UNIQUE INDEX IF NOT EXISTS ux_scoped_ingestion_credential_active_hash
    ON scoped_ingestion_credential (credential_hash)
    WHERE status = 'active';

CREATE INDEX IF NOT EXISTS ix_ingestion_rejection_customer_received
    ON ingestion_rejection (customer_organization_id, received_at_utc DESC)
    WHERE customer_organization_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_ingestion_rejection_reason_received
    ON ingestion_rejection (reason_code, received_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_telemetry_envelope_customer_received
    ON telemetry_envelope (customer_organization_id, received_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_telemetry_envelope_session_correlation
    ON telemetry_envelope (customer_organization_id, harness_setup_profile_id, product_user_id, conversation_id_hash, source_event_timestamp_utc);

CREATE INDEX IF NOT EXISTS ix_agent_session_customer_updated
    ON agent_session (customer_organization_id, updated_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_token_observation_session_metric
    ON token_observation (customer_organization_id, agent_session_id, metric_name, created_at_utc);

CREATE INDEX IF NOT EXISTS ix_aggregate_metric_point_customer_exported
    ON aggregate_metric_point (customer_organization_id, exported_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_aggregate_metric_export_failure_customer_created
    ON aggregate_metric_export_failure (customer_organization_id, created_at_utc DESC);

CREATE UNIQUE INDEX IF NOT EXISTS ux_agent_session_customer_provider_session
    ON agent_session (customer_organization_id, harness_setup_profile_id, product_user_id, provider_session_id_hash)
    WHERE provider_session_id_hash IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_product_role_mapping_active_principal_role_scope
    ON product_role_mapping (
        customer_organization_id,
        identity_tenant_id,
        external_principal_type,
        external_principal_id,
        product_role,
        scope_kind,
        COALESCE(scope_id, '')
    )
    WHERE status = 'active';
