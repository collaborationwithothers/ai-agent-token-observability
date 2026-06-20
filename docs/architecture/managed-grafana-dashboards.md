# Managed Grafana Dashboards

## Purpose

This document defines the Azure Managed Grafana boundary for the Azure Production MVP.

Managed Grafana is the aggregate observability surface. It is not the Session Investigation View, content review surface, recommendation workflow, or developer coaching surface.

The product aggregate metric names, labels, and PromQL query contracts are defined in [aggregate-metrics-contract.md](./aggregate-metrics-contract.md).

## Data Source Decision

First-release Grafana dashboards use aggregate metrics as the primary data source.

Primary data source:

| Data source | Purpose |
| --- | --- |
| Azure Monitor workspace or Azure Monitor managed service for Prometheus | Aggregate token, cost, harness, model, cache, ingestion, and platform metrics |

Allowed later data sources:

| Data source | Allowed use |
| --- | --- |
| Azure Monitor Metrics | Aggregate platform health and Azure resource metrics |
| Log Analytics or Application Insights | Aggregate operational panels only, if a later implementation proves they are needed |

Excluded from first-release Grafana:

| Excluded data | Reason |
| --- | --- |
| Raw traces, logs, or events | Session investigation belongs in Product Dashboard |
| Prompt text, code content, command output, or tool results | Captured content must go through product policy and redaction controls |
| Raw session timelines | Product API authorization and audit context are required |
| Individual developer ranking panels | Violates the non-punitive product posture |
| Content review queues | Review workflows belong in Product Dashboard |

If Log Analytics or Application Insights is added later, it must be limited to aggregate operational panels. It must not expose raw session content, developer ranking, content review, or recommendation evidence packets in Grafana.

The #62 Terraform implementation provisions only the Azure Managed Grafana workspace and Azure Monitor workspace integration. It grants the Grafana managed identity `Monitoring Data Reader` on the Azure Monitor workspace resource ID exported by `observability_foundation.metrics_data_source_identifiers.aggregate_metrics`. Dashboard JSON, Grafana folders, Grafana user RBAC, Grafana provider authentication proof gates, service account fallback, Product Dashboard links, private endpoints, and custom DNS are follow-up work.

## Provider Authentication Contract

Issue #63 owns the Terraform provider authentication proof for dashboard and folder deployment. The default contract for issue #64 is Microsoft Entra bearer-token authentication to the Azure Managed Grafana data plane, passed to the `grafana/grafana` provider through process-local HTTP headers.

The reproducible proof fixture lives under `infrastructure/azure/proofs/grafana_provider_auth` and uses only a read-only folder data source. It must not create dashboards, folders, service accounts, tokens, API keys, or data sources.

Service account token fallback is permitted only if the Entra proof fails for Terraform provider use. Any fallback token must be Key Vault backed, manually approved, least privilege, rotated explicitly, and disabled by default in production workflows. Follow [Grafana Provider Authentication Proof](../operations/grafana-provider-auth-proof.md) before issue #64 adds dashboard JSON deployment.

## Dashboard Boundary

Managed Grafana may show:

- Aggregate token burn over time.
- Estimated cost over time.
- Harness breakdown.
- Model breakdown.
- Cache hit and cache-bust trends.
- High-level hotspot counts and categories.
- Ingestion volume and failure rates.
- Product platform health metrics.

Managed Grafana must not show:

- Raw prompts or generated content.
- Raw command output.
- Raw tool results.
- Session-level coaching.
- Individual user league tables.
- Confirmed factual user-error claims.
- Content redaction review details.

## First-Release Dashboards

The Azure Production MVP uses four Managed Grafana dashboards.

All first-release dashboard JSON files are stored under:

```text
infrastructure/grafana/dashboards/
```

Issue #64 deploys these artifacts from Terraform through the `grafana/grafana` provider. Dashboard JSON remains the production source of truth; manual UI-only dashboard changes are not a production deployment path.

Dashboard artifact contract:

