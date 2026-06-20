# Managed Grafana Stage

Responsibility: Azure Managed Grafana workspace provisioning, aggregate-only Azure Monitor workspace integration, and repo-versioned first-release dashboard deployment.

Backend key: `managed_grafana.tfstate`

Remote backend example: `backend.azurerm.tf.example`

## Aggregate Metrics Boundary

This stage consumes `observability_foundation.metrics_data_source_identifiers.aggregate_metrics` and rejects any upstream contract that is not:

- `type = "azure_monitor_workspace"`.
- `boundary = "aggregate_metrics_only"`.
- allowed for `managed_grafana` in `consumer_stages`.

The Azure Monitor workspace integration is the only first-release Grafana data source created by this stage. The Grafana system-assigned managed identity receives `Monitoring Data Reader` on the Azure Monitor workspace resource ID only.

This stage must not consume `trace_log_data_source_identifiers`, Log Analytics workspace IDs, Application Insights IDs, raw session data, raw content, raw logs, recommendation evidence packets, content review data, or developer ranking data.

## Repo-Versioned Dashboard Deployment

This stage deploys the `Token Observability` Grafana folder and first-release dashboard JSON artifacts from `infrastructure/grafana/dashboards/` through the `grafana/grafana` provider.

Grafana provider connection details and authentication must be supplied through process-local provider environment variables. Do not add provider `url`, `auth`, or `http_headers` arguments to Terraform files, and do not place tokens in variables, outputs, state, dashboard JSON, repository files, or validation artifacts.

Grafana user or group RBAC, Product Dashboard links, private endpoints, custom DNS, service accounts, and API keys remain separate follow-up work. Keep `api_key_enabled = false`.

## Provider And AVM Choice

No suitable Azure Verified Module exists for Azure Managed Grafana in the current AVM list. The local wrapper module uses AzureRM `azurerm_dashboard_grafana` and `azurerm_role_assignment`.

No AzAPI workaround is required for the workspace, Azure Monitor workspace integration, or role assignment in this issue.

## Local Validation

Run from the repository root:

```bash
scripts/terraform-stage-check.sh managed_grafana
scripts/validate-terraform-managed-grafana.sh
scripts/validate-focused.sh terraform
```

Run direct Terraform validation from this stage only after initializing backend-free:

```bash
terraform init -backend=false
terraform validate
```

Planning requires an environment workspace such as `dv_eastus2_internal` and upstream `observability_foundation` outputs. The `default` workspace is intentionally rejected.

Do not add .NET, xUnit, or C# tests for Terraform behavior.
