output "default_data_collection_endpoint_id" {
  description = "Managed default data collection endpoint ID for this Azure Monitor workspace."
  value       = azurerm_monitor_workspace.this.default_data_collection_endpoint_id
}

output "default_data_collection_rule_id" {
  description = "Managed default data collection rule ID for this Azure Monitor workspace."
  value       = azurerm_monitor_workspace.this.default_data_collection_rule_id
}

output "name" {
  description = "Name of the Azure Monitor workspace."
  value       = azurerm_monitor_workspace.this.name
}

output "query_endpoint" {
  description = "Query endpoint for the Azure Monitor workspace."
  value       = azurerm_monitor_workspace.this.query_endpoint
}

output "resource_id" {
  description = "Resource ID of the Azure Monitor workspace."
  value       = azurerm_monitor_workspace.this.id
}
