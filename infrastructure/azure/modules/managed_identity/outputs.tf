output "client_id" {
  description = "Client ID of the user-assigned managed identity."
  value       = module.managed_identity.client_id
}

output "id" {
  description = "Resource ID of the user-assigned managed identity."
  value       = module.managed_identity.resource_id
}

output "name" {
  description = "Name of the user-assigned managed identity."
  value       = module.managed_identity.resource_name
}

output "principal_id" {
  description = "Principal ID of the user-assigned managed identity."
  value       = module.managed_identity.principal_id
}

output "tenant_id" {
  description = "Tenant ID of the user-assigned managed identity."
  value       = module.managed_identity.tenant_id
}
