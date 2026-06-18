# Copilot OTel Field Mapping

Status: historical Phase 0 reference. This document is not the Azure Production MVP ingestion contract.

For the Azure Production MVP, Codex CLI is the first production harness and [codex-production-ingestion-contract.md](../../architecture/codex-production-ingestion-contract.md) is the implementation source of truth. This Copilot mapping remains useful only as prior evidence for future VS Code Copilot adapter work.

## Purpose

Define how the Local-First MVP maps fixture-observed VS Code Copilot OpenTelemetry records into the normalized model before parser behavior is treated as stable.

This document is based on `.local/copilot-otel.jsonl`, captured from VS Code Copilot with file export enabled and content capture disabled. The fixture is ignored by git and must remain local.

## Fixture Summary

Observed fixture shape:

* The fixture may grow while VS Code continues exporting telemetry, so exact record counts are not part of the parser contract.
* Empty objects are present and are ignored by Direct File Import.
* Log records have `_body`, `attributes`, optional `spanContext`, `resource`, and `instrumentationScope`.
* Metric records have `resource` and `scopeMetrics`.
* Resource-only objects may appear and are treated as resource metadata only.

Observed resource fields:

| Source field | Example value | Normalized target | Category | Required | Metric status behavior | Confidence | Metadata-only allowed | Privacy handling |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `resource._asyncAttributesPending` | `false` | None | fixture-observed | Nullable | Not a token metric | Observed | Yes | Ignore; exporter internal state |

Observed resource attributes:

| Source field | Example value | Normalized target | Category | Required | Metric status behavior | Confidence | Metadata-only allowed | Privacy handling |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `resource._rawAttributes[].deployment.environment` | `local` | `telemetry_import.environment_name` | fixture-observed | Nullable | Not a token metric | Observed | Yes | Store raw value; low sensitivity |
| `resource._rawAttributes[].service.name` | `github-copilot-vscode` | `agent_session.harness_source` | documented | Required for Copilot fixture | Not a token metric | Observed | Yes | Store raw service identity; normalized `agent_session.harness` remains `copilot` |
| `resource._rawAttributes[].service.version` | `0.51.0` | `agent_session.harness_version` | documented | Nullable | Not a token metric | Observed | Yes | Store raw value |
| `resource._rawAttributes[].session.id` | `<session-id>` | `agent_session.provider_session_id_hash` | fixture-observed | Required when present | Not a token metric | Observed | Yes | Hash before storing; raw provider session ids are not persisted by default |
| `resource._rawAttributes[].team` | `<team-label>` | `agent_session.team_hash` | fixture-observed | Nullable | Not a token metric | Observed | Yes | Hash before storing; raw team labels are not persisted by default |

## Record Classification

| Record shape | Parser behavior | Normalized target |
| --- | --- | --- |
| `{}` | Count as skipped empty record; do not fail import | `telemetry_import.skipped_record_count` |
| Object with `_body` | Treat as log event | `agent_turn`, `model_invocation`, or `tool_call` depending on `attributes.event.name` |
| Object with `scopeMetrics` | Treat as metric export | `metric_observation` |
| Object with only `resource` | Treat as resource metadata | `telemetry_import` and `agent_session` enrichment |

Malformed JSON remains a fatal or skipped-record parser concern for implementation, but no malformed JSON was observed in this fixture.

## Log Record Fields

Log record common fields:

| Source field | Example value | Normalized target | Category | Required | Metric status behavior | Confidence | Metadata-only allowed | Privacy handling |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `_body` | `copilot_chat.agent.turn: 0` | `telemetry_record.body_redacted_summary` | fixture-observed | Nullable | Not a token metric | Observed | Yes | Do not persist raw body text; derive an event-like redacted summary only |
| `_isReadonly` | `true` | None | fixture-observed | Nullable | Not a token metric | Observed | Yes | Ignore |
| `_logRecordLimits.attributeCountLimit` | numeric limit | None | fixture-observed | Nullable | Not a token metric | Observed | Yes | Ignore |
| `_logRecordLimits.attributeValueLengthLimit` | numeric limit | None | fixture-observed | Nullable | Not a token metric | Observed | Yes | Ignore |
| `hrTime` | timestamp tuple | `telemetry_record.observed_at_utc` | fixture-observed | Required when present | Not a token metric | Observed | Yes | Convert to UTC timestamp |
| `hrTimeObserved` | timestamp tuple | `telemetry_record.received_at_utc` | fixture-observed | Nullable | Not a token metric | Observed | Yes | Convert to UTC timestamp |
| `instrumentationScope.name` | `github-copilot-vscode` | `telemetry_record.instrumentation_scope_name` | documented | Nullable | Not a token metric | Observed | Yes | Store raw value |
| `instrumentationScope.version` | `0.51.0` | `telemetry_record.instrumentation_scope_version` | documented | Nullable | Not a token metric | Observed | Yes | Store raw value |
| `spanContext.traceId` | `<trace-id>` | `telemetry_record.trace_id_hash` | documented | Nullable | Not a token metric | Observed | Yes | Hash before storing; raw trace ids are not persisted by default |
| `spanContext.spanId` | `<span-id>` | `telemetry_record.span_id_hash` | documented | Nullable | Not a token metric | Observed | Yes | Hash before storing; raw span ids are not persisted by default |
| `spanContext.traceFlags` | numeric flags | `telemetry_record.trace_flags` | documented | Nullable | Not a token metric | Observed | Yes | Store raw numeric value |
| `totalAttributesCount` | numeric count | `telemetry_record.attribute_count` | fixture-observed | Nullable | Not a token metric | Observed | Yes | Store raw numeric value |

