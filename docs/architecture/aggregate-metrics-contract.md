# Aggregate Metrics Contract

## Purpose

This document defines the first-release aggregate metric names, allowed labels, and PromQL query contracts used by Azure Managed Grafana.

These metrics are product-normalized metrics emitted by the Product Ingestion Endpoint and product jobs after tenant, credential, schema, policy, and redaction decisions have been applied. They are not raw harness metrics and are not a substitute for Product Dashboard investigation.

## Naming Rules

Metric names use the `tokenobs_` prefix.

Rules:

- Counters end in `_total`.
- Histograms end in the base unit name, such as `_seconds`.
- Estimated cost metrics include `_usd`.
- Metric names and labels use lowercase snake case.
- Labels must be low-cardinality and must not contain raw content, session identifiers, developer identifiers, credential identifiers, file paths, repository paths, trace IDs, span IDs, prompt IDs, hashes, IP addresses, or user-agent strings.
- Repository-level and developer-level analysis belongs in Product Dashboard, not first-release Grafana aggregate metrics.
- Metric labels must not include Product Dashboard role names.

## Common Labels

Every product aggregate metric includes these labels unless explicitly marked otherwise.

| Label | Required | Allowed values |
| --- | --- | --- |
| `customer_organization_slug` | Yes | Stable product tenant slug, such as `internal` |
| `environment` | Yes | `dv`, `qa`, `pp`, `pd` |
| `region` | Yes | Azure region slug, such as `eastus2` or `westeurope` |

## Label Sets

| Label | Allowed values |
| --- | --- |
| `harness` | `codex`, later `copilot`, `claude` |
| `model_provider` | `openai`, `anthropic`, `github`, `unknown` |
| `model` | Normalized model deployment or provider model label |
| `token_type` | `input`, `output`, `cached_input`, `reasoning_output`, `total` |
| `metric_status` | `observed`, `derived`, `estimated`, `unavailable`, `not_applicable`, `mixed` |
| `metric_confidence` | `observed`, `deterministic`, `estimated`, `llm_inferred`, `unavailable` |
| `cost_status` | `estimated`, `unavailable`, `not_applicable`, `mixed` |
| `pricing_basis_kind` | `provider_default`, `customer_override`, `manual_override`, `unavailable` |
| `signal_type` | `metrics`, `traces`, `logs` |
| `schema_version` | Product schema version, such as `2026-06-14` |
| `result` | Metric-specific result, such as `accepted`, `rejected`, `failed`, `succeeded` |
| `rejection_reason` | `none`, `invalid_credential`, `out_of_scope`, `unsupported_schema`, `malformed_otlp`, `payload_too_large`, `rate_limited`, `residency_mismatch`, `content_classification_failed`, `transient_failure` |
| `failure_reason` | Bounded implementation-defined failure category |
| `cache_result` | `hit`, `miss`, `bust`, `unavailable`, `not_applicable` |
| `cache_bust_category` | `prompt_changed`, `system_instruction_changed`, `tool_context_changed`, `repository_context_changed`, `model_changed`, `unknown` |
| `cache_evidence_state` | `observed`, `correlated`, `llm_inferred`, `unavailable`, `not_applicable` |
| `hotspot_type` | `prompt_cache_breakage`, `large_context`, `tool_loop`, `model_retry`, `repo_context_bloat`, `generated_artifact_bloat`, `expensive_model_choice`, `error_rework`, `unknown` |
| `finding_state` | `confirmed`, `llm_inferred_candidate` |
| `job_type` | `normalization`, `hotspot_detection`, `recommendation_generation`, `content_redaction`, `retention_cleanup`, `pricing_refresh`, `reprocessing`, `tenant_maintenance` |
| `http_route` | Product API route template, not raw URL |
| `http_method` | `GET`, `POST`, `PUT`, `PATCH`, `DELETE` |
| `status_class` | `2xx`, `3xx`, `4xx`, `5xx` |
| `container_app` | `product_dashboard`, `product_api`, `product_ingestion`, `product_jobs` |
| `budget_scope` | `customer_organization`, `team`, `repository`, `harness`, `model` |
| `period` | `daily`, `weekly`, `monthly` |

## Metrics

