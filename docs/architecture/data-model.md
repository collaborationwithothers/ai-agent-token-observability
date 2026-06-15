# Production Data Model

## Purpose

This document defines the logical production data model for the Azure Production MVP and the Multi-Tenant SaaS Target State.

It supersedes the previous local-first direct-file-import data model. It is not a database migration file. Implementation may refine table and column names, but the entity boundaries, tenant scoping, evidence states, content separation, and audit requirements must be preserved.

## Source Documents

- [Production Target State Spec](../specs/production-target-state.md)
- [Azure Production MVP PRD](../prd/azure-production-mvp.md)
- [Azure Production Architecture](./azure-production-architecture.md)
- [Codex Production Ingestion Contract](./codex-production-ingestion-contract.md)
- [Identity And Authorization Architecture](./identity-and-authorization.md)
- [Content Capture And Redaction Architecture](./content-capture-and-redaction.md)
- [Recommendation Engine Architecture](./recommendation-engine.md)

## Core Model Rules

- PostgreSQL is the Product Metadata Store.
- Blob Storage stores only policy-approved redacted captured content.
- Raw OTLP payloads are not the product system of record.
- Raw content must not be stored in PostgreSQL.
- Every tenant-owned record must include `customer_organization_id`.
- The Azure Production MVP may have one Customer Organization, but tables must remain tenant-aware.
- Authorization decisions use Product Role Mapping, not raw Entra group names.
- Scoped Ingestion Credential identity is authoritative for telemetry upload and session ownership.
- Harness-emitted identity is evidence, not access-control authority.
- Token metrics distinguish observed, derived, estimated, unavailable, not applicable, and mixed.
- Unavailable token counts are null, not zero.
- LLM-inferred candidate findings are clearly labelled and cannot become confirmed findings without product validation.
- Governance Audit Events are first-class records, not optional logs.

## Data Classes

| Data class | Primary store | Notes |
| --- | --- | --- |
| Tenant and identity metadata | PostgreSQL | Customer Organization, identity tenants, users, role mappings |
| Harness configuration | PostgreSQL | Setup profiles, credential metadata, policy versions |
| Normalized telemetry metadata | PostgreSQL | Envelopes, sessions, turns, invocations, tool activity |
| Aggregate metrics | Azure Monitor workspace or managed Prometheus | Primary data source for Managed Grafana aggregate dashboards; metric contract is defined in [aggregate-metrics-contract.md](./aggregate-metrics-contract.md) |
| Traces, logs, and events | Application Insights or Log Analytics | Redacted or metadata-only diagnostic records |
| Captured content | Blob Storage | Redacted Captured Content Blob only |
| Content references | PostgreSQL | Blob pointer, hash, redaction state, policy, retention, audit |
| Recommendations and hotspots | PostgreSQL | Evidence-backed records and LLM-inferred candidates |
| Pricing and budgets | PostgreSQL | Versioned pricing basis and non-punitive alert policy |
| Audit | PostgreSQL, optionally exported to Log Analytics | Governance Audit Events |

## Tenancy Model

### `customer_organization`

Represents a product tenant.

| Field | Required | Purpose |
| --- | --- | --- |
| `customer_organization_id` | Yes | Product tenant identifier |
| `slug` | Yes | Stable product slug, such as `internal` for MVP |
| `display_name` | Yes | Customer-facing name |
| `data_residency_region` | Yes | Primary region for customer data |
| `isolation_tier` | Yes | `shared`, `dedicated_data`, or `dedicated_cell` |
| `status` | Yes | `active`, `suspended`, `offboarding`, `deleted` |
| `created_at_utc` | Yes | Creation timestamp |
| `updated_at_utc` | Yes | Last update timestamp |

MVP: one row is sufficient, but application code must still resolve tenant scope from credential, auth, or setup profile.

### `customer_policy_version`

Stores versioned product policies that affect ingestion, content, recommendations, retention, pricing, and dashboard visibility.

