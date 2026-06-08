# Local-First MVP Data Model

## Purpose

Define the initial normalized PostgreSQL model for the Local-First MVP. This is a logical model for issue #1, not a migration file. Implementation may refine column names, but it must preserve the behavioral contracts here.

## Tables

### `telemetry_import`

Tracks each Direct File Import run.

| Column | Type | Nullability | Purpose | MVP |
| --- | --- | --- | --- | --- |
| `telemetry_import_id` | UUID | Not null, primary key | Import run id | Yes |
| `harness` | text | Not null | Primary Harness value, initially `copilot` | Yes |
| `source_kind` | text | Not null | `direct_file` for MVP | Yes |
| `source_file_hash` | text | Nullable | Hash of imported file contents, not the source path | Yes |
| `environment_name` | text | Nullable | Resource deployment environment, such as `local` | Yes |
| `started_at_utc` | timestamptz | Not null | Import start | Yes |
| `completed_at_utc` | timestamptz | Nullable | Import completion | Yes |
| `import_status` | text | Not null | `succeeded`, `failed`, or `partial` | Yes |
| `record_count` | integer | Not null | Parsed JSON object count | Yes |
| `skipped_record_count` | integer | Not null | Empty or ignored record count | Yes |
| `warning_count` | integer | Not null | Non-fatal import warnings | Yes |
| `error_count` | integer | Not null | Fatal or skipped error count | Yes |

### `agent_session`

Represents one Coding-Agent Harness session.

| Column | Type | Nullability | Purpose | MVP |
| --- | --- | --- | --- | --- |
| `agent_session_id` | UUID | Not null, primary key | Internal session id | Yes |
| `telemetry_import_id` | UUID | Not null, foreign key | Source import | Yes |
| `harness` | text | Not null | `copilot` for MVP | Yes |
| `harness_source` | text | Nullable | Raw OTel service identity, such as `github-copilot-vscode` | Yes |
| `harness_version` | text | Nullable | Observed service or instrumentation version | Yes |
| `agent_name` | text | Nullable | Observed agent name, such as GitHub Copilot Chat | Yes |
| `provider_session_id_hash` | text | Nullable | Hashed opaque fixture session id from resource attributes or log attributes | Yes |
| `team_hash` | text | Nullable | Hashed team/resource label | Yes |
| `user_hash` | text | Nullable | Salted hash for Developer Identity when available | Yes |
| `developer_display_label` | text | Nullable | Dashboard display label such as email, full name, service account, or explicit alias when supplied or clearly emitted by telemetry | Yes |
| `display_identity_source` | text | Nullable | Source of developer display identity, such as `explicit_import`, `repo_enrichment`, or `harness_telemetry` | Yes |
| `started_at_utc` | timestamptz | Nullable | Session start if observed | Yes |
| `ended_at_utc` | timestamptz | Nullable | Session end if inferred or observed | P2 |
| `token_total_type` | text | Not null | `observed`, `estimated`, `mixed`, or `unavailable` | Yes |
| `input_tokens` | bigint | Nullable | Session input tokens when provable | Yes |
| `output_tokens` | bigint | Nullable | Session output tokens when provable | Yes |
| `estimated_cost_usd` | numeric | Nullable | Estimated Token Cost when matched to a Harness Pricing Basis | Yes |
| `estimated_cost_status` | text | Not null | `estimated`, `unavailable`, or `not_applicable` | Yes |
| `pricing_basis_id` | UUID | Nullable, foreign key | Pricing rule used for Estimated Token Cost when matched | Yes |
| `content_captured` | boolean | Not null | False for MVP default imports | Yes |

### `workspace_repo`

Associates sessions with repository roots and dashboard-safe repository display identity.

