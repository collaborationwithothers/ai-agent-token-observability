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
    network = module.network_resource_group.id
  }
}

output "network_resource_group_name" {
  description = "Name of the resource group containing private data plane network resources."
  value       = module.network_resource_group.name
}

output "virtual_network_id" {
  description = "Resource ID of the private data plane virtual network."
  value       = module.virtual_network.id
}

output "virtual_network_name" {
  description = "Name of the private data plane virtual network."
  value       = module.virtual_network.name
}

output "virtual_network_address_space" {
  description = "Address space assigned to the private data plane virtual network."
  value       = var.virtual_network_address_space
}

output "subnet_ids" {
  description = "Subnet resource IDs by stable downstream contract key."
  value       = { for key, subnet in module.virtual_network.subnets : key => subnet.resource_id }
}

output "subnet_names" {
  description = "Subnet names by stable downstream contract key."
  value       = { for key, subnet in local.subnet_definitions : key => subnet.name }
}

output "subnet_address_prefixes" {
  description = "Subnet address prefixes by stable downstream contract key."
  value       = { for key, subnet in local.subnet_definitions : key => subnet.address_prefix }
}

output "subnet_purposes" {
  description = "Non-secret subnet purpose descriptions by stable downstream contract key."
  value       = { for key, subnet in local.subnet_definitions : key => subnet.purpose }
}

output "network_security_group_ids" {
  description = "Network Security Group resource IDs by subnet boundary key."
  value       = { for key, nsg in module.network_security_groups : key => nsg.id }
}

output "network_security_group_names" {
  description = "Network Security Group names by subnet boundary key."
  value       = { for key, nsg in module.network_security_groups : key => nsg.name }
}

output "private_dns_zone_ids" {
  description = "Private DNS zone resource IDs by stable downstream contract key."
  value       = { for key, zone in module.private_dns_zones : key => zone.id }
}

output "private_dns_zone_names" {
  description = "Private DNS zone names by stable downstream contract key."
  value       = { for key, zone in module.private_dns_zones : key => zone.name }
}

output "private_dns_zone_purposes" {
  description = "Non-secret private DNS zone purpose descriptions by stable downstream contract key."
  value       = { for key, zone in local.private_dns_zones : key => zone.purpose }
}