| Field | Required | Purpose |
| --- | --- | --- |
| `policy_version_id` | Yes | Policy version identifier |
| `customer_organization_id` | Yes | Tenant owner |
| `policy_kind` | Yes | `content_capture`, `retention`, `recommendation_model`, `pricing`, `visibility`, `budget` |
| `version_label` | Yes | Human or date-based version |
| `policy_json` | Yes | Structured policy body |
| `status` | Yes | `draft`, `active`, `superseded`, `disabled` |
| `effective_from_utc` | Yes | Start time |
| `effective_to_utc` | No | End time |
| `created_by_product_user_id` | Yes | Actor |
| `audit_event_id` | Yes | Governance audit reference |

Policy versions are referenced by ingestion envelopes, content references, recommendations, and audit records so later investigations can explain which policy was active.

## Identity And Authorization

### `identity_tenant`

Represents an external identity authority connected to a Customer Organization.

| Field | Required | Purpose |
| --- | --- | --- |
| `identity_tenant_id` | Yes | Internal identity tenant identifier |
| `customer_organization_id` | Yes | Tenant owner |
| `provider` | Yes | `microsoft_entra` for MVP |
| `issuer` | Yes | Token issuer |
| `external_tenant_id` | Yes | Entra tenant ID or provider tenant ID |
| `allowed_audiences_json` | Yes | Accepted app audiences |
| `jwks_uri` | No | Token signing key metadata source |
| `display_name` | Yes | Admin display label |
| `status` | Yes | `active`, `disabled`, `pending_validation` |
| `last_validated_at_utc` | No | Last successful validation |

### `product_user`

Represents a person known to the product.

| Field | Required | Purpose |
| --- | --- | --- |
| `product_user_id` | Yes | Product user identifier |
| `customer_organization_id` | Yes | Tenant owner |
| `identity_tenant_id` | Yes | Source identity tenant |
| `external_subject_id` | Yes | Stable subject claim or provider user ID |
| `display_label` | Yes | User-visible label |
| `email` | No | Email or UPN when available |
| `status` | Yes | `active`, `disabled`, `deleted` |
| `first_seen_at_utc` | Yes | First authentication or ingestion association |
| `last_seen_at_utc` | No | Last authentication or ingestion association |

### `product_role_mapping`

Maps external identity evidence to product roles and scopes.

| Field | Required | Purpose |
| --- | --- | --- |
| `product_role_mapping_id` | Yes | Mapping identifier |
| `customer_organization_id` | Yes | Tenant owner |
| `identity_tenant_id` | Yes | Source identity tenant |
| `external_principal_type` | Yes | `app_role`, `group_object_id`, `user_subject`, `service_principal` |
| `external_principal_id` | Yes | Provider identifier, never raw group display name |
| `product_role` | Yes | `PlatformAdmin`, `SecurityReviewer`, `EngineeringLead`, `Developer`, `ReadOnlyViewer` |
| `scope_kind` | Yes | `organization`, `team`, `repository`, `harness_profile`, `self`, `content_review_queue`, `pricing`, `tenant_admin` |
| `scope_id` | No | Scoped resource ID |
| `status` | Yes | `active`, `disabled`, `expired` |
| `effective_from_utc` | Yes | Start time |
| `effective_to_utc` | No | End time |
| `audit_event_id` | Yes | Governance audit reference |

## Organization Structure

### `team`

Represents a customer-defined team or group used for scoping dashboards and alerts.

| Field | Required | Purpose |
| --- | --- | --- |
| `team_id` | Yes | Team identifier |
| `customer_organization_id` | Yes | Tenant owner |
| `display_name` | Yes | Team name |
| `source_kind` | Yes | `manual`, `identity_group`, `source_provider`, `imported` |
| `external_source_id` | No | External reference |
| `status` | Yes | `active`, `archived` |

### `team_membership`

Maps product users to teams.

| Field | Required | Purpose |
| --- | --- | --- |
| `team_membership_id` | Yes | Membership identifier |
| `customer_organization_id` | Yes | Tenant owner |
| `team_id` | Yes | Team |
| `product_user_id` | Yes | Product user |
| `source_kind` | Yes | `manual`, `role_mapping`, `identity_group`, `source_provider` |
| `status` | Yes | `active`, `removed` |

