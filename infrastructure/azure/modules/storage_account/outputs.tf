output "container_names" {
  description = "Storage container names by key."
  value       = { for key, container in module.storage_account.containers : key => container.name }
}

output "container_resource_ids" {
  description = "Storage container resource IDs by key."
  value       = { for key, container in module.storage_account.containers : key => container.id }
}

output "fqdn" {
  description = "Storage service FQDNs by service."
  value       = module.storage_account.fqdn
}

output "name" {
  description = "Name of the Storage Account."
  value       = module.storage_account.name
}

output "resource_id" {
  description = "Resource ID of the Storage Account."
  value       = module.storage_account.resource_id
}
