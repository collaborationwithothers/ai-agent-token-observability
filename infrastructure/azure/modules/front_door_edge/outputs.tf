output "profile_id" {
  description = "Front Door profile resource ID."
  value       = azurerm_cdn_frontdoor_profile.this.id
}

output "profile_name" {
  description = "Front Door profile name."
  value       = azurerm_cdn_frontdoor_profile.this.name
}

output "endpoint_ids" {
  description = "Front Door endpoint IDs by product service key."
  value       = { for key, endpoint in azurerm_cdn_frontdoor_endpoint.this : key => endpoint.id }
}

output "endpoint_hostnames" {
  description = "Front Door default endpoint hostnames by product service key."
  value       = { for key, endpoint in azurerm_cdn_frontdoor_endpoint.this : key => endpoint.host_name }
}

output "route_ids" {
  description = "Front Door route IDs by product service key."
  value       = { for key, route in azurerm_cdn_frontdoor_route.this : key => route.id }
}

output "origin_group_ids" {
  description = "Front Door origin group IDs by product service key."
  value       = { for key, origin_group in azurerm_cdn_frontdoor_origin_group.this : key => origin_group.id }
}

output "origin_ids" {
  description = "Front Door origin IDs by product service key."
  value       = { for key, origin in azurerm_cdn_frontdoor_origin.this : key => origin.id }
}

output "waf_policy_id" {
  description = "Front Door WAF policy resource ID."
  value       = azurerm_cdn_frontdoor_firewall_policy.this.id
}

output "security_policy_id" {
  description = "Front Door security policy resource ID."
  value       = azurerm_cdn_frontdoor_security_policy.this.id
}

output "diagnostic_setting_id" {
  description = "Front Door profile diagnostic setting ID."
  value       = azurerm_monitor_diagnostic_setting.front_door_profile.id
}