## Repository Model

### `source_provider_connection`

Represents a connected source control provider.

| Field | Required | Purpose |
| --- | --- | --- |
| `source_provider_connection_id` | Yes | Connection identifier |
| `customer_organization_id` | Yes | Tenant owner |
| `provider` | Yes | `github`, `azure_devops`, `gitlab`, or future provider |
| `display_name` | Yes | Admin label |
| `connection_status` | Yes | `active`, `needs_attention`, `disabled` |
| `metadata_json` | No | Non-secret provider metadata |
| `created_by_product_user_id` | Yes | Actor |
| `audit_event_id` | Yes | Governance audit reference |

### `repository`

Represents an enrolled or candidate repository.

| Field | Required | Purpose |
| --- | --- | --- |
| `repository_id` | Yes | Repository identifier |
| `customer_organization_id` | Yes | Tenant owner |
| `source_provider_connection_id` | No | Provider connection |
| `external_repository_id` | No | Provider repository ID |
| `full_name` | No | Provider full name when known |
| `display_name` | Yes | Dashboard display name |
| `default_branch` | No | Default branch when known |
| `enrollment_status` | Yes | `candidate`, `enrolled`, `excluded`, `archived` |
| `content_scanning_status` | Yes | `disabled`, `enabled`, `suspended` |
| `created_at_utc` | Yes | Creation timestamp |

Repository records can be created from provider discovery, self-service enrollment, or unmatched telemetry candidates.

### `repository_evidence`

Stores repository evidence attached to telemetry or scanner findings.

| Field | Required | Purpose |
| --- | --- | --- |
| `repository_evidence_id` | Yes | Evidence identifier |
| `customer_organization_id` | Yes | Tenant owner |
| `repository_id` | No | Matched repository |
| `source_kind` | Yes | `harness_emitted`, `setup_profile_scope`, `provider_metadata`, `scanner`, `manual_review` |
| `evidence_value_hash` | No | Hash of sensitive local path or opaque identifier |
| `display_value` | No | Policy-approved display value |
| `evidence_state` | Yes | `observed`, `correlated`, `inferred`, `unavailable` |
| `telemetry_envelope_id` | No | Source telemetry reference |
| `created_at_utc` | Yes | Creation timestamp |

Scanner findings are Correlatable Repository Evidence. They are not harness-emitted session facts.

## Harness Configuration

### `harness_setup_profile`

Represents a manually configured harness telemetry profile.

| Field | Required | Purpose |
| --- | --- | --- |
| `harness_setup_profile_id` | Yes | Setup profile identifier |
| `customer_organization_id` | Yes | Tenant owner |
| `harness` | Yes | `codex_cli` for MVP |
| `display_name` | Yes | Admin label |
| `environment` | Yes | `dv`, `qa`, `pp`, `pd`, or product environment label |
| `region` | Yes | Product data residency region |
| `schema_version` | Yes | Production Ingestion Contract version |
| `content_capture_policy_version_id` | Yes | Active policy reference |
| `status` | Yes | `active`, `disabled`, `retired` |
| `created_by_product_user_id` | Yes | Actor |
| `audit_event_id` | Yes | Governance audit reference |

### `scoped_ingestion_credential`

Stores credential metadata. Secret material is not stored in plaintext.

| Field | Required | Purpose |
| --- | --- | --- |
| `scoped_ingestion_credential_id` | Yes | Credential identifier |
| `customer_organization_id` | Yes | Tenant owner |
| `harness_setup_profile_id` | Yes | Setup profile |
| `product_user_id` | Yes | Developer identity |
| `credential_hash` | Yes | Hash or verifier for presented credential |
| `credential_prefix` | No | Non-secret lookup prefix |
| `allowed_harness` | Yes | `codex_cli` for MVP |
| `allowed_repository_id` | No | Optional repository scope |
| `allowed_team_id` | No | Optional team scope |
| `status` | Yes | `active`, `expired`, `revoked`, `rotated` |
| `expires_at_utc` | Yes | Expiry |
| `last_used_at_utc` | No | Last accepted request |
| `audit_event_id` | Yes | Creation or latest lifecycle audit |

