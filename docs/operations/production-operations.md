# Production Operations

## Purpose

This document defines the Day-1 Operable Baseline for the Azure Production MVP.

It is not a mature SRE program, an external SLA, or a 24/7 operations contract. It defines the minimum operational behavior required before first production use: health checks, probes, internal SLOs, alerts, backup validation, lifecycle validation, audit export, and incident runbooks.

## Source Documents

- [Azure Production Architecture](../architecture/azure-production-architecture.md)
- [Runtime Service Topology](../architecture/runtime-topology.md)
- [Product API Contract](../architecture/product-api-contract.md)
- [Content Capture And Redaction Architecture](../architecture/content-capture-and-redaction.md)
- [Recommendation Engine Architecture](../architecture/recommendation-engine.md)
- [Terraform Production Infrastructure](../architecture/terraform-production-infrastructure.md)
- [Edge Origin Validation](./edge-origin-validation.md)

## Decision

The first release targets a Day-1 Operable Baseline.

This means:

- Product services must expose health and readiness behavior.
- Azure Container Apps must use liveness and readiness probes.
- Critical workflows must have measurable internal SLOs.
- Azure Monitor alerts must exist for the first-release failure modes.
- Alert notifications must go to private operational channels, not public GitHub issues.
- Data protection must be proven by a non-production PostgreSQL restore drill and Blob lifecycle validation.
- Operators must have runbooks for the most likely first-release incidents.

## Health And Readiness

System routes are defined in [Product API Contract](../architecture/product-api-contract.md):

| Route | Purpose | Auth expectation |
| --- | --- | --- |
| `GET /api/v1/system/health` | Process liveness | No tenant context required |
| `GET /api/v1/system/readiness` | Dependency readiness | Protected or restricted to operational callers |

Implementation may also expose platform probe aliases when useful for Azure Container Apps and Front Door:

| Alias | Maps to | Purpose |
| --- | --- | --- |
| `GET /healthz` | `GET /api/v1/system/health` | Simple liveness probe path |
| `GET /readyz` | `GET /api/v1/system/readiness` | Simple readiness or origin-health path |

Rules:

- Liveness must not call PostgreSQL, Blob Storage, Azure Monitor, AI services, or external model providers.
- Liveness must return whether the process can accept basic HTTP requests.
- Readiness must check dependencies needed by the specific service.
- Readiness must fail closed when required configuration, tenant routing, database access, content store access, or telemetry backend access is unavailable.
- Readiness output must not expose connection strings, keys, tokens, tenant secrets, raw exception messages, or private endpoint details.
- Readiness detail may be available only to operational callers or internal probes.

Service-specific readiness:

| Service | Required readiness checks |
| --- | --- |
| Product Dashboard | Product API reachable, auth configuration loaded, static asset build/version available |
| Product API | PostgreSQL reachable, Blob Storage reachable, telemetry query backends reachable where required, recommendation dependencies configured |
| Product Ingestion Endpoint | PostgreSQL reachable, telemetry write path available, ingestion credential validation store reachable, content policy lookup available |

## Container App Probes

Each long-running Container App must define probes in Terraform.

Minimum probe model:

| Probe | Target | Required behavior |
| --- | --- | --- |
| Startup | Service port | Allows normal cold-start time without premature restart |
| Liveness | `/healthz` or `/api/v1/system/health` | Restarts a stuck or unhealthy process |
| Readiness | `/readyz` or `/api/v1/system/readiness` | Removes an unready replica from traffic |

Rules:

- Probe paths must be stable and version-independent.
- Probe endpoints must not require tenant context.
- Readiness must be stricter than liveness.
- Product Ingestion Endpoint readiness must fail if telemetry cannot be durably accepted or queued.
- Product API readiness must fail if it cannot enforce authorization or reach the Product Metadata Store.
- Product Dashboard readiness must fail if it cannot reach Product API.

## Internal SLOs

These are internal service objectives for the first release, not contractual customer SLAs.