| Metric | Type | Unit | Labels beyond common labels | Purpose |
| --- | --- | --- | --- | --- |
| `tokenobs_tokens_total` | Counter | tokens | `harness`, `model_provider`, `model`, `token_type`, `metric_status`, `metric_confidence` | Token burn by token class |
| `tokenobs_token_metric_states_total` | Counter | observations | `harness`, `model_provider`, `model`, `token_type`, `metric_status`, `metric_confidence` | Token metric state observations, including unavailable and not applicable null-valued token metrics |
| `tokenobs_estimated_cost_usd_total` | Counter | USD | `harness`, `model_provider`, `model`, `cost_status`, `pricing_basis_kind` | Estimated token cost |
| `tokenobs_budget_threshold_usd` | Gauge | USD | `budget_scope`, `period` | Non-punitive budget threshold for aggregate comparison |
| `tokenobs_sessions_started_total` | Counter | sessions | `harness` | Session creation trend |
| `tokenobs_turns_total` | Counter | turns | `harness`, `result` | Turn trend |
| `tokenobs_model_invocations_total` | Counter | invocations | `harness`, `model_provider`, `model`, `result` | Model request, retry, and error trend |
| `tokenobs_ingestion_requests_total` | Counter | requests | `signal_type`, `result`, `rejection_reason`, `schema_version` | Accepted and rejected ingestion requests |
| `tokenobs_ingestion_request_duration_seconds` | Histogram | seconds | `signal_type`, `result` | Ingestion latency |
| `tokenobs_payload_normalization_failures_total` | Counter | failures | `signal_type`, `failure_reason` | Payload normalization failures |
| `tokenobs_cache_events_total` | Counter | events | `harness`, `model_provider`, `model`, `cache_result`, `cache_evidence_state` | Cache hit, miss, and bust trends |
| `tokenobs_cache_bust_token_impact_total` | Counter | tokens | `harness`, `model_provider`, `model`, `cache_bust_category`, `cache_evidence_state`, `metric_confidence` | Estimated token impact of cache busts |
| `tokenobs_hotspots_detected_total` | Counter | hotspots | `hotspot_type`, `finding_state` | Newly detected hotspots |
| `tokenobs_hotspots_open` | Gauge | hotspots | `hotspot_type`, `finding_state` | Current open hotspot count |
| `tokenobs_hotspot_estimated_cost_impact_usd` | Gauge | USD | `hotspot_type`, `finding_state`, `metric_confidence` | Current estimated cost impact for open hotspots |
| `tokenobs_background_jobs_total` | Counter | jobs | `job_type`, `result` | Background job success and failure trend |
| `tokenobs_background_job_duration_seconds` | Histogram | seconds | `job_type`, `result` | Background job latency |
| `tokenobs_redaction_events_total` | Counter | events | `result` | Redaction outcomes including failures and review-required states |
| `tokenobs_product_api_requests_total` | Counter | requests | `http_route`, `http_method`, `status_class` | Product API request volume and error trend |
| `tokenobs_product_api_request_duration_seconds` | Histogram | seconds | `http_route`, `http_method`, `status_class` | Product API latency |
| `tokenobs_container_app_replicas_ready` | Gauge | replicas | `container_app` | Ready replica count derived from platform telemetry where available |
| `tokenobs_container_app_replicas_desired` | Gauge | replicas | `container_app` | Desired replica count derived from platform telemetry where available |

`tokenobs_tokens_total` may use `token_type="total"` only when component token classes are unavailable for the same invocation. If component token classes are emitted for an invocation, `total` must not also be emitted for that invocation.

## Daily Token Timeline Buckets

The Product API exposes dense tenant-scoped daily token timeline buckets at:

`GET /api/v1/overview/token-timeline?from=YYYY-MM-DD&to=YYYY-MM-DD&movingAverageWindowDays=N`

The route requires `OverviewRead`.

Rules:

- `from` and `to` are inclusive UTC dates.
- `movingAverageWindowDays` must be between 1 and 90.
- The response is aggregate-only and must not include session IDs, Product user IDs, scoped ingestion credential IDs, trace IDs, span IDs, file paths, repository paths, prompts, command output, tool results, raw content references, or external URLs.
- One bucket is returned for every UTC day in the requested range, even when no accepted observations exist for that day.
- A day with no accepted token observations has `tokenBurn=0`, `metricStatus=not_applicable`, `metricConfidence=unavailable`, and `isDenseZeroBurn=true`.
- A day with only unavailable or not applicable observations has `tokenBurn=null`. The metric status remains `unavailable`, `not_applicable`, or `mixed` according to the accepted observations.
- Observed, derived, estimated, unavailable, not applicable, and mixed metric states remain distinct. Null token metrics must not be coerced to zero.
- When component token observations exist for a model invocation, those component observations define token burn for that invocation and a same-invocation `total` observation is excluded from token burn.
- The moving average is a trailing arithmetic average over the current bucket and the previous `N - 1` buckets. Buckets with `tokenBurn=null` are excluded. Dense zero-burn buckets with `tokenBurn=0` are included.

Response bucket fields:

| Field | Meaning |
| --- | --- |
| `customerOrganizationSlug` | Tenant slug only, not a tenant identifier or credential |
| `environment` | `dv`, `qa`, `pp`, or `pd` |
| `region` | Azure region slug |
| `bucketDateUtc` | UTC day in `YYYY-MM-DD` format |
| `period` | `day` |
| `tokenBurn` | Aggregate token burn, zero for dense empty days, or null when only unavailable or not applicable token observations exist |
| `metricStatus` | Aggregate token metric state |
| `metricConfidence` | Aggregate confidence state |
| `movingAverageTokenBurn` | Trailing arithmetic moving average, or null when the window has no numeric buckets |
| `movingAverageWindowDays` | Window used for the trailing moving average |
| `isDenseZeroBurn` | True only for generated days with no accepted token observations |
| `calculatedAtUtc` | Bucket calculation time |

## Dashboard Variables

Dashboards use these variables:

| Variable | Source |
| --- | --- |
| `$environment` | `label_values(tokenobs_sessions_started_total, environment)` |
| `$region` | `label_values(tokenobs_sessions_started_total, region)` |
| `$harness` | `label_values(tokenobs_sessions_started_total, harness)` |
| `$model` | `label_values(tokenobs_model_invocations_total, model)` |

Dashboards must not include user, session, credential, trace, file, prompt, or raw repository path variables.

## Executive Cost Overview Queries

| Panel | PromQL contract |
| --- | --- |
| Total token burn over time | `sum(rate(tokenobs_tokens_total{environment=~"$environment",region=~"$region",token_type=~"input|output|cached_input|reasoning_output|total"}[$__rate_interval]))` |
| Estimated cost over time | `sum(rate(tokenobs_estimated_cost_usd_total{environment=~"$environment",region=~"$region"}[$__rate_interval]))` |
| Cost by model | `sum by (model) (increase(tokenobs_estimated_cost_usd_total{environment=~"$environment",region=~"$region"}[$__range]))` |
| Token burn by model | `sum by (model) (increase(tokenobs_tokens_total{environment=~"$environment",region=~"$region",token_type=~"input|output|cached_input|reasoning_output|total"}[$__range]))` |
| Token burn by harness | `sum by (harness) (increase(tokenobs_tokens_total{environment=~"$environment",region=~"$region",token_type=~"input|output|cached_input|reasoning_output|total"}[$__range]))` |
| Trend versus budget threshold | `sum(increase(tokenobs_estimated_cost_usd_total{environment=~"$environment",region=~"$region"}[$__range])) / max(tokenobs_budget_threshold_usd{environment=~"$environment",region=~"$region",budget_scope="customer_organization"})` |
| Top hotspot categories by cost impact | `topk(10, sum by (hotspot_type) (tokenobs_hotspot_estimated_cost_impact_usd{environment=~"$environment",region=~"$region",finding_state="confirmed"}))` |

## Harness And Model Operations Queries

| Panel | PromQL contract |
| --- | --- |
| Codex session count over time | `sum(rate(tokenobs_sessions_started_total{environment=~"$environment",region=~"$region",harness="codex"}[$__rate_interval]))` |
| Codex turn count over time | `sum(rate(tokenobs_turns_total{environment=~"$environment",region=~"$region",harness="codex"}[$__rate_interval]))` |
| Model distribution | `sum by (model) (increase(tokenobs_model_invocations_total{environment=~"$environment",region=~"$region",harness=~"$harness",result="accepted"}[$__range]))` |
| Request count by model | `sum by (model) (rate(tokenobs_model_invocations_total{environment=~"$environment",region=~"$region",harness=~"$harness"}[$__rate_interval]))` |
| Error rate by harness and model | `sum by (harness, model) (rate(tokenobs_model_invocations_total{environment=~"$environment",region=~"$region",result=~"error|timeout|failed"}[$__rate_interval])) / sum by (harness, model) (rate(tokenobs_model_invocations_total{environment=~"$environment",region=~"$region"}[$__rate_interval]))` |
| Accepted telemetry count | `sum(increase(tokenobs_ingestion_requests_total{environment=~"$environment",region=~"$region",result="accepted"}[$__range]))` |
| Rejected telemetry by reason | `sum by (rejection_reason) (increase(tokenobs_ingestion_requests_total{environment=~"$environment",region=~"$region",result="rejected"}[$__range]))` |

## Cache And Hotspot Trends Queries

