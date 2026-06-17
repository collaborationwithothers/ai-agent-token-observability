output "id" {
  description = "Resource ID of the Key Vault."
  value       = module.key_vault.resource_id
}

output "name" {
  description = "Name of the Key Vault."
  value       = module.key_vault.name
}

output "resource_group_name" {
  description = "Resource group name containing the Key Vault."
  value       = var.resource_group_name
}

output "uri" {
  description = "URI of the Key Vault."
  value       = module.key_vault.uri
}