| Column | Type | Nullability | Purpose | MVP |
| --- | --- | --- | --- | --- |
| `workspace_repo_id` | UUID | Not null, primary key | Internal repo id | Yes |
| `agent_session_id` | UUID | Nullable, foreign key | Session association when known | Yes |
| `repo_friendly_name` | text | Nullable | User-provided display label | Yes |
| `repo_full_name` | text | Nullable | Full repository name when supplied explicitly or clearly emitted by telemetry | Yes |
| `repo_path_hash` | text | Nullable | Salted hash of local repo path when a path is available | Yes |
| `repo_display_path` | text | Nullable | Dashboard display path, including absolute local path when supplied explicitly or by Repo Context Enrichment | Yes |
| `display_identity_source` | text | Nullable | Source of repo display identity, such as `explicit_import`, `repo_enrichment`, or `harness_telemetry` | Yes |
| `branch_name_hash` | text | Nullable | Optional branch hash | P2 |

### `telemetry_record`

Stores normalized metadata for each non-empty fixture record.

| Column | Type | Nullability | Purpose | MVP |
| --- | --- | --- | --- | --- |
| `telemetry_record_id` | UUID | Not null, primary key | Internal record id | Yes |
| `telemetry_import_id` | UUID | Not null, foreign key | Source import | Yes |
| `agent_session_id` | UUID | Nullable, foreign key | Session association | Yes |
| `record_index` | integer | Not null | Order in parsed stream | Yes |
| `record_kind` | text | Not null | `log`, `metric`, or `resource` | Yes |
| `body_redacted_summary` | text | Nullable | Event-like redacted summary derived from log body | Yes |
| `event_name` | text | Nullable | Event name attribute | Yes |
| `trace_id_hash` | text | Nullable | Hashed opaque trace id | Yes |
| `span_id_hash` | text | Nullable | Hashed opaque span id | Yes |
| `trace_flags` | integer | Nullable | OTel trace flags | Yes |
| `observed_at_utc` | timestamptz | Nullable | Event time | Yes |
| `received_at_utc` | timestamptz | Nullable | Observed receive/export time | Yes |
| `instrumentation_scope_name` | text | Nullable | Scope name | Yes |
| `instrumentation_scope_version` | text | Nullable | Scope version | Yes |
| `attribute_count` | integer | Nullable | Observed attribute count | Yes |

### `agent_turn`

Represents an agent turn observed in Copilot logs.

| Column | Type | Nullability | Purpose | MVP |
| --- | --- | --- | --- | --- |
| `agent_turn_id` | UUID | Not null, primary key | Internal turn id | Yes |
| `agent_session_id` | UUID | Not null, foreign key | Parent session | Yes |
| `telemetry_record_id` | UUID | Nullable, foreign key | Source record | Yes |
| `turn_index` | integer | Nullable | Observed turn index | Yes |
| `tool_call_count` | integer | Nullable | Observed tool count | Yes |
| `success` | boolean | Nullable | Observed success where emitted | Yes |
| `started_at_utc` | timestamptz | Nullable | Start time if available | P2 |
| `ended_at_utc` | timestamptz | Nullable | End time if available | P2 |

### `model_invocation`

Represents one GenAI model invocation.

| Column | Type | Nullability | Purpose | MVP |
| --- | --- | --- | --- | --- |
| `model_invocation_id` | UUID | Not null, primary key | Internal invocation id | Yes |
| `agent_session_id` | UUID | Not null, foreign key | Parent session | Yes |
| `agent_turn_id` | UUID | Nullable, foreign key | Parent turn if correlated | Yes |
| `telemetry_record_id` | UUID | Not null, foreign key | Source log record | Yes |
| `operation_name` | text | Nullable | `chat` in fixture | Yes |
| `provider_name` | text | Nullable | Provider for this invocation when emitted by a log or proven by deterministic correlation; NULL for the current fixture logs | Yes |
| `request_model` | text | Nullable | Requested model | Yes |
| `response_model` | text | Nullable | Response model | Yes |
| `provider_response_id_hash` | text | Nullable | Hashed opaque response id | Yes |
| `finish_reasons_json` | jsonb | Nullable | Response finish reasons | Yes |
| `request_max_tokens` | integer | Nullable | Request max tokens setting | Yes |
| `request_temperature` | numeric | Nullable | Request temperature setting | Yes |
| `input_tokens` | bigint | Nullable | Observed input tokens | Yes |
| `output_tokens` | bigint | Nullable | Observed output tokens | Yes |
| `token_total_type` | text | Not null | `observed`, `estimated`, `mixed`, or `unavailable` | Yes |
| `estimated_cost_usd` | numeric | Nullable | Estimated Token Cost when matched to a Harness Pricing Basis | Yes |
| `estimated_cost_status` | text | Not null | `estimated`, `unavailable`, or `not_applicable` | Yes |
| `pricing_basis_id` | UUID | Nullable, foreign key | Pricing rule used for Estimated Token Cost when matched | Yes |

