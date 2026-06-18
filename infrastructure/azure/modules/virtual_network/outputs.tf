output "id" {
  description = "Resource ID of the virtual network."
  value       = module.virtual_network.resource_id
}

output "name" {
  description = "Name of the virtual network."
  value       = module.virtual_network.name
}

output "resource" {
  description = "Virtual network resource exported by the AVM module."
  value       = module.virtual_network.resource
}

output "subnets" {
  description = "Subnets exported by the virtual network module."
  value       = module.virtual_network.subnets
}