| Dashboard | UID | JSON artifact |
| --- | --- | --- |
| Executive Cost Overview | `tokenobs-executive-cost-overview` | `infrastructure/grafana/dashboards/executive-cost-overview.json` |
| Harness And Model Operations | `tokenobs-harness-model-operations` | `infrastructure/grafana/dashboards/harness-and-model-operations.json` |
| Cache And Hotspot Trends | `tokenobs-cache-hotspot-trends` | `infrastructure/grafana/dashboards/cache-and-hotspot-trends.json` |
| Ingestion And Platform Health | `tokenobs-ingestion-platform-health` | `infrastructure/grafana/dashboards/ingestion-and-platform-health.json` |

Folder contract:

| Folder title | Folder UID |
| --- | --- |
| Token Observability | `tokenobs` |

### Executive Cost Overview

Purpose: show aggregate spend and usage direction without exposing individual developer performance.

Required panels:

- Total token burn over time.
- Estimated cost over time.
- Cost by model.
- Token burn by model.
- Token burn by harness.
- Trend versus configured non-punitive budget threshold.
- Top hotspot categories by aggregate cost impact.

Forbidden panels:

- Individual developer leaderboard.
- Session-level cost table.
- Prompt or content excerpts.

### Harness And Model Operations

Purpose: show whether Codex telemetry and model usage are healthy and predictable.

Required panels:

- Codex session count over time.
- Codex turn count over time.
- Model distribution.
- Request or invocation count by model.
- Error rate by harness and model.
- Accepted telemetry count.
- Rejected telemetry count by rejection category.

Forbidden panels:

- Raw rejected payloads.
- Individual developer error ranking.
- Session timeline drilldown inside Grafana.

### Cache And Hotspot Trends

Purpose: show aggregate cache and hotspot behavior so teams can reduce waste without blame workflows.

Required panels:

- Cache hit rate over time.
- Cache miss or cache-bust rate over time.
- Cache-bust categories by aggregate count.
- Cache-bust categories by estimated token impact.
- Token Hotspot count by type.
- Token Hotspot estimated impact by type.
- LLM-inferred candidate hotspot count, clearly separated from confirmed findings.

Forbidden panels:

- Confirmed factual user-error claims.
- Individual user hotspot ranking.
- Raw evidence packets or recommendation prompts.

### Ingestion And Platform Health

Purpose: show whether the production ingestion and background processing path is healthy.

Required panels:

- Ingestion request rate.
- Accepted versus rejected telemetry.
- Ingestion latency percentiles.
- Payload normalization failures.
- Background job failures by job type.
- Redaction failure or review-required rate.
- Product API error rate.
- Container Apps resource health summary.

Forbidden panels:

- Raw traces or logs.
- Raw content capture failures.
- Secret, credential, or token material.

## Product Dashboard Linkage

Grafana may link to Product Dashboard routes for authorized investigation.

Rules:

- Grafana links must not bypass Product API authorization.
- Grafana drilldown filters are validated by `GET /api/v1/grafana/drilldown?route=/overview|/sessions&...` before Product Dashboard uses them.
- The drilldown gate authorizes `/overview` with `OverviewRead` and `/sessions` with Product API session-read rules.
- Product Dashboard remains responsible for session, repository, harness, model, hotspot, recommendation, content review, and governance views.
- Product Dashboard may link to Grafana aggregate panels through the native Azure Managed Grafana endpoint.
- The first release does not require a `grafana.tokenobs.consultwithcloud.com` vanity hostname.

Allowed Grafana-to-Product-Dashboard link parameters:

| Parameter | Allowed values |
| --- | --- |
| `from` | Grafana time range start |
| `to` | Grafana time range end |
| `environment` | `dv`, `qa`, `pp`, `pd` |
| `region` | Azure region slug |
| `harness` | `codex`, later `copilot`, `claude` |
| `model` | Normalized model label |
| `modelProvider` | `openai`, `anthropic`, `github`, `unknown` |
| `hotspotType` | Token Hotspot type from the aggregate metrics contract |
| `cacheBustCategory` | Cache-bust category from the aggregate metrics contract |
| `findingState` | `confirmed`, `llm_inferred_candidate` |
| `signalType` | `metrics`, `traces`, `logs` |
| `result` | Bounded aggregate result such as `accepted`, `rejected`, `failed`, `succeeded` |
| `rejectionReason` | Bounded rejection reason from the aggregate metrics contract |