## Ingestion And Telemetry

### `telemetry_envelope`

Product-normalized wrapper around accepted harness telemetry.

| Field | Required | Purpose |
| --- | --- | --- |
| `telemetry_envelope_id` | Yes | Envelope identifier |
| `customer_organization_id` | Yes | Tenant owner |
| `harness_setup_profile_id` | Yes | Setup profile |
| `scoped_ingestion_credential_id` | Yes | Upload credential |
| `product_user_id` | Yes | Credential-derived developer identity |
| `harness` | Yes | `codex_cli` for MVP |
| `schema_version` | Yes | Product ingestion schema |
| `signal_type` | Yes | `log`, `trace`, `metric` |
| `source_event_name` | No | Harness event name |
| `source_event_timestamp_utc` | No | Event timestamp |
| `received_at_utc` | Yes | Product receive timestamp |
| `conversation_id_hash` | No | Opaque session correlation |
| `turn_id_hash` | No | Opaque turn correlation |
| `trace_id_hash` | No | Trace correlation |
| `span_id_hash` | No | Span correlation |
| `model_name` | No | Model when emitted |
| `harness_version` | No | Codex CLI version when emitted |
| `content_policy_decision` | Yes | `metadata_only`, `capture_candidate`, `blocked`, `redaction_required` |
| `routing_decision_json` | Yes | Signal Routing Policy result |
| `evidence_state` | Yes | `observed`, `derived`, `estimated`, `unavailable`, `not_applicable`, `mixed` |
| `dedupe_key_hash` | Yes | Idempotency key |

This table stores metadata only. It must not store raw prompts, raw tool outputs, raw command output, or raw file content.

### `ingestion_rejection`

Records rejected requests without storing content-bearing payloads.

| Field | Required | Purpose |
| --- | --- | --- |
| `ingestion_rejection_id` | Yes | Rejection identifier |
| `customer_organization_id` | No | Tenant when derivable |
| `harness_setup_profile_id` | No | Setup profile when derivable |
| `scoped_ingestion_credential_id` | No | Credential when derivable |
| `reason_code` | Yes | `invalid_credential`, `out_of_scope`, `unsupported_schema`, `payload_too_large`, `rate_limited`, `malformed_otlp`, `region_mismatch` |
| `http_status` | Yes | Returned status |
| `received_at_utc` | Yes | Rejection timestamp |
| `diagnostic_hash` | No | Hash of safe diagnostic metadata |
| `audit_event_id` | No | Audit event when security-relevant |

## Session Model

### `agent_session`

Represents one harness session.

| Field | Required | Purpose |
| --- | --- | --- |
| `agent_session_id` | Yes | Session identifier |
| `customer_organization_id` | Yes | Tenant owner |
| `product_user_id` | Yes | Credential-derived developer identity |
| `harness_setup_profile_id` | Yes | Setup profile |
| `harness` | Yes | `codex_cli` for MVP |
| `provider_session_id_hash` | No | Opaque Codex conversation or session ID |
| `started_at_utc` | No | First observed event |
| `ended_at_utc` | No | Completion or last observed event |
| `session_status` | Yes | `active`, `completed`, `failed`, `partial`, `expired` |
| `environment` | No | Harness environment label |
| `sandbox_setting` | No | Emitted sandbox setting |
| `approval_setting` | No | Emitted approval policy |
| `repository_evidence_state` | Yes | `observed`, `correlated`, `inferred`, `unavailable`, `mixed` |
| `content_capture_summary` | Yes | `none`, `metadata_only`, `captured`, `review_required`, `redaction_failed`, `mixed` |
| `recommendation_status` | Yes | `not_started`, `queued`, `generated`, `failed`, `disabled` |
| `created_at_utc` | Yes | Creation timestamp |
| `updated_at_utc` | Yes | Last update timestamp |

### `agent_turn`

Represents an observed or derived turn within a session.

