output "stage_name" {
  description = "Terraform stage name."
  value       = local.stage_name
}

output "expected_workspace_name" {
  description = "Expected workspace name for workflow guardrails."
  value       = local.expected_workspace_name
}

output "resource_group_ids" {
  description = "Resource group IDs created by this stage."
  value = {
    observability = module.observability_resource_group.id
  }
}

output "observability_resource_group_name" {
  description = "Name of the resource group containing observability foundation resources."
  value       = module.observability_resource_group.name
}

output "log_analytics_workspace_resource_id" {
  description = "Resource ID of the Log Analytics workspace for diagnostics, traces, logs, and operational queries."
  value       = module.log_analytics_workspace.resource_id
}

output "log_analytics_workspace_name" {
  description = "Name of the Log Analytics workspace."
  value       = module.log_analytics_workspace.name
}

output "log_analytics_workspace_id" {
  description = "Non-secret Log Analytics workspace customer ID for consumers that require a workspace identifier."
  value       = module.log_analytics_workspace.workspace_id
}

output "application_insights_resource_id" {
  description = "Resource ID of the workspace-based Application Insights component."
  value       = module.application_insights.resource_id
}

output "application_insights_name" {
  description = "Name of the Application Insights component."
  value       = module.application_insights.name
}

output "application_insights_app_id" {
  description = "Application Insights App ID for non-secret application telemetry configuration references."
  value       = module.application_insights.app_id
}

output "application_insights_configuration_reference" {
  description = "Non-secret Application Insights configuration references. Connection strings and instrumentation keys are intentionally not output."
  value = {
    resource_id           = module.application_insights.resource_id
    name                  = module.application_insights.name
    app_id                = module.application_insights.app_id
    workspace_resource_id = module.log_analytics_workspace.resource_id
  }
}

output "monitor_workspace_resource_id" {
  description = "Resource ID of the Azure Monitor workspace used for aggregate metrics and managed Prometheus-compatible consumers."
  value       = module.monitor_workspace.resource_id
}

output "monitor_workspace_name" {
  description = "Name of the Azure Monitor workspace."
  value       = module.monitor_workspace.name
}

output "monitor_workspace_query_endpoint" {
  description = "Query endpoint for the Azure Monitor workspace."
  value       = module.monitor_workspace.query_endpoint
}

output "monitor_workspace_default_data_collection_endpoint_id" {
  description = "Managed default data collection endpoint ID for the Azure Monitor workspace."
  value       = module.monitor_workspace.default_data_collection_endpoint_id
}

output "monitor_workspace_default_data_collection_rule_id" {
  description = "Managed default data collection rule ID for the Azure Monitor workspace."
  value       = module.monitor_workspace.default_data_collection_rule_id
}

output "diagnostic_destinations" {
  description = "Non-secret diagnostic destination contracts for downstream Container Apps, jobs, Front Door, data platform, and AI service resources."
  value       = local.diagnostic_destination_contracts
}

output "metrics_data_source_identifiers" {
  description = "Non-secret aggregate metrics data source identifiers for app runtime, Managed Grafana, SLO, and alert stages."
  value = {
    aggregate_metrics = local.aggregate_metrics_data_source
  }
}

output "trace_log_data_source_identifiers" {
  description = "Non-secret trace, log, event, and diagnostics data source identifiers for operational consumers."
  value = {
    operational_traces_logs = local.trace_log_data_source
  }
}

output "observability_boundaries" {
  description = "Documented non-secret data boundaries for downstream observability consumers."
  value = {
    aggregate_metrics = "Azure Monitor workspace is for aggregate token, cost, harness, cache, ingestion, and platform metrics."
    traces_logs       = "Application Insights and Log Analytics are for operational traces, logs, events, diagnostics, and authorized investigation queries."
    excluded_payloads = "Raw session content, prompt text, tool output, secrets, captured content, and tenant-private payloads are not exposed through this stage output contract."
  }
}