Forbidden Grafana-to-Product-Dashboard link parameters:

- `sessionId`.
- `productUserId`.
- `developer`.
- `credentialId`.
- `traceId`.
- `spanId`.
- `repositoryPath`.
- `filePath`.
- `contentReferenceId`.
- `blobUri`.
- `prompt`.
- `commandOutput`.
- `toolResult`.
- `returnUrl`.
- Any absolute or external URL.

Link templates:

| Grafana source | Product Dashboard route |
| --- | --- |
| Executive Cost Overview | `/overview?from=${__from}&to=${__to}&environment=${environment}&region=${region}&harness=${harness}&model=${model}` |
| Harness And Model Operations | `/sessions?from=${__from}&to=${__to}&environment=${environment}&region=${region}&harness=${harness}&model=${model}` |
| Cache And Hotspot Trends | `/overview?from=${__from}&to=${__to}&environment=${environment}&region=${region}&harness=${harness}&hotspotType=${__field.labels.hotspot_type}&cacheBustCategory=${__field.labels.cache_bust_category}&findingState=${__field.labels.finding_state}` |
| Ingestion And Platform Health | `/overview?from=${__from}&to=${__to}&environment=${environment}&region=${region}&signalType=${__field.labels.signal_type}&result=${__field.labels.result}&rejectionReason=${__field.labels.rejection_reason}` |

Rules:

- Links must use relative Product Dashboard routes, not absolute URLs.
- Product Dashboard must ignore unknown query parameters.
- Product Dashboard must validate every known query parameter against an allowlist before using it.
- Product Dashboard must call `GET /api/v1/me` and enforce Product API authorization before loading linked data.
- Grafana links may pre-filter an overview or session search. They must not grant access to session investigation, content review, recommendations, or audit details.
- Grafana must not link directly to raw content, redaction queues, evidence packets, Blob Storage, PostgreSQL, Log Analytics, Application Insights, or Managed Prometheus queries.
- If a later dashboard adds a session-specific link, the link must land on Product Dashboard and Product API must authorize the session before returning detail.

## Grafana RBAC Decision

This section defines a later dashboard/RBAC implementation boundary. It is not part of issue #62, which only creates the workspace and aggregate data source wiring.

Managed Grafana uses coarse-grained RBAC for the Azure Production MVP.

The first release maps Microsoft Entra groups to Azure Managed Grafana built-in roles only:

| Entra group purpose | Azure Managed Grafana role | Use |
| --- | --- | --- |
| Grafana administrators | Grafana Admin | Operate the workspace, data source wiring, folders, and dashboard provisioning |
| Grafana dashboard editors | Grafana Editor | Edit aggregate dashboard JSON during controlled development and review in non-production only |
| Grafana dashboard viewers | Grafana Viewer | View approved aggregate dashboards |

Rules:

- Grafana roles must not mirror Product Dashboard roles such as ProductOwner, EngineeringLead, Developer, SecurityReviewer, or PlatformAdmin.
- Product Dashboard roles remain authoritative for investigation, recommendations, content review, governance, and audit workflows.
- Grafana roles grant aggregate dashboard access only.
- Grafana permissions must not be used to authorize Product API routes.
- Production Grafana is human Viewer-only by default.
- Production Grafana Editor assignments are forbidden by default.
- Production Grafana Admin access is restricted to a small break-glass or platform operations group and must not be used for routine dashboard changes.
- Production dashboard changes flow through versioned dashboard JSON and Terraform.
- Grafana Editor assignments are allowed in `dv` and `qa` for controlled dashboard development and review.
- Grafana Editor assignments in `pp` or `pd` require an explicit exception captured in implementation docs and workflow approvals.
- Fine-grained Grafana folder or team permissions are deferred unless a later dashboard segmentation need proves they are required.
- Grafana Limited Viewer is not used in the first release unless a specific least-privilege dashboard-only access case requires it.
- Terraform should manage Entra group role assignments where supported.