| Field | Required | Purpose |
| --- | --- | --- |
| `agent_turn_id` | Yes | Turn identifier |
| `customer_organization_id` | Yes | Tenant owner |
| `agent_session_id` | Yes | Parent session |
| `turn_index` | No | Derived or observed order |
| `started_at_utc` | No | Turn start |
| `ended_at_utc` | No | Turn end |
| `status` | Yes | `observed`, `derived`, `partial`, `failed` |
| `source_telemetry_envelope_id` | No | Source envelope |

### `model_invocation`

Represents a model request or response event.

| Field | Required | Purpose |
| --- | --- | --- |
| `model_invocation_id` | Yes | Invocation identifier |
| `customer_organization_id` | Yes | Tenant owner |
| `agent_session_id` | Yes | Parent session |
| `agent_turn_id` | No | Parent turn |
| `source_telemetry_envelope_id` | Yes | Source envelope |
| `provider_name` | No | `openai`, `anthropic`, or provider when known |
| `model_name` | No | Model name when emitted |
| `operation_name` | No | Operation when emitted |
| `duration_ms` | No | Observed duration |
| `status` | Yes | `succeeded`, `failed`, `partial`, `unknown` |
| `error_code` | No | Error when emitted |
| `cache_evidence_state` | Yes | `observed`, `correlated`, `llm_inferred`, `unavailable`, `not_applicable` |

### `tool_activity`

Represents tool decisions and tool results.

| Field | Required | Purpose |
| --- | --- | --- |
| `tool_activity_id` | Yes | Tool activity identifier |
| `customer_organization_id` | Yes | Tenant owner |
| `agent_session_id` | Yes | Parent session |
| `agent_turn_id` | No | Parent turn |
| `source_telemetry_envelope_id` | Yes | Source envelope |
| `tool_name` | No | Tool name when emitted |
| `decision` | No | `approved`, `denied`, `auto_allowed`, `config_allowed`, `unknown` |
| `duration_ms` | No | Observed duration |
| `success` | No | Observed success |
| `content_reference_id` | No | Redacted content evidence when allowed |

## Token And Cost

### `token_observation`

Stores token values without confusing missing values with zero.

| Field | Required | Purpose |
| --- | --- | --- |
| `token_observation_id` | Yes | Token observation identifier |
| `customer_organization_id` | Yes | Tenant owner |
| `agent_session_id` | Yes | Session |
| `model_invocation_id` | No | Invocation |
| `metric_name` | Yes | `input_tokens`, `output_tokens`, `cached_input_tokens`, `reasoning_output_tokens`, `total_tokens` |
| `value` | No | Token count, null when unavailable |
| `metric_status` | Yes | `observed`, `derived`, `estimated`, `unavailable`, `not_applicable`, `mixed` |
| `metric_confidence` | Yes | `observed`, `deterministic`, `estimated`, `llm_inferred`, `unavailable` |
| `source_kind` | Yes | `codex_event`, `otel_metric`, `derived_summary`, `estimator`, `missing` |
| `source_telemetry_envelope_id` | No | Source envelope |

### `cost_estimate`

Stores estimated cost derived from token observations and pricing basis.

| Field | Required | Purpose |
| --- | --- | --- |
| `cost_estimate_id` | Yes | Cost estimate identifier |
| `customer_organization_id` | Yes | Tenant owner |
| `agent_session_id` | Yes | Session |
| `model_invocation_id` | No | Invocation |
| `pricing_basis_id` | Yes | Pricing basis used |
| `pricing_version` | Yes | Pricing version |
| `currency` | Yes | Currency |
| `estimated_cost` | No | Null when unavailable |
| `cost_status` | Yes | `estimated`, `unavailable`, `not_applicable`, `mixed` |
| `source_kind` | Yes | `derived_from_observed_tokens`, `derived_from_estimated_tokens`, `manual_override`, `unavailable` |

Estimated cost is product guidance, not a provider invoice.

## Content Capture

### `content_reference`

Metadata reference to captured content or a blocked content candidate.