| Capability | SLI | Day-1 SLO |
| --- | --- | --- |
| Product Dashboard availability | Successful Front Door availability checks for `app` | 99.5 percent monthly |
| Product API availability | Successful readiness checks through the intended route | 99.5 percent monthly |
| Product Ingestion availability | Authenticated valid ingestion requests accepted or explicitly rejected by policy | 99.5 percent monthly |
| Ingestion latency | Time from accepted telemetry request to normalized session record availability | p95 under 10 minutes |
| Ingestion rejection visibility | Time from rejected auditable request to rejection record availability | p95 under 5 minutes |
| Redaction latency | Time from accepted content capture candidate to redacted stored blob or redaction failure status | p95 under 15 minutes for first-release content size limits |
| Recommendation completion | Time from eligible session ready to recommendation record or unavailable status | p95 under 30 minutes |
| Pricing refresh | Scheduled pricing seed refresh result recorded | Completes once per configured refresh window |
| Audit recording | Governance Audit Event committed for protected admin and content-review actions | p95 under 1 minute |

SLO rules:

- If a dependency is intentionally disabled by policy, the product must report `unavailable`, `not_applicable`, or another documented state rather than silently failing.
- SLO misses must create operational records and alerts where configured.
- SLOs must not be used to rank individual developers.
- First-release SLOs can be revised after observed production data, but changes must be documented.

## Alerts

Alerts are implemented through Azure Monitor metric alerts or log search alerts.

First-release action groups:

| Action group | Target |
| --- | --- |
| `tokenobs-ops-critical` | Private operations notification channel |
| `tokenobs-ops-warning` | Private operations notification channel |
| `tokenobs-security-review` | Private security or reviewer notification channel |

Rules:

- Public GitHub issues must not be created automatically for production incidents.
- Alert payloads must use sanitized metadata only.
- Alert payloads must not include prompt text, captured content, secrets, tokens, connection strings, raw stack traces with sensitive data, or full request payloads.
- Alert definitions must be managed by Terraform where supported.

Minimum alert set:

| Alert | Severity | Signal |
| --- | --- | --- |
| Front Door origin unhealthy | Critical | Front Door origin health or route failure signal |
| Product Dashboard unavailable | Critical | Availability test or health check failure |
| Product API unavailable | Critical | Readiness or availability failure |
| Product Ingestion Endpoint unavailable | Critical | Readiness or availability failure |
| Ingestion accepted count drops to zero unexpectedly | Warning | Product ingestion metrics over expected active window |
| Ingestion rejection rate spike | Warning | Rejection count or rate by reason |
| ACA unhealthy replicas | Critical | Container Apps replica health or ready replica count |
| ACA restart spike | Warning | Container restart count over threshold |
| PostgreSQL connection failures | Critical | App logs or database metrics |
| PostgreSQL backup or restore validation failure | Critical | Backup or restore drill result |
| Blob lifecycle validation failure | Warning | Lifecycle validation job result |
| Redaction failure rate above threshold | Warning or Critical based on policy | Redaction status counts |
| Redaction review backlog above threshold | Warning | Review queue age or count |
| Recommendation job failure rate above threshold | Warning | Job failure count or rate |
| Recommendation queue age above threshold | Warning | Oldest pending recommendation age |
| Audit write failure | Critical | Governance Audit Event write failure |
| Pricing refresh failure | Warning | Pricing refresh job result |
| Deferred ACA origin isolation proof fails after hardening is reintroduced | Critical | Edge Origin Validation result |

## Data Protection Validation

### PostgreSQL Restore Drill

Before production readiness is claimed, the team must complete a non-production restore drill.

Required proof:

- Restore a PostgreSQL Flexible Server backup to a new server.
- Verify schema migration state.
- Verify representative product metadata can be queried.
- Verify no production overwrite or in-place restore occurs.
- Record restore start time, restore completion time, operator, source server, target server, and result.
- Delete or retain the restored server according to the drill plan.

Minimum cadence:

- One successful restore drill before production readiness.
- Repeat after material changes to backup configuration, database topology, or migration strategy.

