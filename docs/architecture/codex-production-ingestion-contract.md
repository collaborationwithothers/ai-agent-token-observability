# Codex Production Ingestion Contract

## Purpose

This document defines the Azure Production MVP ingestion contract for Codex CLI telemetry.

The contract describes how Codex telemetry reaches the Product Ingestion Endpoint, how the product authenticates it, how tenant and developer identity are resolved, how content-bearing fields are controlled, and how normalized signals are routed to Azure backends.

## Source Documents

- [Production Target State Spec](../specs/production-target-state.md)
- [Azure Production MVP PRD](../prd/azure-production-mvp.md)
- [Azure Production Architecture](./azure-production-architecture.md)
- [Content Capture And Redaction Architecture](./content-capture-and-redaction.md)
- [Identity And Authorization Architecture](./identity-and-authorization.md)
- [Recommendation Engine Architecture](./recommendation-engine.md)

## Core Decisions

- Codex CLI is the Production MVP Harness.
- Codex telemetry setup is manual per developer.
- Codex OpenTelemetry export is opt in, not assumed.
- The first-release public ingestion transport is OTLP/HTTP.
- OTLP/gRPC is not part of the public Front Door path in the Azure Production MVP.
- The Product Ingestion Endpoint is the authoritative ingestion boundary.
- Direct-to-monitor-only ingestion is not authoritative.
- Scoped Ingestion Credential identity is authoritative for upload and session ownership.
- Harness-emitted identity is evidence, not access-control authority.
- Content-bearing fields must pass Content Capture Policy and Pre-Storage Content Redaction Pipeline before durable captured-content storage.
- Product records must preserve metric evidence state: observed, derived, estimated, unavailable, not applicable, or mixed.

## Codex Telemetry Source

Codex supports opt-in OpenTelemetry export. For the MVP, the product treats Codex telemetry as harness-emitted Agent Telemetry Signals, not as a product-owned instrumentation library.

Codex documented telemetry includes structured log events for runs and tool usage. Representative event types include:

- `codex.conversation_starts`.
- `codex.api_request`.
- `codex.sse_event`.
- `codex.websocket_request`.
- `codex.websocket_event`.
- `codex.user_prompt`.
- `codex.tool_decision`.
- `codex.tool_result`.

Codex also documents trace and metrics exporter configuration keys. The Product Ingestion Endpoint therefore reserves paths for logs, traces, and metrics, even if logs or events are the richest first MVP source.

## Transport Contract

### MVP Transport

The Azure Production MVP supports OTLP/HTTP over HTTPS.

Required public endpoints:

```text
POST https://{ingestionHost}/v1/logs
POST https://{ingestionHost}/v1/traces
POST https://{ingestionHost}/v1/metrics
```

Required encoding:

- OTLP/HTTP protobuf binary for production.
- OTLP/HTTP JSON only for diagnostics or support-approved troubleshooting.

OTLP/gRPC is not exposed on the first-release public edge because Azure Front Door does not support gRPC to origins. A future gRPC path would require a separate ingress design or a changed edge decision.

### Customer-Side Collector

A customer-side OpenTelemetry Collector is optional.

When present, it may receive telemetry from Codex and forward to the Product Ingestion Endpoint using the same OTLP/HTTP contract.

The collector is not allowed to become the product authorization boundary. The product still validates:

- Scoped Ingestion Credential.
- Customer Organization.
- Harness setup profile.
- Schema version.
- Content Capture Policy.
- Data residency region.

## Manual Codex Setup Profile

The product generates a Codex setup profile for each developer and harness setup profile.

The setup profile contains:

- Ingestion host.
- Environment label.
- Harness name.
- Harness setup profile ID.
- Scoped Ingestion Credential reference.
- Exporter configuration.
- Content capture setting.
- Schema version.
- Validation command or test event guidance.

Representative Codex configuration:

