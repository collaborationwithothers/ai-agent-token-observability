output "id" {
  description = "Resource ID of the Network Security Group."
  value       = module.network_security_group.resource_id
}

output "name" {
  description = "Name of the Network Security Group."
  value       = module.network_security_group.name
}

output "security_rules" {
  description = "Security rules exported by the Network Security Group module."
  value       = module.network_security_group.security_rules
}
