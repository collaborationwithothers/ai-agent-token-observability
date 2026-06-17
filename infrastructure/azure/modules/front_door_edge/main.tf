locals {
  waf_policy_name = replace("${var.name_prefix}-waf", "-", "")

  private_link_origin = {
    enabled                = var.private_link_origin.enabled
    private_link_target_id = try(var.private_link_origin.private_link_target_id, null)
    location               = coalesce(try(var.private_link_origin.location, null), var.location)
    request_message        = coalesce(try(var.private_link_origin.request_message, null), "Access request for CDN FrontDoor Private Link Origin")
    target_type            = coalesce(try(var.private_link_origin.target_type, null), "managedEnvironments")
  }

  endpoints = {
    product_dashboard = {
      name             = "${var.name_prefix}-app"
      public_hostname  = var.public_ingress_hostnames.app
      origin_fqdn      = var.container_app_fqdns.product_dashboard
      health_path      = "/"
      rate_limit_group = "app"
    }
    product_api = {
      name             = "${var.name_prefix}-api"
      public_hostname  = var.public_ingress_hostnames.api
      origin_fqdn      = var.container_app_fqdns.product_api
      health_path      = "/health/ready"
      rate_limit_group = "app"
    }
    product_ingestion_endpoint = {
      name             = "${var.name_prefix}-ingest"
      public_hostname  = var.public_ingress_hostnames.ingest
      origin_fqdn      = var.container_app_fqdns.product_ingestion_endpoint
      health_path      = "/health/ready"
      rate_limit_group = "ingestion"
    }
  }

  endpoint_hostnames = {
    for key, endpoint in azurerm_cdn_frontdoor_endpoint.this : key => endpoint.host_name
  }

  app_rate_limit_hosts = compact([
    lookup(var.public_ingress_hostnames, "app", ""),
    lookup(var.public_ingress_hostnames, "api", ""),
    local.endpoint_hostnames.product_dashboard,
    local.endpoint_hostnames.product_api
  ])

  ingestion_rate_limit_hosts = compact([
    lookup(var.public_ingress_hostnames, "ingest", ""),
    local.endpoint_hostnames.product_ingestion_endpoint
  ])
}

resource "azurerm_cdn_frontdoor_profile" "this" {
  name                     = "${var.name_prefix}-afd"
  resource_group_name      = var.resource_group_name
  sku_name                 = var.front_door_sku
  response_timeout_seconds = 120
  tags                     = var.tags

  log_scrubbing_rule {
    match_variable = "RequestIPAddress"
  }
}

resource "azurerm_cdn_frontdoor_endpoint" "this" {
  for_each = local.endpoints

  name                     = each.value.name
  cdn_frontdoor_profile_id = azurerm_cdn_frontdoor_profile.this.id
  enabled                  = true
  tags                     = var.tags
}

resource "azurerm_cdn_frontdoor_origin_group" "this" {
  for_each = local.endpoints

  name                     = "${each.value.name}-og"
  cdn_frontdoor_profile_id = azurerm_cdn_frontdoor_profile.this.id

  health_probe {
    interval_in_seconds = 60
    path                = each.value.health_path
    protocol            = "Https"
    request_type        = "GET"
  }

  load_balancing {
    additional_latency_in_milliseconds = 0
    sample_size                        = 4
    successful_samples_required        = 3
  }
}

resource "azurerm_cdn_frontdoor_origin" "this" {
  for_each = local.endpoints

  name                          = "${each.value.name}-origin"
  cdn_frontdoor_origin_group_id = azurerm_cdn_frontdoor_origin_group.this[each.key].id
  enabled                       = true

  certificate_name_check_enabled = true
  host_name                      = each.value.origin_fqdn
  origin_host_header             = each.value.origin_fqdn
  http_port                      = 80
  https_port                     = 443
  priority                       = 1
  weight                         = 500

  dynamic "private_link" {
    for_each = local.private_link_origin.enabled ? [local.private_link_origin] : []

    content {
      private_link_target_id = private_link.value.private_link_target_id
      location               = private_link.value.location
      request_message        = private_link.value.request_message
      target_type            = private_link.value.target_type
    }
  }
}

resource "azurerm_cdn_frontdoor_custom_domain" "this" {
  for_each = local.endpoints

  name                     = "${each.value.name}-domain"
  cdn_frontdoor_profile_id = azurerm_cdn_frontdoor_profile.this.id
  dns_zone_id              = try(var.azure_dns_zone.id, null)
  host_name                = each.value.public_hostname

  tls {
    certificate_type = "ManagedCertificate"
    minimum_version  = "TLS12"
  }
}