```toml
[otel]
environment = "pd"
log_user_prompt = false

exporter = { otlp-http = {
  endpoint = "https://ingest.example.com/v1/logs",
  protocol = "binary",
  headers = {
    "authorization" = "Bearer ${AITO_INGESTION_TOKEN}",
    "x-aito-harness" = "codex-cli",
    "x-aito-setup-profile-id" = "profile_123",
    "x-aito-schema-version" = "2026-06-01"
  }
}}

trace_exporter = { otlp-http = {
  endpoint = "https://ingest.example.com/v1/traces",
  protocol = "binary",
  headers = {
    "authorization" = "Bearer ${AITO_INGESTION_TOKEN}",
    "x-aito-harness" = "codex-cli",
    "x-aito-setup-profile-id" = "profile_123",
    "x-aito-schema-version" = "2026-06-01"
  }
}}

metrics_exporter = { otlp-http = {
  endpoint = "https://ingest.example.com/v1/metrics",
  protocol = "binary",
  headers = {
    "authorization" = "Bearer ${AITO_INGESTION_TOKEN}",
    "x-aito-harness" = "codex-cli",
    "x-aito-setup-profile-id" = "profile_123",
    "x-aito-schema-version" = "2026-06-01"
  }
}}
```

The tenant is derived from the credential and setup profile. Tenant headers are allowed only as correlation hints and must not override credential-derived tenant identity.

## Authentication Contract

Every ingestion request must include a Scoped Ingestion Credential.

MVP credential transport:

```text
Authorization: Bearer {scopedIngestionCredential}
```

The product validates:

- Credential exists.
- Credential is active.
- Credential is not expired.
- Credential is not revoked.
- Credential belongs to the expected Customer Organization.
- Credential belongs to the expected developer.
- Credential belongs to the expected harness setup profile.
- Credential allows the declared harness.
- Credential allows the declared environment and region.

Failed validation is rejected before parsing content-bearing telemetry fields.

## Harness Telemetry Envelope

Accepted records are normalized into a Harness Telemetry Envelope.

Envelope fields:

- Envelope ID.
- Customer Organization.
- Customer Data Residency Region.
- Harness: `codex-cli`.
- Harness setup profile.
- Scoped Ingestion Credential.
- Credential-derived developer identity.
- Harness-emitted identity when present.
- Schema version.
- Signal type: log, trace, metric.
- Source event name.
- Source event timestamp.
- Conversation or session identifier.
- Turn identifier when available.
- Event identifier or deduplication key.
- Model identifier when emitted.
- Sandbox and approval settings when emitted.
- Repository evidence when emitted.
- Content policy decision.
- Content capture state.
- Redaction state.
- Routing decision.
- Evidence state.

The envelope is product metadata. It is not a raw OTLP storage format.

## Signal Contract

### Logs And Events

Logs and events are the primary MVP source for session reconstruction.

Accepted event families:

| Event family | Use |
| --- | --- |
| Conversation start | Session creation, model, environment, sandbox, approval policy |
| API request | Request duration, attempt, status, errors |
| SSE event | Stream lifecycle, token counts when emitted, completion evidence |
| WebSocket request or event | App-server and remote-session evidence when emitted |
| User prompt | Prompt length and optional content candidate |
| Tool decision | Approval or denial evidence |
| Tool result | Tool duration, success, bounded output evidence |

The product must tolerate missing event families because Codex versions and user settings may differ.

### Traces

Traces are accepted when Codex emits them or when a customer-side collector forwards them.

Trace records support:

- Session timeline.
- Turn boundaries.
- API request timing.
- Tool execution timing.
- Recommendation evidence correlation.
- Cache diagnostics correlation.

If trace spans are unavailable, the product may derive a session timeline from logs or events and mark the trace evidence state as `derived` or `unavailable`.

### Metrics

Metrics are accepted when Codex emits them or when a collector forwards them.

Metric records support:

- Token counters.
- Cached-token counters.
- Request counts.
- Error counts.
- Duration histograms.
- Tool-use counts.
- Estimated cost gauges or derived cost metrics.

If Codex emits token counts only as event fields, the product derives aggregate metrics and marks the metric evidence state as `derived`.

## Token And Cost Fields

The product normalizes token and cost fields into product metadata.