Terraform input contract:

| Variable | Required | Purpose |
| --- | --- | --- |
| `grafana_admin_group_object_id` | Yes | Microsoft Entra group object ID mapped to Grafana Admin |
| `grafana_editor_group_object_id` | Environment-specific | Microsoft Entra group object ID mapped to Grafana Editor |
| `grafana_viewer_group_object_id` | Yes | Microsoft Entra group object ID mapped to Grafana Viewer |
| `allow_production_grafana_editors` | Yes | Explicit exception flag for `pp` or `pd` Grafana Editor assignment |
| `grafana_provider_auth_mode` | Yes | `entra_oidc` by default, `service_account_token` only after proof failure |
| `allow_grafana_service_account_fallback` | Yes | Defaults false; explicit gate to enable service account fallback |
| `grafana_service_account_token_secret_name` | Fallback only | Key Vault secret name that stores the Grafana service account token if fallback is approved |

Rules:

- The group variables are environment-scoped Terraform inputs for each `{environment}_{azureRegion}_{customerOrganizationSlug}` workspace.
- The values must be Microsoft Entra object IDs, not group display names.
- The object IDs are not secrets, but they are authorization-sensitive configuration and must be reviewed in Terraform plans.
- `grafana_viewer_group_object_id` is required in every environment.
- `grafana_admin_group_object_id` is required in every environment and must resolve to a small break-glass or platform operations group.
- `grafana_editor_group_object_id` may be set in `dv` and `qa`.
- In `pp` and `pd`, `grafana_editor_group_object_id` must be null or empty unless `allow_production_grafana_editors = true`.
- `allow_production_grafana_editors` must default to `false`.
- A `pp` or `pd` plan with `grafana_editor_group_object_id` set and `allow_production_grafana_editors = false` must fail before apply.
- A `pd` plan with `allow_production_grafana_editors = true` must require GitHub environment approval and must leave a visible plan record of the exception.
- `grafana_provider_auth_mode` must default to `entra_oidc`.
- `grafana_provider_auth_mode = service_account_token` is allowed only when `allow_grafana_service_account_fallback = true`.
- `allow_grafana_service_account_fallback` must default to `false`.
- Service account fallback must not be enabled in `pp` or `pd` until a non-production provider compatibility proof records why Entra OIDC failed.

## Provisioning Decision

Issue #62 implements only the Azure Managed Grafana workspace and aggregate Azure Monitor workspace data source wiring. The dashboard, folder, RBAC, provider authentication, and service account fallback rules below apply to follow-up implementation issues.

Managed Grafana provisioning is Terraform-managed for the Azure Production MVP.

Repository-owned dashboard artifacts:

```text
infrastructure/grafana/dashboards/
```

Rules:

- Dashboard JSON files must be versioned in the repository under `infrastructure/grafana/dashboards/`.
- Terraform manages the Azure Managed Grafana workspace.
- Terraform manages Grafana folders where supported by the Grafana provider.
- Terraform deploys dashboard JSON through Grafana provider resources where supported.
- Terraform manages or wires the Azure Monitor workspace or managed Prometheus data source where supported.
- Terraform manages Entra group role assignments for Azure Managed Grafana where supported by AzureRM.
- Manual UI edits are allowed only for exploration and must be exported back to versioned dashboard JSON before production use.
- No production dashboard can exist only as manual Grafana UI state.
- Terraform plans must show dashboard JSON changes before production apply.
- Dashboard JSON must not contain secrets, raw content examples, or environment-specific identifiers that should be provided by Terraform variables.

## Provider Authentication Proof Gate

Dashboard provisioning authentication is a proof-gated implementation detail, not an unresolved product requirement.

Decision:

