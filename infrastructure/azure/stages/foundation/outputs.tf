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
    foundation = azurerm_resource_group.foundation.id
  }
}

output "container_registry_id" {
  description = "Resource ID of the shared Azure Container Registry."
  value       = azurerm_container_registry.shared.id
}

output "container_registry_name" {
  description = "Name of the shared Azure Container Registry."
  value       = azurerm_container_registry.shared.name
}

output "container_registry_login_server" {
  description = "Login server of the shared Azure Container Registry used by the image publish workflow."
  value       = azurerm_container_registry.shared.login_server
}

output "container_registry_resource_group_id" {
  description = "Resource ID of the resource group containing the shared Azure Container Registry."
  value       = azurerm_resource_group.foundation.id
}

output "key_vault_id" {
  description = "Resource ID of the shared foundation Key Vault."
  value       = module.key_vault.id
}

output "key_vault_name" {
  description = "Name of the shared foundation Key Vault."
  value       = module.key_vault.name
}

output "key_vault_resource_group_name" {
  description = "Resource group name containing the shared foundation Key Vault."
  value       = module.key_vault.resource_group_name
}

output "key_vault_uri" {
  description = "URI of the shared foundation Key Vault."
  value       = module.key_vault.uri
}

output "deployment_identities" {
  description = "Non-secret deployment identity references owned or accepted by the foundation stage."
  value = merge(
    {
      for key, identity in module.deployment_identity : key => {
        client_id    = identity.client_id
        id           = identity.id
        name         = identity.name
        principal_id = identity.principal_id
        tenant_id    = identity.tenant_id
      }
    },
    {
      for key, identity in var.deployment_identity_references : key => {
        client_id    = identity.client_id
        id           = identity.id
        name         = identity.name
        principal_id = identity.principal_id
        tenant_id    = identity.tenant_id
      }
    }
  )
}
