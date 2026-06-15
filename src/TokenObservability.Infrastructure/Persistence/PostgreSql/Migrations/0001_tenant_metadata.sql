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