### `token_metric`

Stores explicit status for token metrics so missing values are not confused with zero.

| Column | Type | Nullability | Purpose | MVP |
| --- | --- | --- | --- | --- |
| `token_metric_id` | UUID | Not null, primary key | Internal metric id | Yes |
| `model_invocation_id` | UUID | Nullable, foreign key | Invocation metric | Yes |
| `agent_session_id` | UUID | Nullable, foreign key | Session aggregate metric | Yes |
| `metric_name` | text | Not null | `input_tokens`, `output_tokens`, or `total_tokens` | Yes |
| `metric_status` | text | Not null | `observed`, `estimated`, `unavailable`, or `not_applicable` | Yes |
| `metric_confidence` | text | Not null | `observed`, `estimated`, `inferred`, or `unavailable` | Yes |
| `value` | bigint | Nullable | Token count; NULL when unavailable | Yes |
| `source` | text | Not null | `log_attribute`, `metric_histogram`, `estimator`, or `missing_log_attribute` | Yes |

### `harness_pricing_basis`

Stores local, versioned rules for Estimated Token Cost. It is dashboard guidance and not a provider invoice.

| Column | Type | Nullability | Purpose | MVP |
| --- | --- | --- | --- | --- |
| `pricing_basis_id` | UUID | Not null, primary key | Internal pricing rule id | Yes |
| `harness` | text | Not null | Coding-Agent Harness, such as `copilot` | Yes |
| `provider_name` | text | Nullable | Provider or billing provider when known | Yes |
| `model_name` | text | Nullable | Model name matched from telemetry | Yes |
| `billing_route` | text | Nullable | Billing route, such as Copilot AI credits or provider API rate | Yes |
| `input_price_per_million_tokens_usd` | numeric | Nullable | Input token rate | Yes |
| `output_price_per_million_tokens_usd` | numeric | Nullable | Output token rate | Yes |
| `currency` | text | Not null | Currency for the rate, initially `USD` | Yes |
| `pricing_version` | text | Not null | Local version label for deterministic estimates | Yes |
| `effective_from_utc` | timestamptz | Nullable | Start of pricing validity when known | Yes |
| `source_label` | text | Nullable | Human-readable pricing source label | Yes |

### `tool_call`

Represents an observed tool call.

| Column | Type | Nullability | Purpose | MVP |
| --- | --- | --- | --- | --- |
| `tool_call_id` | UUID | Not null, primary key | Internal tool call id | Yes |
| `agent_session_id` | UUID | Not null, foreign key | Parent session | Yes |
| `agent_turn_id` | UUID | Nullable, foreign key | Parent turn if correlated | Yes |
| `telemetry_record_id` | UUID | Nullable, foreign key | Source record | Yes |
| `tool_name` | text | Nullable | Tool name only | Yes |
| `duration_ms` | bigint | Nullable | Observed tool duration | Yes |
| `success` | boolean | Nullable | Observed success flag | Yes |
| `arguments_captured` | boolean | Not null | False by default | Yes |
| `result_captured` | boolean | Not null | False by default | Yes |

### `metric_observation`

Stores aggregate OTel metrics without treating them as per-turn facts.

