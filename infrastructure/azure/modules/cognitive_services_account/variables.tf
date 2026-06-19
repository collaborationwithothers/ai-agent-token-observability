variable "allow_project_management" {
  description = "Whether this account supports Foundry project management child resources."
  type        = bool
  default     = true
}

variable "cognitive_deployments" {
  description = "Cognitive Services model deployments keyed by product deployment alias."
  type = map(object({
    name                       = string
    rai_policy_name            = optional(string)
    version_upgrade_option     = optional(string, "NoAutoUpgrade")
    dynamic_throttling_enabled = optional(bool, false)
    model = object({
      format  = string
      name    = string
      version = optional(string)
    })
    scale = object({
      capacity = optional(number, 1)
      family   = optional(string)
      size     = optional(string)
      tier     = optional(string)
      type     = string
    })
    retry = optional(object({
      error_message_regex  = list(string)
      interval_seconds     = optional(number, 30)
      max_interval_seconds = optional(number, 300)
      multiplier           = optional(number, 1.5)
      randomization_factor = optional(number, 0.3)
    }))
    timeouts = optional(object({
      create = optional(string)
      delete = optional(string)
      read   = optional(string)
      update = optional(string)
    }))
  }))
  default = {}
}

variable "custom_subdomain_name" {
  description = "Custom subdomain used for token-based authentication and private endpoint support."
  type        = string
}

variable "diagnostic_settings" {
  description = "Diagnostic settings for the Cognitive Services account."
  type = map(object({
    name                                     = optional(string, null)
    log_categories                           = optional(set(string), [])
    log_groups                               = optional(set(string), ["allLogs"])
    metric_categories                        = optional(set(string), ["AllMetrics"])
    log_analytics_destination_type           = optional(string, "Dedicated")
    workspace_resource_id                    = optional(string, null)
    storage_account_resource_id              = optional(string, null)
    event_hub_authorization_rule_resource_id = optional(string, null)
    event_hub_name                           = optional(string, null)
    marketplace_partner_resource_id          = optional(string, null)
  }))
  default = {}
}

variable "kind" {
  description = "Cognitive Services account kind."
  type        = string
  default     = "AIServices"

  validation {
    condition     = contains(["AIServices", "OpenAI", "TextAnalytics", "ContentSafety"], var.kind)
    error_message = "kind must be AIServices, OpenAI, TextAnalytics, or ContentSafety."
  }
}

variable "location" {
  description = "Azure region for the Cognitive Services account."
  type        = string
}

variable "managed_identities" {
  description = "Managed identity configuration for the Cognitive Services account."
  type = object({
    system_assigned            = optional(bool, true)
    user_assigned_resource_ids = optional(set(string), [])
  })
  default = {
    system_assigned            = true
    user_assigned_resource_ids = []
  }
}

variable "name" {
  description = "Cognitive Services account name."
  type        = string
}

variable "network_acls" {
  description = "Network ACLs for the Cognitive Services account. Null keeps the stage publicly reachable until private access is explicitly supplied."
  type = object({
    default_action = string
    ip_rules       = optional(set(string))
    virtual_network_rules = optional(set(object({
      ignore_missing_vnet_service_endpoint = optional(bool)
      subnet_id                            = string
    })))
    bypass = optional(string)
  })
  default = null
}

variable "network_injections" {
  description = "Optional Foundry agent network injection configuration."
  type = object({
    subnet_id                         = string
    scenario                          = string
    microsoft_managed_network_enabled = optional(bool, false)
  })
  default = null
}

variable "outbound_network_access_restricted" {
  description = "Whether outbound network access is restricted for the Cognitive Services account."
  type        = bool
  default     = null
}

variable "parent_id" {
  description = "Resource group resource ID where the Cognitive Services account is created."
  type        = string
}

variable "private_endpoints" {
  description = "Private endpoints for the Cognitive Services account."
  type = map(object({
    name = optional(string, null)
    role_assignments = optional(map(object({
      role_definition_id_or_name             = string
      principal_id                           = string
      description                            = optional(string, null)
      skip_service_principal_aad_check       = optional(bool, false)
      condition                              = optional(string, null)
      condition_version                      = optional(string, null)
      delegated_managed_identity_resource_id = optional(string, null)
      principal_type                         = optional(string, null)
    })), {})
    lock = optional(object({
      kind = string
      name = optional(string, null)
    }), null)
    tags                                    = optional(map(string), null)
    subnet_resource_id                      = string
    private_dns_zone_group_name             = optional(string, "default")
    private_dns_zone_resource_ids           = optional(set(string), [])
    application_security_group_associations = optional(map(string), {})
    private_service_connection_name         = optional(string, null)
    network_interface_name                  = optional(string, null)
    location                                = optional(string, null)
    resource_group_name                     = optional(string, null)
    ip_configurations = optional(map(object({
      name               = string
      private_ip_address = string
    })), {})
  }))
  default = {}
}

variable "private_endpoints_manage_dns_zone_group" {
  description = "Whether the AVM module manages private DNS zone groups for supplied private endpoints."
  type        = bool
  default     = true
}

variable "public_network_access_enabled" {
  description = "Whether public network access is enabled for the Cognitive Services account."
  type        = bool
  default     = true
}

variable "rai_policies" {
  description = "RAI policies for model deployments."
  type = map(object({
    name             = string
    base_policy_name = string
    mode             = string
    content_filters = optional(list(object({
      blocking           = bool
      enabled            = bool
      name               = string
      severity_threshold = string
      source             = string
    })))
    custom_block_lists = optional(list(object({
      source          = string
      block_list_name = string
      blocking        = bool
    })))
  }))
  default = {}
}

variable "role_assignments" {
  description = "Azure RBAC role assignments scoped to the Cognitive Services account."
  type = map(object({
    role_definition_id_or_name             = string
    principal_id                           = string
    description                            = optional(string, null)
    skip_service_principal_aad_check       = optional(bool, false)
    condition                              = optional(string, null)
    condition_version                      = optional(string, null)
    delegated_managed_identity_resource_id = optional(string, null)
    principal_type                         = optional(string, null)
  }))
  default = {}
}

variable "sku_name" {
  description = "SKU name for the Cognitive Services account."
  type        = string
  default     = "S0"
}

variable "tags" {
  description = "Tags assigned to the Cognitive Services account."
  type        = map(string)
  default     = {}
}