Log record attributes:

| Source field | Example value | Normalized target | Category | Required | Metric status behavior | Confidence | Metadata-only allowed | Privacy handling |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `attributes.event.name` | `gen_ai.client.inference.operation.details` | `telemetry_record.event_name` | fixture-observed | Required for log classification | Not a token metric | Observed | Yes | Store raw value |
| `attributes.gen_ai.agent.name` | `GitHub Copilot Chat` | `agent_session.agent_name` | fixture-observed | Nullable | Not a token metric | Observed | Yes | Store raw value |
| `attributes.gen_ai.operation.name` | `chat` | `model_invocation.operation_name` | documented | Nullable | Not a token metric | Observed | Yes | Store raw value |
| `attributes.gen_ai.request.model` | `gpt-5.4` | `model_invocation.request_model` | documented | Nullable | Not a token metric | Observed | Yes | Store raw value |
| `attributes.gen_ai.request.max_tokens` | numeric max token setting | `model_invocation.request_max_tokens` | documented | Nullable | Not a usage metric | Observed | Yes | Store raw numeric value |
| `attributes.gen_ai.request.temperature` | numeric temperature | `model_invocation.request_temperature` | documented | Nullable | Not a usage metric | Observed | Yes | Store raw numeric value |
| `attributes.gen_ai.response.id` | `<response-id>` | `model_invocation.provider_response_id_hash` | documented | Nullable | Not a token metric | Observed | Yes | Hash before storing; raw provider response ids are not persisted by default |
| `attributes.gen_ai.response.model` | `gpt-5.4-2026-03-05` | `model_invocation.response_model` | documented | Nullable | Not a token metric | Observed | Yes | Store raw value |
| `attributes.gen_ai.response.finish_reasons` | array of finish reasons | `model_invocation.finish_reasons_json` | documented | Nullable | Not a token metric | Observed | Yes | Store array as JSON |
| `attributes.gen_ai.usage.input_tokens` | numeric token count | `model_invocation.input_tokens` and `token_metric.value` | documented | Nullable | `observed` when present; `unavailable` when absent | Observed | Yes | Store numeric value |
| `attributes.gen_ai.usage.output_tokens` | numeric token count | `model_invocation.output_tokens` and `token_metric.value` | documented | Nullable | `observed` when present; `unavailable` when absent | Observed | Yes | Store numeric value |
| `attributes.gen_ai.tool.name` | `read_file` | `tool_call.tool_name` | fixture-observed | Nullable | Not a token metric | Observed | Yes | Store tool name only; do not store tool arguments by default |
| `attributes.session.id` | `<session-id>` | `agent_session.provider_session_id_hash` | fixture-observed | Nullable | Not a token metric | Observed | Yes | Hash before storing; use only for correlation when resource session id is absent or matches |
| `attributes.duration_ms` | numeric duration | `tool_call.duration_ms` | fixture-observed | Nullable | Not a token metric | Observed | Yes | Store numeric value |
| `attributes.success` | boolean success flag | `tool_call.success` or `agent_turn.success` | fixture-observed | Nullable | Not a token metric | Observed | Yes | Store boolean |
| `attributes.turn.index` | numeric turn index | `agent_turn.turn_index` | fixture-observed | Nullable | Not a token metric | Observed | Yes | Store numeric value |
| `attributes.tool_call_count` | numeric count | `agent_turn.tool_call_count` | fixture-observed | Nullable | Not a token metric | Observed | Yes | Store numeric value |

Observed log events:

