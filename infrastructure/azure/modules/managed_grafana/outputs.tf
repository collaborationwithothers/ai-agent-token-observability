output "aggregate_metrics_data_source" {
  description = "Aggregate-only Azure Monitor workspace data source wired to this Grafana workspace."
  value = {
    type           = var.aggregate_metrics_data_source.type
    resource_id    = var.aggregate_metrics_data_source.resource_id
    name           = var.aggregate_metrics_data_source.name
    query_endpoint = var.aggregate_metrics_data_source.query_endpoint
    boundary       = var.aggregate_metrics_data_source.boundary
    role           = "Monitoring Data Reader"
  }
}

output "endpoint" {
  description = "Native Azure Managed Grafana endpoint."
  value       = azurerm_dashboard_grafana.this.endpoint
}

output "grafana_version" {
  description = "Deployed Azure Managed Grafana semantic version."
  value       = azurerm_dashboard_grafana.this.grafana_version
}

output "identity_principal_id" {
  description = "System-assigned managed identity principal ID for Azure Monitor workspace data-reader access."
  value       = azurerm_dashboard_grafana.this.identity[0].principal_id
}

output "identity_tenant_id" {
  description = "System-assigned managed identity tenant ID."
  value       = azurerm_dashboard_grafana.this.identity[0].tenant_id
}

output "name" {
  description = "Azure Managed Grafana workspace name."
  value       = azurerm_dashboard_grafana.this.name
}

output "resource_id" {
  description = "Azure Managed Grafana workspace resource ID."
  value       = azurerm_dashboard_grafana.this.id
}