Token fields:

- Input tokens.
- Output tokens.
- Cached input tokens.
- Reasoning output tokens.
- Total tokens.
- Token evidence state.

Cost fields:

- Pricing basis ID.
- Pricing version.
- Provider.
- Model.
- Billing route.
- Input token unit price.
- Output token unit price.
- Cached token unit price when applicable.
- Estimated cost.
- Cost evidence state.

Unavailable token values are stored as null, not zero.

Estimated cost is never treated as provider-billed fact unless the evidence source supports that claim.

## Content Candidate Contract

Content candidates may appear in:

- Prompt-related events.
- Tool inputs.
- Tool outputs.
- Error summaries.
- Command output snippets.
- Model response excerpts.

Content capture requires all of:

- Codex emitted the content or content-bearing field.
- Developer setup profile allows content capture.
- Customer Organization Content Capture Policy allows the content class.
- The candidate passes policy pre-check.
- The candidate passes the Pre-Storage Content Redaction Pipeline.
- The Redaction Failure Gate allows storage.

If `log_user_prompt = false`, the product should expect prompt length metadata but not raw prompt text from Codex.

If `log_user_prompt = true`, prompt content is still not stored unless product policy and redaction allow it.

Raw content must not be written to PostgreSQL, Blob Storage, Log Analytics, Application Insights, or any durable queue before the redaction decision.

## Normalized Session Model

The ingestion service creates or updates a normalized session from accepted envelopes.

Session fields:

- Customer Organization.
- Developer.
- Harness.
- Harness setup profile.
- Codex conversation ID.
- Session start timestamp.
- Session end timestamp when known.
- Model sequence.
- Repository evidence state.
- Environment.
- Sandbox setting.
- Approval setting.
- Token summary.
- Cost summary.
- Cache summary.
- Tool summary.
- Error summary.
- Content capture summary.
- Recommendation status.

Session records can be incomplete while ingestion is in progress.

## Repository Attribution

Repository attribution is accepted only as telemetry evidence.

Accepted evidence:

- Harness-emitted repository path or identifier.
- Setup-profile repository scope.
- Customer-connected source provider metadata.
- Correlated repository discovery result.

The product must not silently inspect local Git configuration or unrelated developer files to fill repository identity.

Unmatched repository evidence creates an unmatched telemetry candidate for Repository Discovery and Enrollment.

## Routing Contract

Signal Routing Policy determines where each normalized record goes.

| Destination | Receives |
| --- | --- |
| Managed Prometheus or Azure Monitor workspace | Aggregate metrics and derived metrics for Grafana |
| Application Insights or Log Analytics | Redacted traces, logs, events, and diagnostic correlation |
| PostgreSQL | Customer Organization, users, sessions, envelopes, hotspots, recommendations, pricing, policies, content references, audit metadata |
| Blob Storage | Redacted Captured Content Blob only |
| Audit log | Security, credential, policy, content, recommendation, pricing, export, and rejection decisions |

Raw OTLP payloads are not a product system of record.

Product aggregate metric names, labels, and PromQL query contracts are defined in [aggregate-metrics-contract.md](./aggregate-metrics-contract.md). The Product Ingestion Endpoint and product jobs must emit or derive those metrics after tenant, credential, schema, policy, and content routing decisions are complete.

## Rejection Contract

Reject before durable storage when:

- Credential is missing, unknown, expired, revoked, or outside scope.
- Tenant cannot be derived from the credential.
- Setup profile is unknown or disabled.
- Harness is not allowed.
- Schema version is unsupported.
- Payload exceeds configured size limits.
- Content-bearing fields cannot be safely classified.
- Data residency region does not match the endpoint or profile.
- Rate limit is exceeded.

Recommended HTTP responses:

| Condition | Response |
| --- | --- |
| Missing or invalid credential | `401` |
| Credential valid but outside scope | `403` |
| Unsupported schema or malformed OTLP | `400` |
| Payload too large | `413` |
| Rate limited | `429` |
| Transient ingestion failure | `503` |

Rejected requests create audit or diagnostic records without storing content-bearing payloads.

