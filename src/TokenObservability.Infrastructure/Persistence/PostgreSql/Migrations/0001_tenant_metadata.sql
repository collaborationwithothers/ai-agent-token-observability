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

CREATE TABLE IF NOT EXISTS content_reference (
    content_reference_id uuid PRIMARY KEY,
    customer_organization_id uuid NOT NULL,
    agent_session_id text NULL,
    telemetry_envelope_id text NOT NULL,
    content_class text NOT NULL,
    capture_state text NOT NULL,
    redaction_status text NOT NULL,
    content_hash text NULL,
    blob_uri text NULL,
    blob_container text NULL,
    blob_name text NULL,
    blob_version text NULL,
    policy_version_id text NOT NULL,
    redaction_pipeline_version text NULL,
    product_rule_version text NULL,
    retention_class text NOT NULL,
    expires_at_utc timestamptz NULL,
    recommendation_eligible boolean NOT NULL,
    approved_excerpt_hash text NULL,
    audit_event_id text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    CONSTRAINT fk_content_reference_customer_organization FOREIGN KEY (customer_organization_id) REFERENCES customer_organization (customer_organization_id),
    CONSTRAINT fk_content_reference_agent_session FOREIGN KEY (customer_organization_id, agent_session_id) REFERENCES agent_session (customer_organization_id, agent_session_id),
    CONSTRAINT fk_content_reference_telemetry_envelope FOREIGN KEY (customer_organization_id, telemetry_envelope_id) REFERENCES telemetry_envelope (customer_organization_id, telemetry_envelope_id),
    CONSTRAINT fk_content_reference_audit_event FOREIGN KEY (customer_organization_id, audit_event_id) REFERENCES governance_audit_event (customer_organization_id, audit_event_id),
    CONSTRAINT uq_content_reference_customer_reference UNIQUE (customer_organization_id, content_reference_id),
    CONSTRAINT ck_content_reference_content_class CHECK (content_class IN ('prompt_snippet', 'tool_input_excerpt', 'tool_output_excerpt', 'model_response_excerpt', 'command_summary', 'file_content_excerpt', 'metadata_only')),
    CONSTRAINT ck_content_reference_capture_state CHECK (capture_state IN ('not_allowed', 'metadata_only', 'captured', 'redaction_failed', 'review_required', 'discarded', 'approved_excerpt')),
    CONSTRAINT ck_content_reference_redaction_status CHECK (redaction_status IN ('not_required', 'passed', 'failed', 'review_required', 'manually_approved')),
    CONSTRAINT ck_content_reference_retention_class CHECK (retention_class IN ('metadata_only', 'short', 'review', 'blocked')),
    CONSTRAINT ck_content_reference_hashes CHECK (
        content_hash IS NULL OR content_hash ~ '^[a-f0-9]{64}$'
    ),
    CONSTRAINT ck_content_reference_approved_excerpt_hash CHECK (
        approved_excerpt_hash IS NULL OR approved_excerpt_hash ~ '^[a-f0-9]{64}$'
    ),
    CONSTRAINT ck_content_reference_blob_pointer_state CHECK (
        (
            capture_state IN ('captured', 'approved_excerpt')
            AND blob_uri IS NOT NULL
            AND blob_container IS NOT NULL
            AND blob_name IS NOT NULL
        )
        OR
        (
            capture_state IN ('not_allowed', 'metadata_only', 'redaction_failed', 'review_required', 'discarded')
            AND blob_uri IS NULL
            AND blob_container IS NULL
            AND blob_name IS NULL
            AND blob_version IS NULL
        )
    ),
    CONSTRAINT ck_content_reference_captured_status CHECK (
        capture_state <> 'captured' OR redaction_status = 'passed'
    ),
    CONSTRAINT ck_content_reference_approved_excerpt_status CHECK (
        capture_state <> 'approved_excerpt' OR redaction_status = 'manually_approved'
    ),
    CONSTRAINT ck_content_reference_retention_deadline CHECK (
        capture_state NOT IN ('captured', 'approved_excerpt') OR expires_at_utc IS NOT NULL
    ),
    CONSTRAINT ck_content_reference_timestamps CHECK (updated_at_utc >= created_at_utc)
);

CREATE INDEX IF NOT EXISTS ix_content_reference_customer_session_state
    ON content_reference (customer_organization_id, agent_session_id, capture_state);