### Blob Lifecycle Validation

Before production readiness is claimed, the team must validate Blob lifecycle behavior in non-production.

Required proof:

- Lifecycle policy exists for captured content containers or prefixes.
- Test blobs with representative retention classes are affected as expected.
- Metadata-only records remain queryable after content lifecycle action where policy requires that behavior.
- Deletion and transition behavior is recorded as an operational validation result.

## Audit Export Strategy

First-release audit export is minimal.

Requirements:

- Governance Audit Events stay queryable in the Product Metadata Store.
- Operationally relevant audit events also emit to Application Insights or Log Analytics where appropriate.
- Export must support a bounded date range, Customer Organization scope, actor, action, resource type, and result.
- Export must redact sensitive content and secret material.
- Export must be authorized by product role and recorded as a Governance Audit Event.

Out of scope for first release:

- Customer self-service audit export portal.
- SIEM integration as a product feature.
- Long-term immutable audit archive unless required by a later compliance decision.

## Incident Runbooks

First-release runbooks must exist before production readiness is claimed.

Required runbooks:

| Runbook | Minimum content |
| --- | --- |
| Product ingestion down | Symptoms, first checks, Front Door health, ACA health, credential validation, telemetry write path, rollback decision |
| Front Door origin unhealthy | DNS check, managed certificate state, Front Door origin state, ACA readiness, and deferred direct-origin hardening status |
| Redaction blocked | Failure classes, Azure AI dependency checks, manual review path, retry and discard policy |
| Recommendation queue stuck | Job health, queue age, model dependency status, retry limits, unavailable recommendation handling |
| PostgreSQL restore | Restore to new server, validation checks, cutover decision boundaries, rollback notes |
| Emergency credential revocation | Scoped Ingestion Credential lookup, revoke action, audit event, developer communication |
| Content disclosure concern | Stop capture if needed, identify affected records, preserve audit, reviewer workflow, legal or security escalation path |
| Pricing seed failure | Disable automatic pricing candidate promotion, preserve last approved basis, rerun refresh |

Runbook rules:

- Runbooks must not instruct operators to paste secrets or captured content into public issues.
- Runbooks must preserve the non-punitive product posture.
- Runbooks must distinguish product incidents from individual developer behavior.
- Runbooks must state when to stop ingestion, when to keep accepting metadata only, and when to fail closed.

## Implementation Acceptance Criteria

- Each long-running Container App has liveness and readiness behavior.
- Probe paths are documented and configured through Terraform.
- Day-1 internal SLOs are implemented as measurable queries or metrics.
- Minimum alert set is implemented with private action groups.
- Production incidents do not create public GitHub issues automatically.
- One non-production PostgreSQL restore drill is completed before production readiness.
- Blob lifecycle validation is completed before production readiness.
- Required runbooks exist and are linked from the operations documentation.
- Alert payloads and runbook examples do not expose secrets or captured content.
- Operational dashboards and alerts do not rank individual developers.

## Verified Platform Facts

- Azure Container Apps health probes support container health checks for app runtime health: https://learn.microsoft.com/en-us/azure/container-apps/health-probes
- Azure Monitor alerts include metric alerts and log search alerts: https://learn.microsoft.com/en-us/azure/azure-monitor/alerts/alerts-overview
- Azure Monitor action groups define notification and automation actions when alerts fire: https://learn.microsoft.com/en-us/azure/azure-monitor/alerts/action-groups
- Azure Well-Architected reliability guidance defines SLOs as measurable reliability or performance targets for customer interactions: https://learn.microsoft.com/en-us/azure/well-architected/reliability/metrics
- Azure Database for PostgreSQL Flexible Server supports point-in-time restore within configured backup retention: https://learn.microsoft.com/en-us/azure/postgresql/backup-restore/concepts-backup-restore
- Azure Blob Storage lifecycle management policies can transition blobs between access tiers or delete blobs at lifecycle end: https://learn.microsoft.com/en-us/azure/storage/blobs/lifecycle-management-overview
