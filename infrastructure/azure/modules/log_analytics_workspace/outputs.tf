output "name" {
  description = "Name of the Log Analytics workspace."
  # The upstream AVM module marks the full resource object sensitive, but the
  # resource name is a non-secret identifier in this module's output contract.
  value = nonsensitive(module.log_analytics_workspace.resource.name)
}

output "resource_id" {
  description = "Resource ID of the Log Analytics workspace."
  value       = module.log_analytics_workspace.resource_id
}

output "workspace_id" {
  description = "Workspace customer ID used by consumers that require the non-secret workspace identifier."
  # The upstream AVM module marks the full resource object sensitive, but the
  # workspace customer ID is a non-secret identifier in this module's contract.
  value = nonsensitive(try(module.log_analytics_workspace.resource.workspace_id, null))
}
