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
    CONSTRAINT ck_customer_organization_slug CHECK (slug = lower(slug) AND slug ~ '^[a-z0-9][a-z0-9-]*[a-z0-9]$'),
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
    CONSTRAINT ck_product_user_status CHECK (status IN ('active', 'disabled', 'deleted')),
    CONSTRAINT ck_product_user_seen_timestamps CHECK (last_seen_at_utc IS NULL OR last_seen_at_utc >= first_seen_at_utc),
    CONSTRAINT ck_product_user_timestamps CHECK (updated_at_utc >= created_at_utc)
);

CREATE INDEX IF NOT EXISTS ix_customer_organization_status
    ON customer_organization (status);

CREATE INDEX IF NOT EXISTS ix_identity_tenant_customer_status
    ON identity_tenant (customer_organization_id, status);

CREATE INDEX IF NOT EXISTS ix_product_user_customer_status
    ON product_user (customer_organization_id, status);