| Panel | PromQL contract |
| --- | --- |
| Cache hit rate | `sum(rate(tokenobs_cache_events_total{environment=~"$environment",region=~"$region",cache_result="hit"}[$__rate_interval])) / sum(rate(tokenobs_cache_events_total{environment=~"$environment",region=~"$region",cache_result=~"hit|miss|bust"}[$__rate_interval]))` |
| Cache miss or bust rate | `sum(rate(tokenobs_cache_events_total{environment=~"$environment",region=~"$region",cache_result=~"miss|bust"}[$__rate_interval])) / sum(rate(tokenobs_cache_events_total{environment=~"$environment",region=~"$region",cache_result=~"hit|miss|bust"}[$__rate_interval]))` |
| Cache-bust categories by count | `sum by (cache_bust_category) (increase(tokenobs_cache_events_total{environment=~"$environment",region=~"$region",cache_result="bust"}[$__range]))` |
| Cache-bust categories by token impact | `sum by (cache_bust_category) (increase(tokenobs_cache_bust_token_impact_total{environment=~"$environment",region=~"$region"}[$__range]))` |
| Confirmed hotspot count by type | `sum by (hotspot_type) (tokenobs_hotspots_open{environment=~"$environment",region=~"$region",finding_state="confirmed"})` |
| Confirmed hotspot estimated impact by type | `sum by (hotspot_type) (tokenobs_hotspot_estimated_cost_impact_usd{environment=~"$environment",region=~"$region",finding_state="confirmed"})` |
| LLM-inferred candidate hotspots | `sum by (hotspot_type) (tokenobs_hotspots_open{environment=~"$environment",region=~"$region",finding_state="llm_inferred_candidate"})` |

## Ingestion And Platform Health Queries

| Panel | PromQL contract |
| --- | --- |
| Ingestion request rate | `sum(rate(tokenobs_ingestion_requests_total{environment=~"$environment",region=~"$region"}[$__rate_interval]))` |
| Accepted versus rejected telemetry | `sum by (result) (increase(tokenobs_ingestion_requests_total{environment=~"$environment",region=~"$region"}[$__range]))` |
| Ingestion latency p95 | `histogram_quantile(0.95, sum by (le) (rate(tokenobs_ingestion_request_duration_seconds_bucket{environment=~"$environment",region=~"$region"}[$__rate_interval])))` |
| Payload normalization failures | `sum by (failure_reason) (increase(tokenobs_payload_normalization_failures_total{environment=~"$environment",region=~"$region"}[$__range]))` |
| Background job failures | `sum by (job_type) (increase(tokenobs_background_jobs_total{environment=~"$environment",region=~"$region",result="failed"}[$__range]))` |
| Redaction failure or review-required rate | `sum by (result) (rate(tokenobs_redaction_events_total{environment=~"$environment",region=~"$region",result=~"redaction_failed|review_required"}[$__rate_interval]))` |
| Product API error rate | `sum(rate(tokenobs_product_api_requests_total{environment=~"$environment",region=~"$region",status_class=~"4xx|5xx"}[$__rate_interval])) / sum(rate(tokenobs_product_api_requests_total{environment=~"$environment",region=~"$region"}[$__rate_interval]))` |
| Container Apps ready replicas | `sum by (container_app) (tokenobs_container_app_replicas_ready{environment=~"$environment",region=~"$region"})` |
| Container Apps desired replicas | `sum by (container_app) (tokenobs_container_app_replicas_desired{environment=~"$environment",region=~"$region"})` |

## Acceptance Criteria

- Product services and jobs emit or derive every metric listed in this contract before first-release Grafana dashboards are marked complete.
- Metric labels stay within the allowed label sets.
- No metric label contains individual developer identity, session ID, trace ID, span ID, credential ID, raw repository path, file path, prompt text, command output, tool result, content hash, IP address, or user-agent string.
- Dashboard JSON uses the PromQL contracts in this document or documents a reviewed replacement query.
- Counter panels use `rate()` for per-second trends and `increase()` for selected-range totals.
- Histogram percentile panels use `histogram_quantile()` over `rate()` of histogram buckets.

## Verified Platform Facts

- Prometheus naming guidance recommends unit suffixes and `_total` for accumulating counters: https://prometheus.io/docs/practices/naming/
- Prometheus data model guidance defines metric names and labels and reserves labels beginning with `__` for internal use: https://prometheus.io/docs/concepts/data_model/
- Prometheus documents `rate()`, `increase()`, and `histogram_quantile()` query functions: https://prometheus.io/docs/prometheus/latest/querying/functions/
- OpenTelemetry metric instruments have a name, kind, unit, and description: https://opentelemetry.io/docs/concepts/signals/metrics/
- Azure Monitor managed service for Prometheus is a managed Prometheus-compatible metrics service: https://learn.microsoft.com/en-us/azure/azure-monitor/metrics/prometheus-metrics-overview