| Column | Type | Nullability | Purpose | MVP |
| --- | --- | --- | --- | --- |
| `metric_observation_id` | UUID | Not null, primary key | Internal metric observation id | Yes |
| `telemetry_import_id` | UUID | Not null, foreign key | Source import | Yes |
| `agent_session_id` | UUID | Nullable, foreign key | Session association | Yes |
| `scope_name` | text | Nullable | OTel instrumentation scope name | Yes |
| `scope_version` | text | Nullable | OTel instrumentation scope version | Yes |
| `metric_name` | text | Not null | Descriptor name | Yes |
| `metric_type` | text | Not null | Descriptor type | Yes |
| `value_type` | integer | Nullable | Descriptor value type | Yes |
| `unit` | text | Nullable | Descriptor unit | Yes |
| `description` | text | Nullable | Descriptor description | Yes |
| `aggregation_temporality` | integer | Nullable | OTel temporality enum value | Yes |
| `is_monotonic` | boolean | Nullable | Counter monotonic flag | Yes |
| `data_point_type` | integer | Nullable | Data point type enum value | Yes |
| `start_time_utc` | timestamptz | Nullable | Data point start | Yes |
| `end_time_utc` | timestamptz | Nullable | Data point end | Yes |
| `attributes_json` | jsonb | Nullable | Metric dimensions | Yes |
| `value_json` | jsonb | Nullable | Scalar or histogram value | Yes |
| `bucket_boundaries_json` | jsonb | Nullable | Histogram bucket boundaries | Yes |

### `context_source`

Represents Repo Context Enrichment output.

| Column | Type | Nullability | Purpose | MVP |
| --- | --- | --- | --- | --- |
| `context_source_id` | UUID | Not null, primary key | Internal context source id | Yes |
| `workspace_repo_id` | UUID | Not null, foreign key | Repository scanned | Yes |
| `source_type` | text | Not null | `file`, `folder`, `spec`, `instruction`, or `generated_artifact` | Yes |
| `path_hash` | text | Not null | Salted path hash | Yes |
| `display_path` | text | Nullable | Repo-relative or absolute local dashboard display path when supplied by Repo Context Enrichment or explicit input | Yes |
| `display_path_scope` | text | Nullable | `repo_relative`, `absolute_local`, or `unknown` | Yes |
| `file_category` | text | Not null | File Context Category | Yes |
| `spec_artifact_status` | text | Nullable | `active`, `bloat`, or `neutral` | Yes |
| `eligible_for_inferred_hotspot` | boolean | Not null | Generated artifact policy result | Yes |
| `size_bytes` | bigint | Nullable | File size | Yes |
| `line_count` | integer | Nullable | File line count | Yes |

### `hotspot`

Represents a Token Hotspot.

| Column | Type | Nullability | Purpose | MVP |
| --- | --- | --- | --- | --- |
| `hotspot_id` | UUID | Not null, primary key | Internal hotspot id | Yes |
| `agent_session_id` | UUID | Nullable, foreign key | Session scoped hotspot | Yes |
| `workspace_repo_id` | UUID | Nullable, foreign key | Repo scoped hotspot | Yes |
| `source_type` | text | Not null | `spec`, `file`, `folder`, `tool`, `model`, `harness`, or `workspace` | Yes |
| `source_ref` | text | Nullable | Human-readable source reference, which may include repo-relative paths or absolute local Repo Display Paths when available | Yes |
| `attribution_type` | text | Not null | `direct`, `correlated`, or `inferred` | Yes |
| `confidence` | text | Not null | `high`, `medium`, or `low` | Yes |
| `suspected_cause` | text | Not null | Explanation of Token Burn | Yes |
| `evidence_refs_json` | jsonb | Not null | References to telemetry/context rows | Yes |
| `token_burn_score` | numeric | Nullable | Ranking score independent of dollar cost | Yes |

### `recommendation`

Represents a Deterministic Recommendation.

| Column | Type | Nullability | Purpose | MVP |
| --- | --- | --- | --- | --- |
| `recommendation_id` | UUID | Not null, primary key | Internal recommendation id | Yes |
| `hotspot_id` | UUID | Not null, foreign key | Parent hotspot | Yes |
| `recommendation_type` | text | Not null | `deterministic` for MVP | Yes |
| `rule_id` | text | Not null | Rule identifier, such as `rule-2-superseded-spec-bloat` | Yes |
| `trigger_condition` | text | Not null | What caused the rule to fire | Yes |
| `recommended_action` | text | Not null | Action to take | Yes |
| `expected_benefit` | text | Not null | Benefit statement | Yes |
| `confidence` | text | Not null | Recommendation confidence | Yes |
| `evidence_refs_json` | jsonb | Not null | References to supporting rows | Yes |

