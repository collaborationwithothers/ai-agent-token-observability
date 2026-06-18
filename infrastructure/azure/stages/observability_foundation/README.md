# Observability Foundation Stage

Responsibility: provision the Azure observability destinations consumed by app runtime, Managed Grafana, SLO, alert, data platform, AI service, and edge stages.

Backend key: `observability_foundation.tfstate`

Remote backend example: `backend.azurerm.tf.example`

## Topology

This stage creates an observability resource group and three destination resources:

| Resource | Implementation | Purpose |
| --- | --- | --- |
| Log Analytics workspace | Local wrapper over `Azure/avm-res-operationalinsights-workspace/azurerm` v0.5.1 | Resource diagnostics, operational logs, traces, events, and investigation queries |
| Application Insights component | Local wrapper over `Azure/avm-res-insights-component/azurerm` v0.4.0 | Workspace-based application telemetry for Product API, Product Ingestion Endpoint, Product Dashboard, and jobs |
| Azure Monitor workspace | Local AzureRM wrapper using `azurerm_monitor_workspace` | Aggregate metrics and managed Prometheus-compatible data source for Managed Grafana, SLO, and alert stages |

The stage also outputs diagnostic destination contracts for downstream resources. Downstream stages own the actual `azurerm_monitor_diagnostic_setting` resources for their resource scopes because supported categories vary by resource type.

Public ingestion and query access remains enabled by default for Log Analytics, Application Insights, and Azure Monitor workspace so downstream stages can emit diagnostics and query aggregate metrics after this stage is applied. Private-only observability connectivity requires Azure Monitor Private Link Scope, scoped resource associations, private endpoints, private DNS, and Azure Monitor workspace data collection wiring. That plumbing is not created by this issue, so operators must not set the public access variables to `false` until a later implementation provides the private path.

## Metric And Log Boundaries

Aggregate product metrics belong in the Azure Monitor workspace. The first-release metric boundary is defined by `docs/architecture/aggregate-metrics-contract.md` and is limited to low-cardinality aggregate token, cost, harness, model, cache, ingestion, job, redaction, API, and platform health metrics.

Application Insights and Log Analytics are for operational traces, logs, events, resource diagnostics, correlation, and authorized investigation queries. They are not the Product Dashboard Session Investigation View and they are not a Grafana raw log surface.

Product metadata remains in PostgreSQL. Captured content remains in Blob Storage only when Content Capture Policy allows it and pre-storage redaction succeeds.

## Downstream Consumers

| Consumer | Uses |
| --- | --- |
| `app_runtime` | Log Analytics workspace ID for Container Apps diagnostics, Azure Monitor workspace IDs for aggregate metrics, and Application Insights App ID/resource ID references |
| `managed_grafana` | Azure Monitor workspace resource ID, query endpoint, default data collection endpoint ID, and default data collection rule ID |
| SLO and alert stages | Aggregate metrics data source IDs and Log Analytics workspace resource ID |
| `edge` | Log Analytics workspace resource ID for Front Door access, health probe, WAF logs, and metrics |
| `data_platform` | Log Analytics workspace resource ID for database and storage diagnostics where supported |
| `ai_services` | Log Analytics workspace resource ID and Application Insights resource ID for operational diagnostics where supported |

## Exclusions

Stage outputs and examples must not expose:

- Raw session content.
- Captured content.
- Prompt text.
- Code content.
- Command output.
- Tool output or tool results.
- Secrets, tokens, keys, connection strings, or instrumentation keys.
- Tenant-private payloads.
- Developer ranking, blame, or individual performance data.

Managed Grafana must use aggregate-only data sources from this stage. Grafana dashboards, folders, RBAC, and role assignments are implemented by later Managed Grafana issues. Alert rules and action groups are implemented by later alerting issues.

## Non-Punitive Aggregate Expectations

Observability data from this stage supports product health, cost visibility, and waste reduction without ranking individual developers. The aggregate metrics contract forbids labels containing developer identity, session identifiers, credential identifiers, trace IDs, span IDs, prompt IDs, repository paths, file paths, hashes, IP addresses, or user-agent strings.

## Provider And AVM Choices

The repository order is AVM through a local wrapper, then AzureRM, then AzAPI for provider gaps.

- Log Analytics uses AVM because a verified module exists.
- Application Insights uses AVM because a verified module exists.
- Azure Monitor workspace uses AzureRM because no suitable verified Azure Monitor workspace AVM was found for this issue.
- No AzAPI resources are required by this stage.

The wrappers disable AVM telemetry where supported and do not define provider blocks. Providers remain stage-owned.

Private Link for Azure Monitor is deferred. Until that follow-up exists, the default public ingestion and query variables preserve functional diagnostics and aggregate metrics readiness for downstream stages.

## Workspace Guard

The selected Terraform workspace must equal:

```text
{environment}_{azureRegion}_{customerOrganizationSlug}
```

The `default` workspace is rejected before Azure-changing plans. If `terraform_workspace_name` is supplied by automation, it must match the selected workspace and the environment, region, and customer slug inputs.

## Local Validation

From the repository root:

```bash
terraform -chdir=infrastructure/azure fmt -recursive
terraform -chdir=infrastructure/azure/stages/observability_foundation init -backend=false
terraform -chdir=infrastructure/azure/stages/observability_foundation validate
scripts/validate-terraform-observability-foundation.sh
scripts/validate-focused.sh terraform
```

The focused Terraform profile also runs repository workflow guardrails. Do not add .NET, xUnit, or C# tests for Terraform behavior.