- Product requirements assume Terraform-managed Grafana dashboards.
- The first implementation must prove whether the Grafana Terraform provider can use Microsoft Entra authentication against Azure Managed Grafana data-plane APIs from the guarded GitHub workflow.
- If Entra OIDC works, it is the only first-release provider authentication mode.
- If Entra OIDC does not work reliably with the provider path, a Grafana service account token is the fallback.
- The fallback must be explicitly enabled and documented in the implementation issue that proves the incompatibility.

Proof requirements:

- Dashboard provisioning must use a least-privilege automation path.
- First implementation must prove whether the Grafana provider can use a Microsoft Entra access token acquired by the guarded GitHub Actions workflow after Azure OIDC login.
- The Entra token proof must request the Azure Managed Grafana data-plane audience `https://dashboard.azure.com`.
- The proof must create or update one non-production Grafana folder and one non-production dashboard JSON through Terraform.
- The proof must run in a non-production workspace before production provisioning is implemented.
- The proof result must be recorded in the implementation issue or pull request.
- If Entra token authentication works reliably for the Grafana provider, production provisioning must use Entra OIDC and must not enable Grafana service accounts only for Terraform.
- If the Grafana provider cannot use the Entra token reliably, Azure Managed Grafana service accounts are the fallback for Grafana API automation.
- Service account tokens, if required by the provider fallback, must be short-lived where feasible or rotated on a defined cadence and stored outside the repository.
- Service account tokens, if required by the provider fallback, must be stored in Key Vault and fetched at runtime by the guarded workflow.
- Long-lived personal user tokens are forbidden for production provisioning.
- Grafana service accounts and API keys must remain disabled unless the fallback is approved.
- Service account fallback must use the minimum Grafana role required to manage folders, data sources, and dashboard JSON.
- Service account tokens must not be stored in Terraform state, GitHub secrets, repository files, or workflow artifacts.
- Service account token creation, rotation, use, and revocation must be recorded as Governance Audit Events or operational audit logs.
- The fallback can be removed later if provider support for Entra authentication becomes reliable.

## Implementation Issue Creation Needs

- Exact Terraform provider split between AzureRM, AzAPI if needed, and Grafana provider.
- Dashboard JSON implementation for the UIDs and artifact paths defined here.
- Any KQL query contract if a later aggregate Log Analytics panel is accepted.
- Environment-specific values for `grafana_admin_group_object_id`, `grafana_editor_group_object_id`, and `grafana_viewer_group_object_id`.
- Terraform validation that rejects `pp` or `pd` Grafana Editor assignment unless `allow_production_grafana_editors = true`.
- Grafana provider Entra token compatibility proof in non-production.
- Key Vault-backed service account fallback workflow only if Entra token compatibility fails.
- Product Dashboard implementation of the Grafana link parameter allowlist.

## Acceptance Criteria

- Azure Managed Grafana is configured with Azure Monitor workspace or managed Prometheus as the first-release aggregate metrics data source.
- The first release provisions Executive Cost Overview, Harness And Model Operations, Cache And Hotspot Trends, and Ingestion And Platform Health dashboards.
- First-release dashboard JSON uses the metric names, labels, and PromQL query contracts in Aggregate Metrics Contract.
- First-release dashboard JSON files are versioned in the repository and deployed through Terraform.
- No production Grafana dashboard exists only as manual UI state.
- Production Grafana provider authentication uses Entra OIDC if the compatibility proof succeeds.
- Grafana service account tokens are used only as a documented fallback if Entra token authentication is not compatible with the provider path.
- Grafana service accounts and API keys remain disabled unless the fallback is explicitly approved.
- Service account fallback tokens, if used, are stored in Key Vault, fetched at runtime, and never stored in repository files, Terraform state, GitHub secrets, or workflow artifacts.
- Grafana users are assigned through Entra groups mapped to Grafana Admin, Editor, and Viewer only for the first release.
- Production human users are assigned Grafana Viewer by default; Grafana Editor is non-production only unless an exception is explicitly approved.
- Terraform uses environment-scoped Entra group object ID variables for Grafana Admin, Editor, and Viewer assignments.
- `pp` and `pd` Terraform plans fail when `grafana_editor_group_object_id` is set and `allow_production_grafana_editors` is false.
- Production Grafana Admin is restricted to a break-glass or platform operations group.
- Routine production dashboard changes are applied through Terraform, not through human Grafana Editor access.
- Grafana role assignments do not duplicate Product Dashboard role names or authorize Product API routes.
- Grafana dashboards do not query product PostgreSQL, Blob Storage, or Product API directly.
- Grafana dashboards do not expose raw session traces, raw logs, raw events, captured content, or redaction review queues.
- Grafana dashboards do not rank individual developers.
- Any link from Grafana to Product Dashboard requires Product API authorization before showing investigation details.
- Grafana links use only the approved aggregate filter parameters and relative Product Dashboard routes.
- Product Dashboard rejects or ignores unknown Grafana query parameters and never treats them as authorization.
- Log Analytics or Application Insights is not used for first-release session investigation in Grafana.

