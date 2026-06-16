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
    app_runtime = azurerm_resource_group.app_runtime.id
  }
}

output "container_app_environment_id" {
  description = "Azure Container Apps environment ID."
  value       = azurerm_container_app_environment.this.id
}

output "container_app_environment_name" {
  description = "Azure Container Apps environment name."
  value       = azurerm_container_app_environment.this.name
}

output "container_app_ids" {
  description = "Container App resource IDs by long-running service key."
  value       = { for key, app in azurerm_container_app.services : key => app.id }
}

output "container_app_fqdns" {
  description = "Container App latest revision FQDNs by long-running service key."
  value       = { for key, app in azurerm_container_app.services : key => app.latest_revision_fqdn }
}

output "container_app_identity_principal_ids" {
  description = "Managed identity principal IDs by long-running service key."
  value       = { for key, identity in azurerm_user_assigned_identity.services : key => identity.principal_id }
}
