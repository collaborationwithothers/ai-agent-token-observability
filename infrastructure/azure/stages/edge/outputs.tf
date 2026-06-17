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
    edge = azurerm_resource_group.edge.id
  }
}

output "front_door_profile_id" {
  description = "Front Door profile resource ID."
  value       = module.front_door_edge.profile_id
}

output "front_door_profile_name" {
  description = "Front Door profile name."
  value       = module.front_door_edge.profile_name
}

output "front_door_endpoint_ids" {
  description = "Front Door endpoint IDs by product service key."
  value       = module.front_door_edge.endpoint_ids
}

output "front_door_endpoint_hostnames" {
  description = "Front Door default endpoint hostnames by product service key."
  value       = module.front_door_edge.endpoint_hostnames
}

output "front_door_route_ids" {
  description = "Front Door route IDs by product service key."
  value       = module.front_door_edge.route_ids
}

output "front_door_custom_domain_ids" {
  description = "Front Door managed custom domain IDs by product service key."
  value       = module.front_door_edge.custom_domain_ids
}

output "front_door_custom_domain_hostnames" {
  description = "Public Front Door product hostnames by product service key."
  value       = module.front_door_edge.custom_domain_hostnames
}

output "front_door_managed_certificate_validation_records" {
  description = "DNS TXT validation records required for Front Door managed certificates."
  value       = module.front_door_edge.managed_certificate_validation_records
}

output "front_door_custom_domain_cname_records" {
  description = "DNS CNAME records required to route product hostnames to Front Door endpoints."
  value       = module.front_door_edge.custom_domain_cname_records
}

output "public_auth_callback_base_urls" {
  description = "Public Front Door base URLs that must be used for browser-visible authentication callbacks and redirects."
  value = {
    dashboard = "https://${var.public_ingress_hostnames.app}"
    api       = "https://${var.public_ingress_hostnames.api}"
    ingest    = "https://${var.public_ingress_hostnames.ingest}"
  }
}

output "front_door_origin_group_ids" {
  description = "Front Door origin group IDs by product service key."
  value       = module.front_door_edge.origin_group_ids
}

output "front_door_origin_ids" {
  description = "Front Door origin IDs by product service key."
  value       = module.front_door_edge.origin_ids
}

output "front_door_waf_policy_id" {
  description = "Front Door WAF policy resource ID."
  value       = module.front_door_edge.waf_policy_id
}

output "front_door_security_policy_id" {
  description = "Front Door security policy resource ID."
  value       = module.front_door_edge.security_policy_id
}

output "front_door_diagnostic_setting_id" {
  description = "Front Door profile diagnostic setting ID."
  value       = module.front_door_edge.diagnostic_setting_id
}
