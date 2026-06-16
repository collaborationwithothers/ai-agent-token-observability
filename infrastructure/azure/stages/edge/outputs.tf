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