## Verified Platform Facts

- Azure Monitor managed service for Prometheus integrates with Azure Managed Grafana: https://learn.microsoft.com/en-us/azure/azure-monitor/metrics/prometheus-metrics-overview
- Azure Monitor data can be visualized with Grafana, and Prometheus data from Azure Monitor managed service for Prometheus can be queried from Grafana: https://learn.microsoft.com/en-us/azure/azure-monitor/visualize/visualize-grafana-overview
- Microsoft documents connecting Azure Monitor managed service for Prometheus to Grafana, including Azure Managed Grafana: https://learn.microsoft.com/en-us/azure/azure-monitor/metrics/prometheus-grafana
- Azure Managed Grafana is a fully managed Azure service that supports Azure data sources and Microsoft Entra access control: https://learn.microsoft.com/en-us/azure/managed-grafana/overview
- Azure Managed Grafana supports assigning Grafana roles such as Grafana Admin, Grafana Editor, Grafana Limited Viewer, and Grafana Viewer to Microsoft Entra users, groups, service principals, and managed identities: https://learn.microsoft.com/en-us/azure/managed-grafana/how-to-manage-access-permissions-users-identities
- Azure Managed Grafana Team Sync can map Microsoft Entra groups to Grafana teams and folder permissions when more granular dashboard permissions are needed: https://learn.microsoft.com/en-us/azure/managed-grafana/how-to-sync-teams-with-entra-groups
- Azure Managed Grafana data-plane APIs can be called with a Microsoft Entra access token using the audience `https://dashboard.azure.com`: https://learn.microsoft.com/en-us/azure/managed-grafana/how-to-authenticate-data-plane-api
- Azure Managed Grafana service accounts are intended for automated operations such as provisioning or configuring dashboards through the Grafana API: https://learn.microsoft.com/en-us/azure/managed-grafana/how-to-service-accounts
- Azure Managed Grafana security guidance says service accounts and API keys bypass Microsoft Entra authentication and should be disabled when they are not needed: https://learn.microsoft.com/en-us/azure/managed-grafana/secure-azure-managed-grafana
- The Grafana Terraform provider manages resources such as dashboards, data sources, and folders: https://registry.terraform.io/providers/grafana/grafana/latest/docs
- Grafana documentation describes managing dashboards with Terraform and GitHub Actions using folders and dashboard JSON: https://grafana.com/docs/grafana/latest/as-code/infrastructure-as-code/terraform/dashboards-github-action/
- Grafana dashboard links can include the current time range and template variables, and data links support URL variables: https://grafana.com/docs/grafana/latest/visualizations/dashboards/build-dashboards/manage-dashboard-links/ and https://grafana.com/docs/grafana/latest/visualizations/panels-visualizations/configure-data-links/
- OWASP warns that unvalidated redirects and forwards can be abused when applications accept untrusted URL input: https://cheatsheetseries.owasp.org/cheatsheets/Unvalidated_Redirects_and_Forwards_Cheat_Sheet.html