| Field | Required | Purpose |
| --- | --- | --- |
| `content_reference_id` | Yes | Content reference identifier |
| `customer_organization_id` | Yes | Tenant owner |
| `agent_session_id` | No | Session |
| `telemetry_envelope_id` | Yes | Source envelope |
| `content_class` | Yes | `prompt_snippet`, `tool_input_excerpt`, `tool_output_excerpt`, `model_response_excerpt`, `command_summary`, `file_content_excerpt`, `metadata_only` |
| `capture_state` | Yes | `not_allowed`, `metadata_only`, `captured`, `redaction_failed`, `review_required`, `discarded`, `approved_excerpt` |
| `redaction_status` | Yes | `not_required`, `passed`, `failed`, `review_required`, `manually_approved` |
| `content_hash` | No | Hash when allowed |
| `blob_uri` | No | Blob pointer only when redacted content is stored |
| `blob_version` | No | Blob version or ETag |
| `policy_version_id` | Yes | Content policy version |
| `retention_class` | Yes | Retention classification |
| `expires_at_utc` | No | Deletion deadline |
| `audit_event_id` | Yes | Content decision audit |

`blob_uri` must be null unless content has passed redaction or manual approval.

### `redaction_review`

Tracks privileged review decisions.

| Field | Required | Purpose |
| --- | --- | --- |
| `redaction_review_id` | Yes | Review identifier |
| `customer_organization_id` | Yes | Tenant owner |
| `content_reference_id` | Yes | Content reference |
| `reviewer_product_user_id` | Yes | SecurityReviewer |
| `decision` | Yes | `retry`, `discard`, `approve_excerpt`, `reject_excerpt` |
| `decision_reason` | No | Reviewer note |
| `decided_at_utc` | Yes | Decision timestamp |
| `audit_event_id` | Yes | Governance audit reference |

## Hotspots And Recommendations

### `token_hotspot`

Represents a confirmed or candidate token hotspot.

| Field | Required | Purpose |
| --- | --- | --- |
| `token_hotspot_id` | Yes | Hotspot identifier |
| `customer_organization_id` | Yes | Tenant owner |
| `agent_session_id` | No | Session scope |
| `repository_id` | No | Repository scope |
| `hotspot_type` | Yes | `prompt_cache_breakage`, `large_context`, `tool_loop`, `model_retry`, `repo_context_bloat`, `generated_artifact_bloat`, `expensive_model_choice`, `error_rework`, `unknown` |
| `finding_state` | Yes | `confirmed`, `candidate_llm_inferred`, `candidate_correlated`, `rejected`, `superseded` |
| `attribution_type` | Yes | `direct`, `correlated`, `llm_inferred`, `unavailable` |
| `confidence` | Yes | `high`, `medium`, `low`, `unavailable` |
| `token_burn_score` | No | Relative burn score |
| `estimated_cost_impact` | No | Estimated cost impact |
| `evidence_refs_json` | Yes | Evidence references |
| `created_at_utc` | Yes | Creation timestamp |

LLM-inferred candidate hotspots must not be displayed as confirmed findings.

### `recommendation`

Represents optimization coaching generated from evidence.

| Field | Required | Purpose |
| --- | --- | --- |
| `recommendation_id` | Yes | Recommendation identifier |
| `customer_organization_id` | Yes | Tenant owner |
| `token_hotspot_id` | No | Parent hotspot |
| `agent_session_id` | No | Session scope |
| `recommendation_kind` | Yes | `deterministic`, `llm_assisted`, `manual` |
| `recommendation_state` | Yes | `candidate`, `validated`, `rejected`, `superseded` |
| `visibility_scope` | Yes | `self`, `team_scoped`, `security_review`, `admin`, `aggregate_only` |
| `model_policy_version_id` | No | Recommendation Model Policy version |
| `prompt_template_version` | No | Prompt template version |
| `evidence_packet_hash` | Yes | Evidence packet hash |
| `summary` | Yes | User-facing recommendation |
| `expected_benefit` | No | Expected improvement |
| `created_at_utc` | Yes | Creation timestamp |
| `audit_event_id` | Yes | Generation audit |

### `recommendation_evidence`

Links recommendations to bounded evidence.

