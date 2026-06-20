variable "environment" {
  description = "Deployment environment code."
  type        = string

  validation {
    condition     = contains(["dv", "qa", "pp", "pd"], var.environment)
    error_message = "environment must be one of dv, qa, pp, or pd."
  }
}

variable "azure_region" {
  description = "Lowercase Azure region name for this stage."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9]+$", var.azure_region))
    error_message = "azure_region must be a lowercase Azure region name."
  }
}

variable "customer_organization_slug" {
  description = "Lowercase customer organization slug. Use internal for first release."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9][a-z0-9-]*[a-z0-9]$|^[a-z0-9]$", var.customer_organization_slug))
    error_message = "customer_organization_slug must be lowercase URL-safe."
  }
}

variable "terraform_workspace_name" {
  description = "Optional workspace name supplied by automation for validation against the environment, region, and customer organization slug contract."
  type        = string
  default     = null

  validation {
    condition     = var.terraform_workspace_name == null || var.terraform_workspace_name != "default"
    error_message = "terraform_workspace_name must not be default."
  }
}

variable "resource_instance" {
  description = "Stable short resource instance identifier."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9][a-z0-9-]{0,11}$", var.resource_instance))
    error_message = "resource_instance must be lowercase alphanumeric or hyphen, starting with alphanumeric, and at most 12 characters."
  }
}

variable "tags" {
  description = "Common tags for resources created by this stage."
  type        = map(string)

  validation {
    condition = alltrue([
      contains(keys(var.tags), "environment"),
      contains(keys(var.tags), "region"),
      contains(keys(var.tags), "product"),
      contains(keys(var.tags), "owner"),
      contains(keys(var.tags), "data_classification"),
      contains(keys(var.tags), "managed_by")
    ])
    error_message = "tags must include environment, region, product, owner, data_classification, and managed_by."
  }
}

variable "allowed_regions" {
  description = "Allowed Azure regions for this repository."
  type        = list(string)
  default     = ["eastus", "eastus2", "westeurope"]
}

variable "allowed_customer_organization_slugs" {
  description = "Allowed customer organization slugs for this repository."
  type        = list(string)
  default     = ["internal"]
}

variable "enable_zone_redundancy" {
  description = "Whether this stage should enable zone redundancy for resources that support it."
  type        = bool
  default     = false
}

variable "public_ingress_hostnames" {
  description = "Public ingress hostnames used by stages that expose product traffic."
  type = object({
    app    = string
    api    = string
    ingest = string
  })
  default = {
    app    = "app.tokenobs.consultwithcloud.com"
    api    = "api.tokenobs.consultwithcloud.com"
    ingest = "ingest.tokenobs.consultwithcloud.com"
  }

  validation {
    condition = alltrue([
      contains(keys(var.public_ingress_hostnames), "app"),
      contains(keys(var.public_ingress_hostnames), "api"),
      contains(keys(var.public_ingress_hostnames), "ingest"),
      alltrue([for hostname in values(var.public_ingress_hostnames) : can(regex("^[a-z0-9][a-z0-9.-]*[a-z0-9]$", hostname))]),
      var.public_ingress_hostnames.app == "app.tokenobs.consultwithcloud.com",
      var.public_ingress_hostnames.api == "api.tokenobs.consultwithcloud.com",
      var.public_ingress_hostnames.ingest == "ingest.tokenobs.consultwithcloud.com"
    ])
    error_message = "public_ingress_hostnames must be the first-release app, api, and ingest hostnames under tokenobs.consultwithcloud.com."
  }
}

variable "azure_dns_zone" {
  description = "Optional delegated Azure DNS zone for product hostname TXT validation and CNAME records."
  type = object({
    id                  = optional(string)
    name                = optional(string)
    resource_group_name = optional(string)
    manage_records      = optional(bool, false)
  })
  default = null

  validation {
    condition = var.azure_dns_zone == null || !try(var.azure_dns_zone.manage_records, false) || alltrue([
      can(regex("^/subscriptions/[^/]+/resourceGroups/[^/]+/providers/Microsoft\\.Network/dnsZones/tokenobs\\.consultwithcloud\\.com$", coalesce(try(var.azure_dns_zone.id, null), ""))),
      coalesce(try(var.azure_dns_zone.name, null), "") == "tokenobs.consultwithcloud.com",
      coalesce(try(var.azure_dns_zone.resource_group_name, null), "") != ""
    ])
    error_message = "azure_dns_zone must be the delegated tokenobs.consultwithcloud.com Azure DNS zone when manage_records is true."
  }
}

