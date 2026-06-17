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

output "custom_domain_ids" {
  description = "Front Door managed custom domain IDs by product service key."
  value       = { for key, domain in azurerm_cdn_frontdoor_custom_domain.this : key => domain.id }
}

output "custom_domain_hostnames" {
  description = "Front Door managed custom domain hostnames by product service key."
  value       = { for key, domain in azurerm_cdn_frontdoor_custom_domain.this : key => domain.host_name }
}

output "managed_certificate_validation_records" {
  description = "DNS TXT validation records required by Azure Front Door managed certificates."
  value = {
    for key, domain in azurerm_cdn_frontdoor_custom_domain.this : key => {
      name  = "_dnsauth.${split(".", domain.host_name)[0]}"
      type  = "TXT"
      value = domain.validation_token
    }
  }
}

output "custom_domain_cname_records" {
  description = "DNS CNAME records required to route public product hostnames to Front Door endpoints."
  value = {
    for key, domain in azurerm_cdn_frontdoor_custom_domain.this : key => {
      name  = split(".", domain.host_name)[0]
      type  = "CNAME"
      value = azurerm_cdn_frontdoor_endpoint.this[key].host_name
    }
  }
}

output "origin_group_ids" {
  description = "Front Door origin group IDs by product service key."
  value       = { for key, origin_group in azurerm_cdn_frontdoor_origin_group.this : key => origin_group.id }
}

output "origin_ids" {
  description = "Front Door origin IDs by product service key."
  value       = { for key, origin in azurerm_cdn_frontdoor_origin.this : key => origin.id }
}

output "private_link_origin_approval_requests" {
  description = "Auditable Private Link origin approval details by product service key."
  value = local.private_link_origin.enabled ? {
    for key, endpoint in local.endpoints : key => {
      origin_id              = azurerm_cdn_frontdoor_origin.this[key].id
      private_link_target_id = local.private_link_origin.private_link_target_id
      target_type            = local.private_link_origin.target_type
      location               = local.private_link_origin.location
      request_message        = local.private_link_origin.request_message
      origin_host_name       = endpoint.origin_fqdn
      origin_host_header     = endpoint.origin_fqdn
    }
  } : {}
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