resource "azurerm_cdn_frontdoor_route" "this" {
  for_each = local.endpoints

  name                          = "${each.value.name}-route"
  cdn_frontdoor_endpoint_id     = azurerm_cdn_frontdoor_endpoint.this[each.key].id
  cdn_frontdoor_origin_group_id = azurerm_cdn_frontdoor_origin_group.this[each.key].id
  cdn_frontdoor_origin_ids      = [azurerm_cdn_frontdoor_origin.this[each.key].id]
  cdn_frontdoor_custom_domain_ids = [
    azurerm_cdn_frontdoor_custom_domain.this[each.key].id
  ]
  enabled = true

  forwarding_protocol    = "HttpsOnly"
  https_redirect_enabled = true
  link_to_default_domain = false
  patterns_to_match      = ["/*"]
  supported_protocols    = ["Http", "Https"]
}

resource "azurerm_cdn_frontdoor_firewall_policy" "this" {
  name                              = local.waf_policy_name
  resource_group_name               = var.resource_group_name
  sku_name                          = azurerm_cdn_frontdoor_profile.this.sku_name
  enabled                           = true
  mode                              = var.waf_policy_mode
  request_body_check_enabled        = true
  custom_block_response_status_code = 403
  tags                              = var.tags

  custom_rule {
    name                           = "AppTrafficRateLimit"
    action                         = "Block"
    enabled                        = true
    priority                       = 100
    type                           = "RateLimitRule"
    rate_limit_duration_in_minutes = var.waf_rate_limits.app.duration_in_minutes
    rate_limit_threshold           = var.waf_rate_limits.app.threshold

    match_condition {
      match_variable     = "RequestHeader"
      selector           = "Host"
      operator           = "Equal"
      negation_condition = false
      match_values       = local.app_rate_limit_hosts
      transforms         = ["Lowercase", "Trim"]
    }
  }

  custom_rule {
    name                           = "IngestionTrafficRateLimit"
    action                         = "Block"
    enabled                        = true
    priority                       = 110
    type                           = "RateLimitRule"
    rate_limit_duration_in_minutes = var.waf_rate_limits.ingestion.duration_in_minutes
    rate_limit_threshold           = var.waf_rate_limits.ingestion.threshold

    match_condition {
      match_variable     = "RequestHeader"
      selector           = "Host"
      operator           = "Equal"
      negation_condition = false
      match_values       = local.ingestion_rate_limit_hosts
      transforms         = ["Lowercase", "Trim"]
    }
  }

  managed_rule {
    type    = "Microsoft_DefaultRuleSet"
    version = "2.1"
    action  = "Block"
  }

  managed_rule {
    type    = "Microsoft_BotManagerRuleSet"
    version = "1.1"
    action  = "Block"
  }
}

resource "azurerm_cdn_frontdoor_security_policy" "this" {
  name                     = "${var.name_prefix}-security"
  cdn_frontdoor_profile_id = azurerm_cdn_frontdoor_profile.this.id

  security_policies {
    firewall {
      cdn_frontdoor_firewall_policy_id = azurerm_cdn_frontdoor_firewall_policy.this.id

      association {
        patterns_to_match = ["/*"]

        dynamic "domain" {
          for_each = merge(
            { for key, endpoint in azurerm_cdn_frontdoor_endpoint.this : "default-${key}" => endpoint.id },
            { for key, domain in azurerm_cdn_frontdoor_custom_domain.this : "custom-${key}" => domain.id }
          )

          content {
            cdn_frontdoor_domain_id = domain.value
          }
        }
      }
    }
  }
}

resource "azurerm_dns_txt_record" "front_door_domain_validation" {
  for_each = try(var.azure_dns_zone.manage_records, false) ? azurerm_cdn_frontdoor_custom_domain.this : {}

  name                = "_dnsauth.${split(".", each.value.host_name)[0]}"
  zone_name           = var.azure_dns_zone.name
  resource_group_name = var.azure_dns_zone.resource_group_name
  ttl                 = 3600
  tags                = var.tags

  record {
    value = each.value.validation_token
  }
}

resource "azurerm_dns_cname_record" "front_door_custom_domain" {
  for_each = try(var.azure_dns_zone.manage_records, false) ? azurerm_cdn_frontdoor_custom_domain.this : {}

  depends_on = [
    azurerm_cdn_frontdoor_route.this,
    azurerm_cdn_frontdoor_security_policy.this
  ]

  name                = split(".", each.value.host_name)[0]
  zone_name           = var.azure_dns_zone.name
  resource_group_name = var.azure_dns_zone.resource_group_name
  ttl                 = 3600
  record              = azurerm_cdn_frontdoor_endpoint.this[each.key].host_name
  tags                = var.tags
}

resource "azurerm_monitor_diagnostic_setting" "front_door_profile" {
  name                           = "${var.name_prefix}-afd-diag"
  target_resource_id             = azurerm_cdn_frontdoor_profile.this.id
  log_analytics_workspace_id     = var.log_analytics_workspace_id
  log_analytics_destination_type = "Dedicated"

  enabled_log {
    category = "FrontDoorAccessLog"
  }

  enabled_log {
    category = "FrontDoorHealthProbeLog"
  }

  enabled_log {
    category = "FrontDoorWebApplicationFirewallLog"
  }

  enabled_metric {
    category = "AllMetrics"
  }
}