| Field | Required | Purpose |
| --- | --- | --- |
| `recommendation_evidence_id` | Yes | Evidence link identifier |
| `customer_organization_id` | Yes | Tenant owner |
| `recommendation_id` | Yes | Recommendation |
| `evidence_kind` | Yes | `telemetry_envelope`, `token_observation`, `content_reference`, `repository_evidence`, `audit_event`, `pricing_basis` |
| `evidence_id` | Yes | Referenced record |
| `evidence_state` | Yes | `observed`, `derived`, `correlated`, `llm_inferred`, `unavailable` |

## Pricing And Budgets

### `pricing_basis`

Versioned pricing basis for estimated cost.

| Field | Required | Purpose |
| --- | --- | --- |
| `pricing_basis_id` | Yes | Pricing basis identifier |
| `customer_organization_id` | Yes | Tenant owner |
| `harness` | Yes | Harness |
| `provider_name` | No | Provider |
| `model_name` | No | Model |
| `billing_route` | No | Billing route |
| `currency` | Yes | Currency |
| `input_price_per_million_tokens` | No | Input token rate |
| `output_price_per_million_tokens` | No | Output token rate |
| `cached_input_price_per_million_tokens` | No | Cached input token rate |
| `pricing_version` | Yes | Active version |
| `source_kind` | Yes | `automated_seed`, `admin_override`, `provider_docs`, `enterprise_contract` |
| `review_state` | Yes | `candidate`, `approved`, `rejected`, `superseded` |
| `effective_from_utc` | Yes | Start time |
| `effective_to_utc` | No | End time |
| `audit_event_id` | Yes | Review audit |

Automated Pricing Seed creates candidate records. It must not silently change active cost estimates without Pricing Update Review.

### `budget_policy`

Defines non-punitive budget alerts.

| Field | Required | Purpose |
| --- | --- | --- |
| `budget_policy_id` | Yes | Budget policy identifier |
| `customer_organization_id` | Yes | Tenant owner |
| `scope_kind` | Yes | `organization`, `team`, `repository`, `workflow`, `harness`, `model` |
| `scope_id` | No | Scoped resource |
| `metric_kind` | Yes | `tokens`, `estimated_cost`, `cache_miss_rate`, `error_rework` |
| `threshold_json` | Yes | Threshold definition |
| `status` | Yes | `active`, `disabled` |
| `audit_event_id` | Yes | Governance audit reference |

No budget policy can rank individual developers by waste or wrongness.

## Audit And Lifecycle

### `audit_event`

Represents a Governance Audit Event.

| Field | Required | Purpose |
| --- | --- | --- |
| `audit_event_id` | Yes | Audit event identifier |
| `customer_organization_id` | No | Tenant when applicable |
| `actor_product_user_id` | No | User actor |
| `actor_service_identity` | No | Service actor |
| `action` | Yes | Action name |
| `target_kind` | Yes | Target entity kind |
| `target_id` | No | Target record |
| `decision` | Yes | `allowed`, `denied`, `created`, `updated`, `deleted`, `generated`, `rejected` |
| `effective_role` | No | Role used |
| `correlation_id` | Yes | Request or job correlation |
| `evidence_metadata_json` | Yes | Non-sensitive evidence metadata for the decision |
| `created_at_utc` | Yes | Audit timestamp |

Audit evidence metadata must not store raw prompt text, code content, command output, tool results, secrets, tokens, or connection strings.

### `data_lifecycle_request`

Tracks export, deletion, legal hold, and offboarding workflows.

| Field | Required | Purpose |
| --- | --- | --- |
| `data_lifecycle_request_id` | Yes | Request identifier |
| `customer_organization_id` | Yes | Tenant owner |
| `request_type` | Yes | `export`, `delete`, `legal_hold`, `offboard` |
| `scope_kind` | Yes | `organization`, `user`, `repository`, `session`, `content` |
| `scope_id` | No | Scoped record |
| `status` | Yes | `requested`, `approved`, `running`, `completed`, `failed`, `cancelled` |
| `requested_by_product_user_id` | Yes | Requester |
| `approved_by_product_user_id` | No | Approver |
| `audit_event_id` | Yes | Governance audit reference |

