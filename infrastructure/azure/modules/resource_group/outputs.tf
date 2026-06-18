output "id" {
  description = "Resource ID of the resource group."
  value       = module.resource_group.resource_id
}

output "location" {
  description = "Azure region of the resource group."
  value       = module.resource_group.location
}

output "name" {
  description = "Name of the resource group."
  value       = module.resource_group.name
}
