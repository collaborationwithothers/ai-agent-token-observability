output "stage_name" {
  description = "Terraform stage name."
  value       = local.stage_name
}

output "expected_workspace_name" {
  description = "Expected workspace name for workflow guardrails."
  value       = local.expected_workspace_name
}

output "resource_group_ids" {
  description = "Resource group IDs referenced by this stage."
  value = {
    observability = var.observability_resource_group_id
  }
}

output "aggregate_metrics_data_source" {
  description = "Aggregate-only metrics data source wired to Azure Managed Grafana."
  value       = module.managed_grafana.aggregate_metrics_data_source
}

output "grafana_endpoint" {
  description = "Native Azure Managed Grafana endpoint."
  value       = module.managed_grafana.endpoint
}

output "grafana_identity_principal_id" {
  description = "System-assigned managed identity principal ID granted aggregate metrics data-reader access."
  value       = module.managed_grafana.identity_principal_id
}

output "grafana_identity_tenant_id" {
  description = "System-assigned managed identity tenant ID."
  value       = module.managed_grafana.identity_tenant_id
}

output "grafana_version" {
  description = "Deployed Azure Managed Grafana semantic version."
  value       = module.managed_grafana.grafana_version
}

output "grafana_workspace_name" {
  description = "Azure Managed Grafana workspace name."
  value       = module.managed_grafana.name
}

output "grafana_workspace_resource_id" {
  description = "Azure Managed Grafana workspace resource ID."
  value       = module.managed_grafana.resource_id
}

output "observability_resource_group_name" {
  description = "Name of the observability resource group that contains Azure Managed Grafana."
  value       = var.observability_resource_group_name
}