variable "edge_resource_group_name" {
  description = "Optional explicit resource group name for edge resources. When null, a deterministic stage name is used."
  type        = string
  default     = null

  validation {
    condition     = var.edge_resource_group_name == null || can(regex("^[A-Za-z0-9._() -]{1,90}$", var.edge_resource_group_name))
    error_message = "edge_resource_group_name must be a valid Azure resource group name when supplied."
  }
}

variable "front_door_sku" {
  description = "Front Door SKU for the production edge."
  type        = string
  default     = "Premium_AzureFrontDoor"

  validation {
    condition     = var.front_door_sku == "Premium_AzureFrontDoor"
    error_message = "front_door_sku must be Premium_AzureFrontDoor."
  }
}

variable "waf_policy_mode" {
  description = "Optional Front Door WAF policy mode. Defaults to Detection in dv/qa and Prevention in pp/pd."
  type        = string
  default     = null

  validation {
    condition     = var.waf_policy_mode == null || contains(["Detection", "Prevention"], var.waf_policy_mode)
    error_message = "waf_policy_mode must be Detection or Prevention when supplied."
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
  default = {
    app = {
      duration_in_minutes = 1
      threshold           = 600
    }
    ingestion = {
      duration_in_minutes = 1
      threshold           = 300
    }
  }
}

variable "container_app_fqdns" {
  description = "Container App generated FQDNs by app_runtime output key."
  type        = map(string)
}

variable "container_app_environment_id" {
  description = "Azure Container Apps environment ID from app_runtime output."
  type        = string

  validation {
    condition     = can(regex("^/subscriptions/[^/]+/resourceGroups/[^/]+/providers/Microsoft\\.App/managedEnvironments/[^/]+$", var.container_app_environment_id))
    error_message = "container_app_environment_id must be an Azure Container Apps managed environment resource ID."
  }
}

variable "container_app_environment_public_network_access" {
  description = "Configured Container Apps environment public network access from app_runtime output. Direct-origin blocking remains deferred until the origin isolation hardening slice is reintroduced."
  type        = string

  validation {
    condition     = contains(["Enabled", "Disabled"], var.container_app_environment_public_network_access)
    error_message = "container_app_environment_public_network_access must be Enabled or Disabled."
  }
}

variable "diagnostic_destinations" {
  description = "Non-secret diagnostic destination contracts from observability_foundation."
  type = map(object({
    log_analytics_workspace_resource_id = optional(string)
    application_insights_resource_id    = optional(string)
    destination_type                    = string
    expected_log_groups                 = optional(list(string))
    expected_log_categories             = optional(list(string))
    expected_metric_categories          = list(string)
    consumer_stage                      = string
  }))

  validation {
    condition = (
      can(var.diagnostic_destinations["front_door"]) &&
      var.diagnostic_destinations["front_door"].consumer_stage == "edge" &&
      var.diagnostic_destinations["front_door"].log_analytics_workspace_resource_id != null
    )
    error_message = "diagnostic_destinations must include front_door for edge with a Log Analytics workspace resource ID."
  }
}

variable "log_analytics_workspace_id" {
  description = "Log Analytics workspace ID used for Front Door access, health probe, WAF logs, and metrics."
  type        = string
  default     = null

  validation {
    condition     = var.log_analytics_workspace_id == null || can(regex("^/subscriptions/[^/]+/resourceGroups/[^/]+/providers/Microsoft\\.OperationalInsights/workspaces/[^/]+$", var.log_analytics_workspace_id))
    error_message = "log_analytics_workspace_id must be a Log Analytics workspace resource ID when supplied."
  }
}