## Deduplication And Ordering

Ingestion must be idempotent.

Deduplication key inputs:

- Customer Organization.
- Harness setup profile.
- Credential ID.
- Signal type.
- Conversation ID.
- Turn ID when present.
- Source event name.
- Source event timestamp.
- Source event ID when present.
- Stable body hash of non-content metadata.

Events may arrive out of order. Session reconstruction must tolerate late arrivals and update derived summaries asynchronously.

## Schema Versioning

The product schema version is independent from the Codex version.

The MVP schema version format is date based:

```text
YYYY-MM-DD
```

Schema rules:

- Additive fields are allowed within a schema version.
- Meaning changes require a new schema version.
- Removed or renamed fields require a new schema version.
- Unsupported versions fail closed.
- The setup profile pins the schema version.
- The ingestion service records the observed Codex CLI version when emitted.

## Security Requirements

- HTTPS only.
- Front Door WAF and rate limiting before the Product Ingestion Endpoint.
- Credential-derived tenant identity.
- No anonymous ingestion.
- No shared tenant upload key.
- No raw content durable storage before redaction.
- Data-store access over private networking.
- Managed identities for service-to-service Azure access.
- Audit all credential lifecycle and suspicious ingestion decisions.

## MVP Acceptance Criteria

The Azure Production MVP ingestion contract is satisfied when:

- A PlatformAdmin can create a Codex CLI setup profile.
- A developer can manually configure Codex CLI OTel export with a Scoped Ingestion Credential.
- The Product Ingestion Endpoint accepts OTLP/HTTP logs from Codex.
- The endpoint rejects invalid credentials and unsupported schemas.
- Accepted telemetry creates a normalized session.
- Token metrics are stored as observed, derived, estimated, unavailable, not applicable, or mixed.
- Unavailable token metrics are null, not zero.
- Aggregate token and cost metrics can be visualized in Managed Grafana.
- Session drill-down can find the normalized session in the Product Dashboard.
- Content candidates are blocked, redacted, or stored according to Content Capture Policy.
- Raw content is not stored before redaction.
- Audit records exist for security-sensitive ingestion decisions.

## Target-State Additions

The Multi-Tenant SaaS Target State adds:

- Codex desktop app support after telemetry parity is validated.
- VS Code Copilot and Claude Code harness contracts.
- Multiple Customer Organizations.
- Multiple Identity Tenants per Customer Organization.
- Optional customer-side collector templates.
- Optional dedicated ingestion endpoints for high-volume customers.
- More granular data residency routing.
- Wider trace and metric normalization if harnesses emit richer signals.
- Contract test suites per harness and Codex version.

## Verified Platform Facts

- Codex supports opt-in OpenTelemetry export and documents OTel log export for API requests, SSE/events, prompts, tool approvals, and tool results: https://developers.openai.com/codex/config-advanced#observability-and-telemetry
- Codex configuration documents OTel log, trace, and metrics exporter settings: https://developers.openai.com/codex/config-reference
- OpenTelemetry Protocol defines OTLP over gRPC and HTTP transports: https://opentelemetry.io/docs/specs/otlp/
- OpenTelemetry logs include resource context for correlation with traces and metrics: https://opentelemetry.io/docs/specs/otel/logs/
- OpenTelemetry metrics data model is designed for pre-aggregated metric time series: https://opentelemetry.io/docs/specs/otel/metrics/data-model/
- OpenTelemetry GenAI semantic conventions define attributes, metrics, spans, and events for generative AI systems: https://opentelemetry.io/docs/specs/semconv/gen-ai/gen-ai-events/
- Azure Front Door does not support gRPC to origins: https://learn.microsoft.com/en-us/azure/frontdoor/front-door-faq#does-azure-front-door-support-grpc
- Azure Monitor supports OpenTelemetry logs, metrics, and traces through OTLP ingestion paths and routes metrics to Azure Monitor workspace and logs or traces to Log Analytics: https://learn.microsoft.com/en-us/azure/azure-monitor/containers/opentelemetry-options