| Event name | Observed body pattern | Normalized interpretation |
| --- | --- | --- |
| `copilot_chat.session.start` | `copilot_chat.session.start` | Start or identify an `agent_session` |
| `copilot_chat.agent.turn` | `copilot_chat.agent.turn: <index>` | Create or update an `agent_turn` |
| `copilot_chat.tool.call` | `copilot_chat.tool.call: <tool>` | Create a `tool_call` |
| `gen_ai.client.inference.operation.details` | `GenAI inference: <model>` | Create a `model_invocation` and observed token metrics |

## Metric Record Fields

Metric record common fields:

| Source field | Example value | Normalized target | Category | Required | Metric status behavior | Confidence | Metadata-only allowed | Privacy handling |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `scopeMetrics[].scope.name` | `github-copilot-vscode` | `metric_observation.scope_name` | documented | Nullable | Not a token metric | Observed | Yes | Store raw value |
| `scopeMetrics[].scope.version` | `0.51.0` | `metric_observation.scope_version` | documented | Nullable | Not a token metric | Observed | Yes | Store raw value |
| `scopeMetrics[].metrics[].descriptor.name` | `gen_ai.client.token.usage` | `metric_observation.metric_name` | documented | Required for metric records | Depends on metric | Observed | Yes | Store raw value |
| `scopeMetrics[].metrics[].descriptor.type` | `HISTOGRAM` | `metric_observation.metric_type` | documented | Required for metric records | Depends on metric | Observed | Yes | Store raw value |
| `scopeMetrics[].metrics[].descriptor.valueType` | numeric enum value | `metric_observation.value_type` | fixture-observed | Nullable | Depends on metric | Observed | Yes | Store raw value |
| `scopeMetrics[].metrics[].descriptor.unit` | optional unit | `metric_observation.unit` | documented | Nullable | Depends on metric | Observed | Yes | Store raw value |
| `scopeMetrics[].metrics[].descriptor.description` | optional description | `metric_observation.description` | documented | Nullable | Depends on metric | Observed | Yes | Store raw value |
| `scopeMetrics[].metrics[].descriptor.advice.explicitBucketBoundaries` | numeric boundary array | `metric_observation.bucket_boundaries_json` | documented | Nullable | Depends on metric | Observed | Yes | Store array as JSON |
| `scopeMetrics[].metrics[].aggregationTemporality` | numeric enum value | `metric_observation.aggregation_temporality` | documented | Nullable | Depends on metric | Observed | Yes | Store raw value |
| `scopeMetrics[].metrics[].isMonotonic` | boolean | `metric_observation.is_monotonic` | documented | Nullable | Depends on metric | Observed | Yes | Store boolean |
| `scopeMetrics[].metrics[].dataPointType` | numeric enum value | `metric_observation.data_point_type` | fixture-observed | Nullable | Depends on metric | Observed | Yes | Store raw value |
| `scopeMetrics[].metrics[].dataPoints[].startTime` | timestamp tuple | `metric_observation.start_time_utc` | documented | Nullable | Depends on metric | Observed | Yes | Convert to UTC timestamp |
| `scopeMetrics[].metrics[].dataPoints[].endTime` | timestamp tuple | `metric_observation.end_time_utc` | documented | Nullable | Depends on metric | Observed | Yes | Convert to UTC timestamp |
| `scopeMetrics[].metrics[].dataPoints[].attributes.gen_ai.agent.name` | agent label | `metric_observation.attributes_json` | fixture-observed | Nullable | Depends on metric | Observed | Yes | Store metadata dimension; consider allowlist or hash if cardinality grows |
| `scopeMetrics[].metrics[].dataPoints[].attributes.gen_ai.operation.name` | `chat` | `metric_observation.attributes_json` | documented | Nullable | Depends on metric | Observed | Yes | Store metadata dimension |
| `scopeMetrics[].metrics[].dataPoints[].attributes.gen_ai.provider.name` | `github` | `metric_observation.attributes_json` | documented | Nullable | Depends on metric | Observed | Yes | Store metadata dimension |
| `scopeMetrics[].metrics[].dataPoints[].attributes.gen_ai.request.model` | model name | `metric_observation.attributes_json` | documented | Nullable | Depends on metric | Observed | Yes | Store metadata dimension |
| `scopeMetrics[].metrics[].dataPoints[].attributes.gen_ai.response.model` | model name | `metric_observation.attributes_json` | documented | Nullable | Depends on metric | Observed | Yes | Store metadata dimension |
| `scopeMetrics[].metrics[].dataPoints[].attributes.gen_ai.token.type` | `input` or `output` | `metric_observation.attributes_json`; optional aggregate `token_metric.metric_name` | documented | Nullable | Determines aggregate token direction | Observed | Yes | Store metadata dimension |
| `scopeMetrics[].metrics[].dataPoints[].attributes.gen_ai.tool.name` | tool name | `metric_observation.attributes_json` | fixture-observed | Nullable | Depends on metric | Observed | Yes | Store tool name only; do not store arguments or results |
| `scopeMetrics[].metrics[].dataPoints[].attributes.success` | boolean success flag | `metric_observation.attributes_json` | fixture-observed | Nullable | Depends on metric | Observed | Yes | Store boolean dimension |
| `scopeMetrics[].metrics[].dataPoints[].value` | scalar value | `metric_observation.value_json` | documented | Required for scalar data points | `observed` when present | Observed | Yes | Store numeric value |
| `scopeMetrics[].metrics[].dataPoints[].value.count` | histogram sample count | `metric_observation.value_json` | documented | Required for histogram data points | `observed` when present | Observed | Yes | Store numeric aggregate |
| `scopeMetrics[].metrics[].dataPoints[].value.sum` | histogram sample sum | `metric_observation.value_json` | documented | Nullable | `observed` when present | Observed | Yes | Store numeric aggregate |
| `scopeMetrics[].metrics[].dataPoints[].value.min` | histogram minimum | `metric_observation.value_json` | documented | Nullable | `observed` when present | Observed | Yes | Store numeric aggregate |
| `scopeMetrics[].metrics[].dataPoints[].value.max` | histogram maximum | `metric_observation.value_json` | documented | Nullable | `observed` when present | Observed | Yes | Store numeric aggregate |
| `scopeMetrics[].metrics[].dataPoints[].value.buckets.boundaries` | histogram bucket boundaries | `metric_observation.bucket_boundaries_json` | documented | Nullable | `observed` when present | Observed | Yes | Store numeric boundary array |
| `scopeMetrics[].metrics[].dataPoints[].value.buckets.counts` | histogram bucket counts | `metric_observation.value_json` | documented | Nullable | `observed` when present | Observed | Yes | Store numeric count array |