CREATE INDEX IF NOT EXISTS ix_content_reference_customer_review_state
    ON content_reference (customer_organization_id, capture_state, created_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_content_reference_customer_expires
    ON content_reference (customer_organization_id, expires_at_utc)
    WHERE expires_at_utc IS NOT NULL;

CREATE TABLE IF NOT EXISTS redaction_review (
    redaction_review_id uuid PRIMARY KEY,
    customer_organization_id uuid NOT NULL,
    content_reference_id uuid NOT NULL,
    reviewer_product_user_id uuid NOT NULL,
    decision text NOT NULL,
    decision_reason text NULL,
    decided_at_utc timestamptz NOT NULL,
    audit_event_id text NOT NULL,
    correlation_id text NOT NULL,
    CONSTRAINT fk_redaction_review_customer_organization FOREIGN KEY (customer_organization_id) REFERENCES customer_organization (customer_organization_id),
    CONSTRAINT fk_redaction_review_content_reference FOREIGN KEY (customer_organization_id, content_reference_id) REFERENCES content_reference (customer_organization_id, content_reference_id),
    CONSTRAINT fk_redaction_review_reviewer_product_user FOREIGN KEY (customer_organization_id, reviewer_product_user_id) REFERENCES product_user (customer_organization_id, product_user_id),
    CONSTRAINT fk_redaction_review_audit_event FOREIGN KEY (customer_organization_id, audit_event_id) REFERENCES governance_audit_event (customer_organization_id, audit_event_id),
    CONSTRAINT uq_redaction_review_customer_review UNIQUE (customer_organization_id, redaction_review_id),
    CONSTRAINT ck_redaction_review_decision CHECK (decision IN ('retry', 'discard', 'approve_excerpt', 'reject_excerpt', 'mark_recommendation_ineligible')),
    CONSTRAINT ck_redaction_review_decision_reason CHECK (
        decision_reason IS NULL OR (
            length(decision_reason) <= 256
            AND decision_reason !~* '(raw[ _-]?prompt|prompt[ _-]?text|code[ _-]?content|command[ _-]?output|tool[ _-]?result|secret|password|api[ _-]?key|access[ _-]?token)'
        )
    )
);

CREATE INDEX IF NOT EXISTS ix_redaction_review_customer_content_reference
    ON redaction_review (customer_organization_id, content_reference_id, decided_at_utc DESC);

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

CREATE TABLE IF NOT EXISTS pricing_basis (
    pricing_basis_id text PRIMARY KEY,
    customer_organization_id uuid NOT NULL,
    harness text NOT NULL,
    provider_name text NOT NULL,
    model_name text NOT NULL,
    token_type text NOT NULL,
    billing_route text NOT NULL,
    currency text NOT NULL,
    price_per_million_tokens numeric(18, 8) NOT NULL,
    pricing_version text NOT NULL,
    source_kind text NOT NULL,
    review_state text NOT NULL,
    effective_from_utc timestamptz NOT NULL,
    effective_to_utc timestamptz NULL,
    audit_event_id text NOT NULL,
    source_metadata_json jsonb NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    CONSTRAINT fk_pricing_basis_customer_organization FOREIGN KEY (customer_organization_id) REFERENCES customer_organization (customer_organization_id),
    CONSTRAINT fk_pricing_basis_audit_event FOREIGN KEY (customer_organization_id, audit_event_id) REFERENCES governance_audit_event (customer_organization_id, audit_event_id),
    CONSTRAINT uq_pricing_basis_customer_basis_id UNIQUE (customer_organization_id, pricing_basis_id),
    CONSTRAINT ck_pricing_basis_harness CHECK (harness IN ('codex-cli')),
    CONSTRAINT ck_pricing_basis_token_type CHECK (token_type IN ('input', 'output', 'cached_input', 'reasoning_output')),
    CONSTRAINT ck_pricing_basis_currency CHECK (currency ~ '^[A-Z]{3}$'),
    CONSTRAINT ck_pricing_basis_non_negative_price CHECK (price_per_million_tokens >= 0),
    CONSTRAINT ck_pricing_basis_source_kind CHECK (source_kind IN ('automated_seed', 'admin_override', 'provider_docs', 'enterprise_contract')),
    CONSTRAINT ck_pricing_basis_review_state CHECK (review_state IN ('candidate', 'approved', 'rejected', 'superseded')),
    CONSTRAINT ck_pricing_basis_source_metadata_json CHECK (jsonb_typeof(source_metadata_json) = 'object'),
    CONSTRAINT ck_pricing_basis_effective_window CHECK (effective_to_utc IS NULL OR effective_to_utc > effective_from_utc),
    CONSTRAINT ck_pricing_basis_timestamps CHECK (updated_at_utc >= created_at_utc)
);

CREATE TABLE IF NOT EXISTS cost_estimate (
    cost_estimate_id text PRIMARY KEY,
    customer_organization_id uuid NOT NULL,
    agent_session_id text NOT NULL,
    model_invocation_id text NULL,
    pricing_basis_id text NULL,
    pricing_version text NULL,
    currency text NOT NULL,
    estimated_cost numeric(18, 12) NULL,
    cost_status text NOT NULL,
    source_kind text NOT NULL,
    token_metric_status text NOT NULL,
    token_metric_confidence text NOT NULL,
    provider_name text NOT NULL,
    model_name text NOT NULL,
    billing_route text NOT NULL,
    token_type text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    CONSTRAINT fk_cost_estimate_agent_session FOREIGN KEY (customer_organization_id, agent_session_id) REFERENCES agent_session (customer_organization_id, agent_session_id),
    CONSTRAINT fk_cost_estimate_pricing_basis FOREIGN KEY (customer_organization_id, pricing_basis_id) REFERENCES pricing_basis (customer_organization_id, pricing_basis_id),
    CONSTRAINT ck_cost_estimate_currency CHECK (currency ~ '^[A-Z]{3}$'),
    CONSTRAINT ck_cost_estimate_non_negative CHECK (estimated_cost IS NULL OR estimated_cost >= 0),
    CONSTRAINT ck_cost_estimate_status CHECK (cost_status IN ('estimated', 'unavailable', 'not_applicable', 'mixed')),
    CONSTRAINT ck_cost_estimate_source_kind CHECK (source_kind IN ('derived_from_observed_tokens', 'derived_from_estimated_tokens', 'manual_override', 'unavailable')),
    CONSTRAINT ck_cost_estimate_token_metric_status CHECK (token_metric_status IN ('observed', 'derived', 'estimated', 'unavailable', 'not_applicable', 'mixed')),
    CONSTRAINT ck_cost_estimate_token_metric_confidence CHECK (token_metric_confidence IN ('observed', 'deterministic', 'estimated', 'llm_inferred', 'unavailable')),
    CONSTRAINT ck_cost_estimate_token_type CHECK (token_type IN ('input', 'output', 'cached_input', 'reasoning_output')),
    CONSTRAINT ck_cost_estimate_unavailable_null_semantics CHECK (
        (
            cost_status IN ('unavailable', 'not_applicable')
            AND estimated_cost IS NULL
            AND pricing_basis_id IS NULL
            AND pricing_version IS NULL
            AND source_kind = 'unavailable'
        )
        OR (
            cost_status IN ('estimated', 'mixed')
            AND estimated_cost IS NOT NULL
            AND pricing_basis_id IS NOT NULL
            AND pricing_version IS NOT NULL
        )
    )
);

CREATE TABLE IF NOT EXISTS product_api_idempotency (
    customer_organization_id uuid NOT NULL,
    product_user_id uuid NOT NULL,
    route text NOT NULL,
    idempotency_key text NOT NULL,
    request_hash text NOT NULL,
    operation_id text NULL,
    response_status_code integer NULL,
    response_location text NULL,
    response_json jsonb NULL,
    created_at_utc timestamptz NOT NULL,
    expires_at_utc timestamptz NOT NULL,
    completed_at_utc timestamptz NULL,
    PRIMARY KEY (customer_organization_id, product_user_id, route, idempotency_key),
    CONSTRAINT fk_product_api_idempotency_customer_organization FOREIGN KEY (customer_organization_id) REFERENCES customer_organization (customer_organization_id),
    CONSTRAINT fk_product_api_idempotency_product_user FOREIGN KEY (customer_organization_id, product_user_id) REFERENCES product_user (customer_organization_id, product_user_id),
    CONSTRAINT ck_product_api_idempotency_route CHECK (route ~ '^/api/v1/'),
    CONSTRAINT ck_product_api_idempotency_key CHECK (length(idempotency_key) BETWEEN 1 AND 200),
    CONSTRAINT ck_product_api_idempotency_request_hash CHECK (request_hash ~ '^[A-F0-9]{64}$'),
    CONSTRAINT ck_product_api_idempotency_success_status CHECK (response_status_code IS NULL OR response_status_code BETWEEN 200 AND 299),
    CONSTRAINT ck_product_api_idempotency_response_json CHECK (response_json IS NULL OR jsonb_typeof(response_json) = 'object'),
    CONSTRAINT ck_product_api_idempotency_expiry CHECK (expires_at_utc > created_at_utc),
    CONSTRAINT ck_product_api_idempotency_completion CHECK (
        (
            completed_at_utc IS NULL
            AND operation_id IS NULL
            AND response_status_code IS NULL
            AND response_json IS NULL
        )
        OR
        (
            completed_at_utc IS NOT NULL
            AND completed_at_utc >= created_at_utc
            AND operation_id IS NOT NULL
            AND response_status_code IS NOT NULL
            AND response_json IS NOT NULL
        )
    )
);

CREATE TABLE IF NOT EXISTS token_hotspot (
    token_hotspot_id uuid PRIMARY KEY,
    customer_organization_id uuid NOT NULL,
    agent_session_id text NOT NULL,
    harness text NOT NULL,
    model_name text NULL,
    hotspot_type text NOT NULL,
    finding_state text NOT NULL,
    attribution_type text NOT NULL,
    confidence text NOT NULL,
    metric_status text NOT NULL,
    metric_confidence text NOT NULL,
    prompt_cache_evidence_state text NOT NULL,
    evidence_summary text NOT NULL,
    evidence_refs_json jsonb NOT NULL,
    detection_key text NULL,
    token_burn_score double precision NULL,
    estimated_cost_impact numeric NULL,
    created_at_utc timestamptz NOT NULL,
    CONSTRAINT fk_token_hotspot_agent_session FOREIGN KEY (customer_organization_id, agent_session_id) REFERENCES agent_session (customer_organization_id, agent_session_id),
    CONSTRAINT uq_token_hotspot_customer_hotspot UNIQUE (customer_organization_id, token_hotspot_id),
    CONSTRAINT ck_token_hotspot_harness CHECK (harness IN ('codex-cli')),
    CONSTRAINT ck_token_hotspot_type CHECK (hotspot_type IN ('prompt_cache_breakage', 'large_context', 'tool_loop', 'model_retry', 'repo_context_bloat', 'generated_artifact_bloat', 'expensive_model_choice', 'error_rework', 'unknown')),
    CONSTRAINT ck_token_hotspot_finding_state CHECK (finding_state IN ('confirmed', 'candidate_llm_inferred', 'candidate_correlated', 'rejected', 'superseded')),
    CONSTRAINT ck_token_hotspot_attribution_type CHECK (attribution_type IN ('direct', 'correlated', 'llm_inferred', 'unavailable')),
    CONSTRAINT ck_token_hotspot_confidence CHECK (confidence IN ('high', 'medium', 'low', 'unavailable')),
    CONSTRAINT ck_token_hotspot_metric_status CHECK (metric_status IN ('observed', 'derived', 'estimated', 'unavailable', 'not_applicable', 'mixed')),
    CONSTRAINT ck_token_hotspot_metric_confidence CHECK (metric_confidence IN ('observed', 'deterministic', 'estimated', 'llm_inferred', 'unavailable')),
    CONSTRAINT ck_token_hotspot_prompt_cache_evidence_state CHECK (prompt_cache_evidence_state IN ('known_reason', 'inferred_candidate', 'unknown', 'unavailable', 'not_applicable')),
    CONSTRAINT ck_token_hotspot_metric_quality CHECK (
        metric_status NOT IN ('unavailable', 'not_applicable')
        OR metric_confidence = 'unavailable'
    ),
    CONSTRAINT ck_token_hotspot_prompt_cache_scope CHECK (
        (hotspot_type = 'prompt_cache_breakage' AND prompt_cache_evidence_state <> 'not_applicable')
        OR (hotspot_type <> 'prompt_cache_breakage' AND prompt_cache_evidence_state = 'not_applicable')
    ),
    CONSTRAINT ck_token_hotspot_llm_candidate_boundary CHECK (
        (finding_state = 'candidate_llm_inferred' AND attribution_type = 'llm_inferred')
        OR (finding_state <> 'candidate_llm_inferred' AND attribution_type <> 'llm_inferred')
    ),
    CONSTRAINT ck_token_hotspot_confirmed_authority CHECK (
        finding_state <> 'confirmed'
        OR attribution_type <> 'llm_inferred'
    ),
    CONSTRAINT ck_token_hotspot_confirmed_metric_authority CHECK (
        finding_state <> 'confirmed'
        OR metric_confidence NOT IN ('llm_inferred', 'unavailable')
    ),
    CONSTRAINT ck_token_hotspot_evidence_summary CHECK (
        length(evidence_summary) BETWEEN 1 AND 512
    ),
    CONSTRAINT ck_token_hotspot_evidence_refs_json CHECK (
        jsonb_typeof(evidence_refs_json) = 'array'
        AND jsonb_array_length(evidence_refs_json) BETWEEN 1 AND 32
    ),
    CONSTRAINT ck_token_hotspot_detection_key CHECK (
        detection_key IS NULL
        OR (
            length(detection_key) BETWEEN 1 AND 128
            AND detection_key ~ '^[A-Za-z0-9_:/.-]+$'
        )
    ),
    CONSTRAINT ck_token_hotspot_token_burn_score CHECK (
        token_burn_score IS NULL
        OR token_burn_score BETWEEN 0 AND 1
    ),
    CONSTRAINT ck_token_hotspot_estimated_cost_impact CHECK (
        estimated_cost_impact IS NULL
        OR estimated_cost_impact >= 0
    )
);

CREATE TABLE IF NOT EXISTS recommendation_prompt_template (
    customer_organization_id uuid NOT NULL,
    prompt_template_version text NOT NULL,
    purpose text NOT NULL,
    state text NOT NULL,
    prompt_template_hash text NOT NULL,
    structured_output_schema_version text NOT NULL,
    policy_constraints_json jsonb NOT NULL,
    audit_event_id text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    activated_at_utc timestamptz NULL,
    PRIMARY KEY (customer_organization_id, prompt_template_version),
    CONSTRAINT fk_recommendation_prompt_template_customer FOREIGN KEY (customer_organization_id) REFERENCES customer_organization (customer_organization_id),
    CONSTRAINT fk_recommendation_prompt_template_audit_event FOREIGN KEY (customer_organization_id, audit_event_id) REFERENCES governance_audit_event (customer_organization_id, audit_event_id),
    CONSTRAINT ck_recommendation_prompt_template_purpose CHECK (purpose IN ('recommendation_drafter', 'candidate_hotspot_generator', 'cache_breakage_reasoner')),
    CONSTRAINT ck_recommendation_prompt_template_state CHECK (state IN ('draft', 'active', 'superseded', 'disabled')),
    CONSTRAINT ck_recommendation_prompt_template_hash CHECK (prompt_template_hash ~ '^[a-f0-9]{64}$'),
    CONSTRAINT ck_recommendation_prompt_template_constraints CHECK (jsonb_typeof(policy_constraints_json) = 'object'),
    CONSTRAINT ck_recommendation_prompt_template_version CHECK (length(prompt_template_version) BETWEEN 1 AND 128 AND prompt_template_version ~ '^[A-Za-z0-9_:/.-]+$'),
    CONSTRAINT ck_recommendation_prompt_template_schema CHECK (length(structured_output_schema_version) BETWEEN 1 AND 128 AND structured_output_schema_version ~ '^[A-Za-z0-9_:/.-]+$'),
    CONSTRAINT ck_recommendation_prompt_template_activation CHECK (
        (state = 'active' AND activated_at_utc IS NOT NULL)
        OR (state <> 'active')
    )
);

CREATE TABLE IF NOT EXISTS recommendation_model_policy (
    customer_organization_id uuid NOT NULL,
    policy_version_id text NOT NULL,
    state text NOT NULL,
    provider text NOT NULL,
    primary_deployment_alias text NOT NULL,
    fallback_deployment_alias text NULL,
    region text NOT NULL,
    model_family_or_sku text NOT NULL,
    model_version text NULL,
    prompt_template_version text NOT NULL,
    structured_output_schema_version text NOT NULL,
    allowed_evidence_classes_json jsonb NOT NULL,
    fallback_behavior text NOT NULL,
    llm_assisted_enabled boolean NOT NULL,
    audit_event_id text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    activated_at_utc timestamptz NULL,
    PRIMARY KEY (customer_organization_id, policy_version_id),
    CONSTRAINT fk_recommendation_model_policy_customer FOREIGN KEY (customer_organization_id) REFERENCES customer_organization (customer_organization_id),
    CONSTRAINT fk_recommendation_model_policy_prompt_template FOREIGN KEY (customer_organization_id, prompt_template_version) REFERENCES recommendation_prompt_template (customer_organization_id, prompt_template_version),
    CONSTRAINT fk_recommendation_model_policy_audit_event FOREIGN KEY (customer_organization_id, audit_event_id) REFERENCES governance_audit_event (customer_organization_id, audit_event_id),
    CONSTRAINT ck_recommendation_model_policy_state CHECK (state IN ('draft', 'active', 'superseded', 'disabled')),
    CONSTRAINT ck_recommendation_model_policy_provider CHECK (provider IN ('azure_openai')),
    CONSTRAINT ck_recommendation_model_policy_alias CHECK (
        primary_deployment_alias = 'recommendation-writer-primary'
        AND (fallback_deployment_alias IS NULL OR fallback_deployment_alias = 'recommendation-writer-fallback')
    ),
    CONSTRAINT ck_recommendation_model_policy_fallback CHECK (fallback_behavior IN ('deterministic_only', 'fallback_deployment_then_deterministic')),
    CONSTRAINT ck_recommendation_model_policy_fallback_alias CHECK (
        fallback_behavior <> 'fallback_deployment_then_deterministic'
        OR fallback_deployment_alias IS NOT NULL
    ),
    CONSTRAINT ck_recommendation_model_policy_evidence_classes CHECK (
        jsonb_typeof(allowed_evidence_classes_json) = 'array'
        AND jsonb_array_length(allowed_evidence_classes_json) BETWEEN 1 AND 32
    ),
    CONSTRAINT ck_recommendation_model_policy_safe_ids CHECK (
        length(policy_version_id) BETWEEN 1 AND 128
        AND policy_version_id ~ '^[A-Za-z0-9_:/.-]+$'
        AND length(region) BETWEEN 1 AND 128
        AND region ~ '^[a-z0-9-]+$'
        AND length(model_family_or_sku) BETWEEN 1 AND 128
        AND model_family_or_sku ~ '^[A-Za-z0-9_:/.-]+$'
        AND length(structured_output_schema_version) BETWEEN 1 AND 128
        AND structured_output_schema_version ~ '^[A-Za-z0-9_:/.-]+$'
        AND (model_version IS NULL OR (length(model_version) BETWEEN 1 AND 128 AND model_version ~ '^[A-Za-z0-9_:/.-]+$'))
    ),
    CONSTRAINT ck_recommendation_model_policy_activation CHECK (
        (state = 'active' AND activated_at_utc IS NOT NULL)
        OR (state <> 'active')
    )
);

CREATE TABLE IF NOT EXISTS recommendation (
    recommendation_id uuid PRIMARY KEY,
    customer_organization_id uuid NOT NULL,
    agent_session_id text NOT NULL,
    token_hotspot_id uuid NULL,
    rule_id text NULL,
    recommendation_kind text NOT NULL,
    recommendation_state text NOT NULL,
    authority_state text NOT NULL,
    confidence text NOT NULL,
    validation_state text NOT NULL,
    visibility_scope text NOT NULL,
    evidence_packet_version text NOT NULL,
    evidence_packet_json jsonb NOT NULL,
    evidence_packet_hash text NOT NULL,
    summary text NOT NULL,
    rationale text NOT NULL,
    recommended_action text NOT NULL,
    expected_benefit text NOT NULL,
    model_policy_version_id text NULL,
    prompt_template_version text NULL,
    evidence_refs_json jsonb NOT NULL,
    policy_metadata_json jsonb NOT NULL,
    audit_event_id text NOT NULL,
    generation_key text NULL,
    created_at_utc timestamptz NOT NULL,
    CONSTRAINT fk_recommendation_customer_organization FOREIGN KEY (customer_organization_id) REFERENCES customer_organization (customer_organization_id),
    CONSTRAINT fk_recommendation_agent_session FOREIGN KEY (customer_organization_id, agent_session_id) REFERENCES agent_session (customer_organization_id, agent_session_id),
    CONSTRAINT fk_recommendation_token_hotspot FOREIGN KEY (customer_organization_id, token_hotspot_id) REFERENCES token_hotspot (customer_organization_id, token_hotspot_id),
    CONSTRAINT fk_recommendation_audit_event FOREIGN KEY (customer_organization_id, audit_event_id) REFERENCES governance_audit_event (customer_organization_id, audit_event_id),
    CONSTRAINT uq_recommendation_customer_recommendation UNIQUE (customer_organization_id, recommendation_id),
    CONSTRAINT uq_recommendation_generation_key UNIQUE (customer_organization_id, generation_key),
    CONSTRAINT ck_recommendation_kind CHECK (recommendation_kind IN ('deterministic', 'llm_assisted', 'mixed')),
    CONSTRAINT ck_recommendation_state CHECK (recommendation_state IN ('candidate', 'accepted', 'rejected', 'expired', 'superseded')),
    CONSTRAINT ck_recommendation_authority_state CHECK (authority_state IN ('deterministic', 'llm_assisted', 'llm_inferred_candidate', 'rejected')),
    CONSTRAINT ck_recommendation_confidence CHECK (confidence IN ('low', 'medium', 'high')),
    CONSTRAINT ck_recommendation_validation_state CHECK (validation_state IN ('pending', 'validated', 'rejected')),
    CONSTRAINT ck_recommendation_visibility_scope CHECK (visibility_scope IN ('self', 'team_scoped', 'security_review', 'admin', 'aggregate_only')),
    CONSTRAINT ck_recommendation_evidence_packet_json CHECK (
        jsonb_typeof(evidence_packet_json) = 'object'
        AND evidence_packet_json ? 'policy'
    ),
    CONSTRAINT ck_recommendation_evidence_packet_hash CHECK (evidence_packet_hash ~ '^[A-F0-9]{64}$'),
    CONSTRAINT ck_recommendation_evidence_refs_json CHECK (
        jsonb_typeof(evidence_refs_json) = 'array'
        AND jsonb_array_length(evidence_refs_json) BETWEEN 1 AND 64
    ),
    CONSTRAINT ck_recommendation_policy_metadata_json CHECK (
        jsonb_typeof(policy_metadata_json) = 'object'
        AND policy_metadata_json ? 'content_capture_policy_version'
        AND policy_metadata_json ? 'recommendation_model_policy_version'
    ),
    CONSTRAINT ck_recommendation_text_lengths CHECK (
        length(summary) BETWEEN 1 AND 512
        AND length(rationale) BETWEEN 1 AND 1024
        AND length(recommended_action) BETWEEN 1 AND 1024
        AND length(expected_benefit) BETWEEN 1 AND 1024
    ),
    CONSTRAINT ck_recommendation_candidate_authority CHECK (
        authority_state <> 'llm_inferred_candidate'
        OR recommendation_state = 'candidate'
    )
);

CREATE TABLE IF NOT EXISTS recommendation_evidence (
    recommendation_evidence_id uuid PRIMARY KEY,
    customer_organization_id uuid NOT NULL,
    recommendation_id uuid NOT NULL,
    evidence_kind text NOT NULL,
    evidence_id text NOT NULL,
    evidence_state text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    CONSTRAINT fk_recommendation_evidence_customer_organization FOREIGN KEY (customer_organization_id) REFERENCES customer_organization (customer_organization_id),
    CONSTRAINT fk_recommendation_evidence_recommendation FOREIGN KEY (customer_organization_id, recommendation_id) REFERENCES recommendation (customer_organization_id, recommendation_id),
    CONSTRAINT ck_recommendation_evidence_kind CHECK (evidence_kind IN ('telemetry_envelope', 'token_observation', 'token_hotspot', 'content_reference', 'repository_evidence', 'audit_event', 'pricing_basis')),
    CONSTRAINT ck_recommendation_evidence_state CHECK (evidence_state IN ('observed', 'derived', 'correlated', 'llm_inferred', 'unavailable')),
    CONSTRAINT ck_recommendation_evidence_id CHECK (length(evidence_id) BETWEEN 1 AND 256)
);

CREATE TABLE IF NOT EXISTS recommendation_regeneration_request (
    recommendation_regeneration_request_id uuid PRIMARY KEY,
    customer_organization_id uuid NOT NULL,
    agent_session_id text NULL,
    token_hotspot_id uuid NULL,
    reason text NOT NULL,
    state text NOT NULL,
    audit_event_id text NOT NULL,
    correlation_id text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    CONSTRAINT fk_recommendation_regeneration_customer_organization FOREIGN KEY (customer_organization_id) REFERENCES customer_organization (customer_organization_id),
    CONSTRAINT fk_recommendation_regeneration_agent_session FOREIGN KEY (customer_organization_id, agent_session_id) REFERENCES agent_session (customer_organization_id, agent_session_id),
    CONSTRAINT fk_recommendation_regeneration_token_hotspot FOREIGN KEY (customer_organization_id, token_hotspot_id) REFERENCES token_hotspot (customer_organization_id, token_hotspot_id),
    CONSTRAINT fk_recommendation_regeneration_audit_event FOREIGN KEY (customer_organization_id, audit_event_id) REFERENCES governance_audit_event (customer_organization_id, audit_event_id),
    CONSTRAINT ck_recommendation_regeneration_state CHECK (state IN ('queued', 'completed', 'failed')),
    CONSTRAINT ck_recommendation_regeneration_target CHECK (agent_session_id IS NOT NULL OR token_hotspot_id IS NOT NULL),
    CONSTRAINT ck_recommendation_regeneration_reason CHECK (length(reason) BETWEEN 1 AND 512)
);

CREATE TABLE IF NOT EXISTS recommendation_llm_generation_failure (
    recommendation_llm_generation_failure_id uuid PRIMARY KEY,
    customer_organization_id uuid NOT NULL,
    agent_session_id text NOT NULL,
    token_hotspot_id uuid NULL,
    failure_code text NOT NULL,
    provider text NOT NULL,
    deployment_alias text NOT NULL,
    policy_version_id text NOT NULL,
    prompt_template_version text NOT NULL,
    evidence_packet_hash text NOT NULL,
    structured_output_schema_version text NOT NULL,
    audit_event_id text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    CONSTRAINT fk_recommendation_llm_failure_customer FOREIGN KEY (customer_organization_id) REFERENCES customer_organization (customer_organization_id),
    CONSTRAINT fk_recommendation_llm_failure_agent_session FOREIGN KEY (customer_organization_id, agent_session_id) REFERENCES agent_session (customer_organization_id, agent_session_id),
    CONSTRAINT fk_recommendation_llm_failure_token_hotspot FOREIGN KEY (customer_organization_id, token_hotspot_id) REFERENCES token_hotspot (customer_organization_id, token_hotspot_id),
    CONSTRAINT fk_recommendation_llm_failure_model_policy FOREIGN KEY (customer_organization_id, policy_version_id) REFERENCES recommendation_model_policy (customer_organization_id, policy_version_id),
    CONSTRAINT fk_recommendation_llm_failure_prompt_template FOREIGN KEY (customer_organization_id, prompt_template_version) REFERENCES recommendation_prompt_template (customer_organization_id, prompt_template_version),
    CONSTRAINT fk_recommendation_llm_failure_audit_event FOREIGN KEY (customer_organization_id, audit_event_id) REFERENCES governance_audit_event (customer_organization_id, audit_event_id),
    CONSTRAINT ck_recommendation_llm_failure_provider CHECK (provider IN ('azure_openai')),
    CONSTRAINT ck_recommendation_llm_failure_alias CHECK (deployment_alias IN ('recommendation-writer-primary', 'recommendation-writer-fallback')),
    CONSTRAINT ck_recommendation_llm_failure_hash CHECK (evidence_packet_hash ~ '^[A-F0-9]{64}$'),
    CONSTRAINT ck_recommendation_llm_failure_safe_ids CHECK (
        length(failure_code) BETWEEN 1 AND 128
        AND failure_code ~ '^[A-Za-z0-9_:/.-]+$'
        AND length(structured_output_schema_version) BETWEEN 1 AND 128
        AND structured_output_schema_version ~ '^[A-Za-z0-9_:/.-]+$'
    )
);

CREATE INDEX IF NOT EXISTS ix_recommendation_prompt_template_state ON recommendation_prompt_template (customer_organization_id, state, created_at_utc);
CREATE INDEX IF NOT EXISTS ix_recommendation_model_policy_state ON recommendation_model_policy (customer_organization_id, state, created_at_utc);
CREATE UNIQUE INDEX IF NOT EXISTS ux_recommendation_model_policy_active ON recommendation_model_policy (customer_organization_id) WHERE state = 'active';
CREATE INDEX IF NOT EXISTS ix_recommendation_session_state ON recommendation (customer_organization_id, agent_session_id, recommendation_state, created_at_utc);
CREATE INDEX IF NOT EXISTS ix_recommendation_customer_state ON recommendation (customer_organization_id, recommendation_state);
CREATE INDEX IF NOT EXISTS ix_recommendation_evidence_recommendation ON recommendation_evidence (customer_organization_id, recommendation_id);
CREATE INDEX IF NOT EXISTS ix_recommendation_regeneration_customer_state ON recommendation_regeneration_request (customer_organization_id, state, created_at_utc);
CREATE INDEX IF NOT EXISTS ix_recommendation_regeneration_customer_session ON recommendation_regeneration_request (customer_organization_id, agent_session_id, created_at_utc);
CREATE INDEX IF NOT EXISTS ix_recommendation_llm_failure_customer_session ON recommendation_llm_generation_failure (customer_organization_id, agent_session_id, created_at_utc);

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

CREATE INDEX IF NOT EXISTS ix_pricing_basis_customer_review_model
    ON pricing_basis (customer_organization_id, review_state, provider_name, model_name, token_type, billing_route, effective_from_utc DESC);

CREATE INDEX IF NOT EXISTS ix_cost_estimate_customer_mix
    ON cost_estimate (customer_organization_id, provider_name, model_name, token_type, billing_route, cost_status);

CREATE INDEX IF NOT EXISTS ix_product_api_idempotency_expiry
    ON product_api_idempotency (expires_at_utc);

CREATE INDEX IF NOT EXISTS ix_token_hotspot_session_state
    ON token_hotspot (customer_organization_id, agent_session_id, finding_state, created_at_utc);

CREATE UNIQUE INDEX IF NOT EXISTS ux_token_hotspot_customer_detection_key
    ON token_hotspot (customer_organization_id, detection_key)
    WHERE detection_key IS NOT NULL;

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
