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

output "container_app_environment_public_network_access" {
  description = "Configured public network access mode for the Container Apps environment."
  value       = azurerm_container_app_environment.this.public_network_access
}

output "container_app_ids" {
  description = "Container App resource IDs by long-running service key."
  value       = { for key, app in azurerm_container_app.services : key => app.id }
}

output "container_app_fqdns" {
  description = "Stable generated Container App ingress FQDNs by long-running service key."
  value       = { for key, app in azurerm_container_app.services : key => app.ingress[0].fqdn }
}

output "direct_origin_validation_targets" {
  description = "Generated ACA FQDNs and expected origin evidence result for edge-origin validation."
  value = {
    public_network_access = azurerm_container_app_environment.this.public_network_access
    expected_result       = "Front Door origin health should succeed against these generated ACA FQDN origins. Direct-origin blocking is deferred hardening."
    fqdns                 = { for key, app in azurerm_container_app.services : key => app.ingress[0].fqdn }
  }
}

output "container_app_identity_principal_ids" {
  description = "Managed identity principal IDs by long-running service key."
  value       = { for key, identity in azurerm_user_assigned_identity.services : key => identity.principal_id }
}

output "container_app_job_ids" {
  description = "Container Apps Job resource IDs by finite job key."
  value       = { for key, job in module.container_app_jobs : key => job.resource_id }
}

output "container_app_job_names" {
  description = "Container Apps Job names by finite job key."
  value       = { for key, job in module.container_app_jobs : key => job.name }
}

output "container_app_job_identities" {
  description = "User-assigned managed identity details by finite job key."
  value = {
    for key, identity in azurerm_user_assigned_identity.jobs : key => {
      client_id    = identity.client_id
      id           = identity.id
      name         = identity.name
      principal_id = identity.principal_id
      tenant_id    = identity.tenant_id
    }
  }
}