Observed metrics:

| Metric name | Type | Key dimensions | Normalized use |
| --- | --- | --- | --- |
| `gen_ai.client.token.usage` | Histogram | `gen_ai.token.type`, model/provider/operation dimensions | Aggregate token usage metric. Use for metric observations and cross-checking token totals; do not treat as the only source for per-invocation token records. |
| `gen_ai.client.operation.duration` | Histogram | model/provider/operation dimensions | Aggregate model operation duration metric. |
| `copilot_chat.time_to_first_token` | Histogram | model/provider/operation dimensions | Aggregate response latency metric. |
| `copilot_chat.session.count` | Counter | none observed | Session counter metric. |
| `copilot_chat.agent.turn.count` | Histogram | agent/model dimensions | Aggregate turn count metric. |
| `copilot_chat.agent.invocation.duration` | Histogram | agent/model dimensions | Aggregate agent invocation duration metric. |
| `copilot_chat.tool.call.count` | Counter | tool name and success dimensions | Aggregate tool-call count metric. |
| `copilot_chat.tool.call.duration` | Histogram | tool name and success dimensions | Aggregate tool-call duration metric. |

## Token Metric Semantics

Primary MVP token metric source:

* Per-invocation observed token counts come from log attributes `gen_ai.usage.input_tokens` and `gen_ai.usage.output_tokens`.
* Aggregate token usage metrics from `gen_ai.client.token.usage` are persisted as `metric_observation` and can be used for cross-checking, trend charts, or later Collector ingestion.
* The current fixture observes `gen_ai.provider.name` only on metric datapoints, not on model-invocation log records. Direct File Import must leave `model_invocation.provider_name` NULL unless a future log field or a deterministic correlation rule proves the value for that invocation.

Missing metric behavior:

* If `gen_ai.usage.input_tokens` is absent for a model invocation, store `model_invocation.input_tokens = NULL` and a `token_metric` row with `metric_name = input_tokens`, `metric_status = unavailable`, `metric_confidence = unavailable`, and `source = missing_log_attribute`.
* If `gen_ai.usage.output_tokens` is absent, apply the same rule for output tokens.
* Never store unavailable usage metrics as `0`.
* A total is `observed` when both input and output are observed, `estimated` when values are estimated by a future estimator, `mixed` when observed and estimated values are combined, and `unavailable` when no token total can be proven.

## Parser Gates

Parser behavior is stable enough for issue #3 only when:

* Empty objects are skipped and counted.
* Resource attributes are parsed and applied to sessions or import metadata.
* Log records create session, turn, tool-call, and model-invocation records where their event names and attributes support it.
* Metric records create `metric_observation` rows without pretending aggregate metrics are per-turn facts.
* All usage metrics absent from a record are represented as `NULL` plus explicit metric status, never zero.
* Metadata-only privacy defaults are enforced at parse time and storage time.