## Enum Values

| Enum | MVP values |
| --- | --- |
| `harness` | `copilot` |
| `source_kind` | `direct_file` |
| `record_kind` | `log`, `metric`, `resource` |
| `metric_status` | `observed`, `estimated`, `unavailable`, `not_applicable` |
| `metric_confidence` | `observed`, `estimated`, `inferred`, `unavailable` |
| `token_metric.source` | `log_attribute`, `metric_histogram`, `estimator`, `missing_log_attribute` |
| `token_total_type` | `observed`, `estimated`, `mixed`, `unavailable` |
| `estimated_cost_status` | `estimated`, `unavailable`, `not_applicable` |
| `attribution_type` | `direct`, `correlated`, `inferred` |
| `display_identity_source` | `explicit_import`, `repo_enrichment`, `harness_telemetry` |
| `display_path_scope` | `repo_relative`, `absolute_local`, `unknown` |
| `file_category` | `source`, `generated`, `lockfile`, `vendor`, `binary`, `build_artifact`, `spec`, `instruction`, `unknown` |
| `spec_artifact_status` | `active`, `bloat`, `neutral` |
| `recommendation_type` | `deterministic` |
| `import_status` | `succeeded`, `failed`, `partial` |

## Indexes

MVP query indexes:

* `agent_session(harness, started_at_utc)`
* `agent_session(user_hash)`
* `agent_session(developer_display_label)`
* `agent_session(token_total_type)`
* `agent_session(estimated_cost_status)`
* `agent_turn(agent_session_id, turn_index)`
* `model_invocation(agent_session_id)`
* `model_invocation(request_model)`
* `model_invocation(response_model)`
* `tool_call(agent_session_id, tool_name)`
* `workspace_repo(repo_path_hash)`
* `workspace_repo(repo_full_name)`
* `context_source(workspace_repo_id, file_category)`
* `context_source(workspace_repo_id, spec_artifact_status)`
* `hotspot(agent_session_id, token_burn_score)`
* `hotspot(workspace_repo_id, attribution_type)`
* `recommendation(hotspot_id)`
* `metric_observation(agent_session_id, metric_name)`
* `harness_pricing_basis(harness, model_name, billing_route, pricing_version)`

## Dashboard Overview Read Model

`/dashboard/overview` is a read model produced from the normalized tables, not an MVP persistence table. The API owns Dashboard Range filtering, aggregation, dense daily buckets, 30-day Moving Burn Average, Metric Quality Marker propagation, filter option lists, freshness summary, hotspot ranking, and Recommendation Rationale.

Request parameters:

* `range`: Dashboard Range preset, defaulting to `90d`.
* `repository`: optional Workspace Repo filter by stable id or display option value.
* `harness`: optional Coding-Agent Harness filter.
* `model`: optional requested or response model filter.
* `metric_status`: optional Metric Status filter.
* `timezone`: optional Dashboard Timezone for date bucketing; timestamps remain stored in UTC.

Response components:

* Dashboard Freshness Summary: latest telemetry import time, latest Repo Context Enrichment time when available, and Harness Pricing Basis version or date.
* Filter options: repository, Coding-Agent Harness, model, and Metric Status options that match the current data set.
* Typed Burn Total: raw token values plus Token Total Type and Metric Quality Markers.
* Estimated Token Cost: raw dollar value when matched to Harness Pricing Basis, or Unavailable Token Cost when unmatched.
* Token Burn Timeline: one bucket for every day in the selected Dashboard Range, including zero-burn days.
* Moving Burn Average: server-computed 30-day Moving Burn Average.
* Model Cost Mix and compact tool behavior summary.
* Hotspot Driver View rows with Token Hotspots, attribution, confidence, exact values, and Dashboard Fragment Targets.
* Recommendation Action Section rows with Recommendation Rationale, expected benefit, confidence, and evidence links.
* SessionsEvidenceTable rows with session, developer label, repo display, token split, and evidence references.

