variable "name_prefix" {
  description = "Stable lowercase resource name prefix for Front Door resources."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9][a-z0-9-]{1,40}[a-z0-9]$", var.name_prefix))
    error_message = "name_prefix must be lowercase alphanumeric or hyphen, start and end with alphanumeric, and be 3 to 42 characters."
  }
}

variable "resource_group_name" {
  description = "Resource group name for Front Door edge resources."
  type        = string
}

variable "location" {
  description = "Azure location for regional Front Door child resources."
  type        = string
}

variable "tags" {
  description = "Tags applied to Front Door edge resources."
  type        = map(string)
}

variable "front_door_sku" {
  description = "Front Door SKU. Production must use Premium_AzureFrontDoor."
  type        = string

  validation {
    condition     = contains(["Premium_AzureFrontDoor"], var.front_door_sku)
    error_message = "front_door_sku must be Premium_AzureFrontDoor for this production edge."
  }
}

variable "waf_policy_mode" {
  description = "Front Door WAF policy mode."
  type        = string

  validation {
    condition     = contains(["Detection", "Prevention"], var.waf_policy_mode)
    error_message = "waf_policy_mode must be Detection or Prevention."
  }
}

variable "waf_rate_limits" {
  description = "Rate-limit thresholds for app and ingestion WAF custom rules."
  type = object({
    app = object({
      duration_in_minutes = number
      threshold           = number
    })
    ingestion = object({
      duration_in_minutes = number
      threshold           = number
    })
  })

  validation {
    condition = alltrue([
      var.waf_rate_limits.app.duration_in_minutes >= 1,
      var.waf_rate_limits.ingestion.duration_in_minutes >= 1,
      var.waf_rate_limits.app.threshold >= 1,
      var.waf_rate_limits.ingestion.threshold >= 1
    ])
    error_message = "waf_rate_limits durations and thresholds must be positive."
  }
}

variable "public_ingress_hostnames" {
  description = "First-release product hostnames by app, api, and ingest key."
  type = object({
    app    = string
    api    = string
    ingest = string
  })

  validation {
    condition = alltrue([
      can(regex("^[a-z0-9][a-z0-9.-]*[a-z0-9]$", var.public_ingress_hostnames.app)),
      can(regex("^[a-z0-9][a-z0-9.-]*[a-z0-9]$", var.public_ingress_hostnames.api)),
      can(regex("^[a-z0-9][a-z0-9.-]*[a-z0-9]$", var.public_ingress_hostnames.ingest))
    ])
    error_message = "public_ingress_hostnames must include valid app, api, and ingest hostnames."
  }
}

variable "container_app_fqdns" {
  description = "Container App generated FQDNs by app_runtime output key."
  type        = map(string)

  validation {
    condition = alltrue([
      contains(keys(var.container_app_fqdns), "product_dashboard"),
      contains(keys(var.container_app_fqdns), "product_api"),
      contains(keys(var.container_app_fqdns), "product_ingestion_endpoint"),
      alltrue([for fqdn in values(var.container_app_fqdns) : can(regex("^[A-Za-z0-9][A-Za-z0-9.-]*[A-Za-z0-9]$", fqdn))])
    ])
    error_message = "container_app_fqdns must include product_dashboard, product_api, and product_ingestion_endpoint FQDNs."
  }
}

variable "azure_dns_zone" {
  description = "Optional delegated Azure DNS zone used to create Front Door custom domain validation TXT and CNAME records."
  type = object({
    id                  = optional(string)
    name                = optional(string)
    resource_group_name = optional(string)
    manage_records      = optional(bool, false)
  })
  default = null

  validation {
    condition = var.azure_dns_zone == null || !try(var.azure_dns_zone.manage_records, false) || alltrue([
      can(regex("^/subscriptions/[^/]+/resourceGroups/[^/]+/providers/Microsoft\\.Network/dnsZones/[^/]+$", coalesce(try(var.azure_dns_zone.id, null), ""))),
      coalesce(try(var.azure_dns_zone.name, null), "") != "",
      coalesce(try(var.azure_dns_zone.resource_group_name, null), "") != ""
    ])
    error_message = "azure_dns_zone must include id, name, and resource_group_name when manage_records is true."
  }
}

variable "private_link_origin" {
  description = "Optional Front Door Private Link configuration for Container Apps managed environment origins."
  type = object({
    enabled                = bool
    private_link_target_id = optional(string)
    location               = optional(string)
    request_message        = optional(string)
    target_type            = optional(string)
  })
  default = {
    enabled = false
  }

  validation {
    condition = !var.private_link_origin.enabled || alltrue([
      can(regex("^/subscriptions/[^/]+/resourceGroups/[^/]+/providers/Microsoft\\.App/managedEnvironments/[^/]+$", coalesce(try(var.private_link_origin.private_link_target_id, null), ""))),
      contains(["managedEnvironments"], coalesce(try(var.private_link_origin.target_type, null), "managedEnvironments")),
      length(coalesce(try(var.private_link_origin.request_message, null), "Access request for CDN FrontDoor Private Link Origin")) >= 1,
      length(coalesce(try(var.private_link_origin.request_message, null), "Access request for CDN FrontDoor Private Link Origin")) <= 140
    ])
    error_message = "private_link_origin must target a Container Apps managed environment with target_type managedEnvironments and a 1 to 140 character request message when enabled."
  }
}

variable "log_analytics_workspace_id" {
  description = "Log Analytics workspace ID for Front Door logs and metrics."
  type        = string

  validation {
    condition     = can(regex("^/subscriptions/[^/]+/resourceGroups/[^/]+/providers/Microsoft\\.OperationalInsights/workspaces/[^/]+$", var.log_analytics_workspace_id))
    error_message = "log_analytics_workspace_id must be a Log Analytics workspace resource ID."
  }
}