## Indexing And Constraints

Required constraints:

- Tenant-owned tables include `customer_organization_id`.
- Foreign keys include tenant-compatible relationships in application logic and migrations.
- External identity references are unique per `customer_organization_id`, `identity_tenant_id`, and `external_subject_id`.
- Active role mappings are unique by principal, role, and scope.
- Active ingestion credentials are unique by credential hash.
- `telemetry_envelope.dedupe_key_hash` is unique per Customer Organization.
- Token observations allow null values for unavailable metrics.
- Content references cannot have `blob_uri` when redaction status is failed, review required, or not allowed.

Recommended indexes:

- `(customer_organization_id, status)` on tenant configuration tables.
- `(customer_organization_id, product_user_id, started_at_utc desc)` on `agent_session`.
- `(customer_organization_id, repository_id, started_at_utc desc)` where repository joins are materialized.
- `(customer_organization_id, agent_session_id)` on session child tables.
- `(customer_organization_id, source_event_timestamp_utc)` on `telemetry_envelope`.
- `(customer_organization_id, dedupe_key_hash)` unique on `telemetry_envelope`.
- `(customer_organization_id, content_reference_id, capture_state)` for review queues.
- `(customer_organization_id, created_at_utc desc)` on `audit_event`.

## MVP Boundary

The Azure Production MVP needs these tables first:

- `customer_organization`.
- `identity_tenant`.
- `product_user`.
- `product_role_mapping`.
- `harness_setup_profile`.
- `scoped_ingestion_credential`.
- `telemetry_envelope`.
- `ingestion_rejection`.
- `agent_session`.
- `agent_turn`.
- `model_invocation`.
- `tool_activity`.
- `token_observation`.
- `cost_estimate`.
- `content_reference`.
- `redaction_review`.
- `token_hotspot`.
- `recommendation`.
- `recommendation_evidence`.
- `pricing_basis`.
- `budget_policy`.
- `audit_event`.

Target-state tables such as `source_provider_connection`, full `repository` discovery, `repository_evidence`, and `data_lifecycle_request` can be introduced in slices, but their relationships are defined here so MVP schema decisions do not block SaaS tenancy later.

## Migration From Superseded Implementation

The superseded local-first implementation included direct file import, Copilot fixture parsing, and local workspace repository enrichment. Those are historical implementation artifacts of the superseded MVP and are not active production paths.

Production implementation should introduce new tables rather than trying to mutate local-only imports into tenant-aware product ingestion records in place.

Mapping guidance:

| Superseded local-first concept | Production replacement |
| --- | --- |
| `telemetry_import` | `telemetry_envelope` plus `ingestion_rejection` |
| `agent_session` without tenant | Tenant-scoped `agent_session` |
| `workspace_repo` | `repository` plus `repository_evidence` |
| `token_metric` | `token_observation` |
| `harness_pricing_basis` | Tenant-scoped `pricing_basis` |
| `context_source` | `repository_evidence` and later content-scanning records |
| `hotspot` | `token_hotspot` |
| `recommendation` deterministic only | `recommendation` with deterministic and LLM-assisted states |

## Verified Platform Facts

- Azure Database for PostgreSQL Flexible Server supports private access through Azure virtual network integration and denies public endpoint access in that model: https://learn.microsoft.com/en-us/azure/postgresql/overview#automatic-backups
- Azure Database for PostgreSQL Flexible Server automatically performs backups and supports point-in-time restore within the configured retention period: https://learn.microsoft.com/en-us/azure/postgresql/backup-restore/concepts-business-continuity
- Azure Database for PostgreSQL Flexible Server supports zone-redundant high availability in supported regions and tiers: https://learn.microsoft.com/en-us/azure/postgresql/high-availability/concepts-high-availability
- Azure Storage encrypts data at rest: https://learn.microsoft.com/en-us/azure/storage/common/storage-service-encryption
- Blob lifecycle management supports rule-based transition and deletion: https://learn.microsoft.com/en-us/azure/storage/blobs/lifecycle-management-overview