The read model returns raw values plus semantic fields. Blazor owns display strings, local timezone presentation, compact token labels, badges, CSS states, and component layout.

## Fixture-to-Table Mapping

| Fixture record | Normalized rows |
| --- | --- |
| Empty object `{}` | No domain rows; increment `telemetry_import.skipped_record_count` |
| Resource-only object | `telemetry_import`, optional `agent_session` enrichment |
| Direct File Import source file | `telemetry_import.source_file_hash` from file contents only; do not hash or store the raw source file path by default |
| Direct File Import repo option | Optional `workspace_repo` when the import command receives explicit repo root/path, full repo name, or repo display metadata. Repo path is salted and hashed into `repo_path_hash` when present. The parser must not invent repo association from telemetry or source file path |
| Direct File Import developer option | `agent_session.user_hash`, `agent_session.developer_display_label`, and `agent_session.display_identity_source` when developer identity is supplied explicitly or clearly emitted by telemetry |
| `copilot_chat.session.start` log | `telemetry_record`, `agent_session` |
| `copilot_chat.agent.turn` log | `telemetry_record`, `agent_turn`; do not create token metrics from turn logs unless future telemetry emits turn-level token attributes with an explicit model boundary |
| `copilot_chat.tool.call` log | `telemetry_record`, `tool_call` |
| `gen_ai.client.inference.operation.details` log | `telemetry_record`, `model_invocation`, `token_metric` rows |
| `gen_ai.client.token.usage` metric | `telemetry_record`, `metric_observation`; optional aggregate `token_metric` only when implementation can prove session association |
| Other `copilot_chat.*` metrics | `telemetry_record`, `metric_observation` |

## Privacy Defaults

Default Local-First MVP imports are metadata-only for content capture. The MVP has one privacy mode.

* Full prompt text is not persisted.
* Full response text is not persisted.
* Full code file content is not persisted.
* Full tool arguments and results are not persisted.
* Developer Display Labels may be persisted when supplied explicitly, produced by Repo Context Enrichment, or clearly emitted by telemetry.
* Full repo names and Repo Display Paths may be persisted when supplied explicitly, produced by Repo Context Enrichment, or clearly emitted by telemetry.
* `telemetry_import.source_file_hash` hashes file contents only. If a future feature needs source path matching, it must use a separate salted path hash field.
* The MVP must not silently scrape Git config, OS user accounts, shell environment, or unrelated local files to populate display identity.
* `context_source.display_path` may store repo-relative paths for main-dashboard context and absolute local paths for expanded evidence detail when available.
* `hotspot.source_ref` may contain repo-relative paths or absolute local Repo Display Paths when available. It must not contain provider response ids, session ids, trace ids, span ids, prompt text, response text, tool arguments, or tool results.
* Raw team labels are hashed before storage.
* Provider response ids, trace ids, span ids, resource session ids, and log session ids are hashed before storage.

## Migration Strategy

Use normal PostgreSQL migrations once implementation starts. The first migration should create all MVP tables and enum-compatible check constraints or lookup tables. Later migrations should add:

* OpenTelemetry Collector ingestion fields.
* Claude Code and Codex adapter-specific fields.
* Content Capture Mode columns.
* Pricing Refresh Workflow metadata.
* Work Family rollup tables.
* Identity Mapping tables if later phases need cross-system identity merge behavior beyond Developer Display Labels and User Hash.
* Azure Production Path operational metadata.

## Implementation Gate

Issue #1 satisfies the PRD implementation-start gate when:

* `docs/architecture/copilot-otel-field-mapping.md` exists.
* This data model document exists.
* Direct File Import implementation starts from the observed fixture fields and nullable metric semantics documented here.
* Missing token metrics are explicitly represented as `NULL` plus status, never zero.
