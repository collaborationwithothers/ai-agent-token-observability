output "name" {
  description = "Name of the Log Analytics workspace."
  value       = module.log_analytics_workspace.resource.name
}

output "resource_id" {
  description = "Resource ID of the Log Analytics workspace."
  value       = module.log_analytics_workspace.resource_id
}

output "workspace_id" {
  description = "Workspace customer ID used by consumers that require the non-secret workspace identifier."
  value       = try(module.log_analytics_workspace.resource.workspace_id, null)
}
