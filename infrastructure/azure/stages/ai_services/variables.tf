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
    condition     = contains(var.allowed_regions, var.azure_region)
    error_message = "azure_region must be included in allowed_regions."
  }
}

variable "customer_organization_slug" {
  description = "Lowercase customer organization slug. Use internal for first release."
  type        = string

  validation {
    condition     = contains(var.allowed_customer_organization_slugs, var.customer_organization_slug) && can(regex("^[a-z0-9][a-z0-9-]*[a-z0-9]$|^[a-z0-9]$", var.customer_organization_slug))
    error_message = "customer_organization_slug must be allowed and lowercase URL-safe."
  }
}

variable "terraform_workspace_name" {
  description = "Optional workspace name supplied by automation for validation against the environment, region, and customer organization slug contract."
  type        = string
  default     = null

  validation {
    condition     = var.terraform_workspace_name == null || var.terraform_workspace_name == "${var.environment}_${var.azure_region}_${var.customer_organization_slug}"
    error_message = "terraform_workspace_name must match {environment}_{azureRegion}_{customerOrganizationSlug}."
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
  type        = map(string)
  default     = {}
}

variable "ai_resource_group_name" {
  description = "Optional explicit resource group name for AI service resources. When null, a deterministic stage name is used."
  type        = string
  default     = null

  validation {
    condition     = var.ai_resource_group_name == null || can(regex("^[A-Za-z0-9._() -]{1,90}$", var.ai_resource_group_name))
    error_message = "ai_resource_group_name must be a valid Azure resource group name when supplied."
  }
}

variable "diagnostic_destinations" {
  description = "Diagnostic destination contracts from observability_foundation outputs."
  type = object({
    ai_services = optional(object({
      log_analytics_workspace_resource_id = string
      application_insights_resource_id    = optional(string)
      destination_type                    = optional(string, "Dedicated")
    }))
  })
  default = {}
}

variable "ai_services_account_name" {
  description = "Optional explicit Azure AI Services account name. When null, a deterministic stage name is used."
  type        = string
  default     = null

  validation {
    condition     = var.ai_services_account_name == null || can(regex("^[a-zA-Z0-9][a-zA-Z0-9-]{1,62}[a-zA-Z0-9]$", var.ai_services_account_name))
    error_message = "ai_services_account_name must be 3 to 64 letters, numbers, or hyphens when supplied."
  }
}

variable "ai_services_custom_subdomain_name" {
  description = "Optional explicit custom subdomain for token-based authentication and private endpoint support. When null, the account name is used."
  type        = string
  default     = null

  validation {
    condition     = var.ai_services_custom_subdomain_name == null || can(regex("^[a-zA-Z0-9][a-zA-Z0-9-]{1,62}[a-zA-Z0-9]$", var.ai_services_custom_subdomain_name))
    error_message = "ai_services_custom_subdomain_name must be 3 to 64 letters, numbers, or hyphens when supplied."
  }
}

variable "ai_services_sku_name" {
  description = "SKU name for the Azure AI Services account."
  type        = string
  default     = "S0"
}

variable "ai_services_public_network_access_enabled" {
  description = "Whether public network access is enabled. Keep true until exact private endpoint, subnet, and DNS requirements are supplied."
  type        = bool
  default     = true
}

variable "ai_services_network_acls" {
  description = "Optional network ACLs for the Azure AI Services account."
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

variable "ai_services_network_injections" {
  description = "Optional Foundry agent network injection configuration."
  type = object({
    subnet_id                         = string
    scenario                          = string
    microsoft_managed_network_enabled = optional(bool, false)
  })
  default = null
}

variable "ai_services_outbound_network_access_restricted" {
  description = "Whether outbound network access is restricted for the Azure AI Services account."
  type        = bool
  default     = null
}

variable "ai_services_private_endpoints" {
  description = "Optional private endpoints for the Azure AI Services account."
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

variable "ai_services_private_endpoints_manage_dns_zone_group" {
  description = "Whether the AVM module manages private DNS zone groups for supplied private endpoints."
  type        = bool
  default     = true
}

variable "ai_services_user_assigned_identity_resource_ids" {
  description = "User-assigned managed identity resource IDs to attach to the Azure AI Services account."
  type        = set(string)
  default     = []
}

variable "runtime_managed_identity_principal_ids" {
  description = "Runtime managed identity principal IDs that should receive Azure AI User access. Downstream runtime issues supply these values."
  type        = map(string)
  default     = {}
}

variable "ai_services_role_assignments" {
  description = "Additional Azure RBAC role assignments scoped to the Azure AI Services account."
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

variable "llm_assisted_recommendations_enabled" {
  description = "Whether LLM-assisted recommendations are enabled for this environment. Requires recommendation-writer-primary in model_deployments."
  type        = bool
  default     = false
}

variable "model_deployments" {
  description = "Azure OpenAI or Foundry model deployments keyed by approved product deployment alias."
  type = map(object({
    model_format                = string
    model_name                  = string
    model_version               = optional(string)
    sku_name                    = string
    capacity                    = optional(number, 1)
    family                      = optional(string)
    size                        = optional(string)
    tier                        = optional(string)
    rai_policy_name             = optional(string)
    version_upgrade_option      = optional(string, "NoAutoUpgrade")
    dynamic_throttling_enabled  = optional(bool, false)
    structured_outputs_required = optional(bool, true)
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

  validation {
    condition = alltrue([
      for alias in keys(var.model_deployments) :
      can(regex("^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$", alias))
    ])
    error_message = "model_deployments keys must be stable lowercase deployment aliases."
  }
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
