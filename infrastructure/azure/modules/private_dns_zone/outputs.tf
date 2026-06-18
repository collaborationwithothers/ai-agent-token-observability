output "id" {
  description = "Resource ID of the private DNS zone."
  value       = module.private_dns_zone.resource_id
}

output "name" {
  description = "Name of the private DNS zone."
  value       = module.private_dns_zone.name
}

output "virtual_network_link_outputs" {
  description = "Virtual network link outputs exported by the private DNS zone module."
  value       = module.private_dns_zone.virtual_network_link_outputs
}
