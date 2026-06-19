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

variable "observability_resource_group_name" {
  description = "Optional explicit resource group name for observability resources. When null, a deterministic stage name is used."
  type        = string
  default     = null

  validation {
    condition     = var.observability_resource_group_name == null || can(regex("^[A-Za-z0-9._() -]{1,90}$", var.observability_resource_group_name))
    error_message = "observability_resource_group_name must be a valid Azure resource group name when supplied."
  }
}

variable "log_analytics_workspace_name" {
  description = "Optional explicit Log Analytics workspace name. When null, a deterministic stage name is used."
  type        = string
  default     = null

  validation {
    condition     = var.log_analytics_workspace_name == null || can(regex("^[A-Za-z0-9-]{4,63}$", var.log_analytics_workspace_name))
    error_message = "log_analytics_workspace_name must be 4 to 63 letters, numbers, or hyphens when supplied."
  }
}

variable "log_analytics_workspace_sku" {
  description = "Log Analytics workspace SKU."
  type        = string
  default     = "PerGB2018"

  validation {
    condition     = contains(["Free", "PerNode", "Premium", "Standard", "Standalone", "Unlimited", "CapacityReservation", "PerGB2018"], var.log_analytics_workspace_sku)
    error_message = "log_analytics_workspace_sku must be a valid Log Analytics workspace SKU."
  }
}

variable "log_analytics_retention_in_days" {
  description = "Retention period for Log Analytics workspace data."
  type        = number
  default     = 30

  validation {
    condition     = var.log_analytics_retention_in_days == 7 || (var.log_analytics_retention_in_days >= 30 && var.log_analytics_retention_in_days <= 730)
    error_message = "log_analytics_retention_in_days must be 7 for Free tier or between 30 and 730."
  }
}

variable "log_analytics_daily_quota_gb" {
  description = "Daily ingestion quota for Log Analytics. Use -1 for unlimited."
  type        = number
  default     = -1

  validation {
    condition     = var.log_analytics_daily_quota_gb == -1 || var.log_analytics_daily_quota_gb >= 0
    error_message = "log_analytics_daily_quota_gb must be -1 for unlimited or a non-negative number."
  }
}

variable "log_analytics_internet_ingestion_enabled" {
  description = "Whether Log Analytics ingestion over the public internet is enabled. Keep enabled until Azure Monitor Private Link Scope plumbing is implemented."
  type        = bool
  default     = true
}

variable "log_analytics_internet_query_enabled" {
  description = "Whether Log Analytics querying over the public internet is enabled. Keep enabled until Azure Monitor Private Link Scope plumbing is implemented."
  type        = bool
  default     = true
}

variable "log_analytics_local_authentication_enabled" {
  description = "Whether local/shared-key authentication is enabled for Log Analytics."
  type        = bool
  default     = false
}

variable "application_insights_name" {
  description = "Optional explicit Application Insights component name. When null, a deterministic stage name is used."
  type        = string
  default     = null

  validation {
    condition     = var.application_insights_name == null || can(regex("^[A-Za-z0-9_.-]{1,260}$", var.application_insights_name))
    error_message = "application_insights_name must be a valid Application Insights component name when supplied."
  }
}

variable "application_insights_application_type" {
  description = "Application Insights application type."
  type        = string
  default     = "web"

  validation {
    condition     = contains(["web", "ios", "java", "phone", "MobileCenter", "Node.JS", "other", "store"], var.application_insights_application_type)
    error_message = "application_insights_application_type must be a supported Application Insights application type."
  }
}

variable "application_insights_daily_data_cap_in_gb" {
  description = "Daily data cap for Application Insights in GB. Use 0 for unlimited."
  type        = number
  default     = 5

  validation {
    condition     = var.application_insights_daily_data_cap_in_gb >= 0
    error_message = "application_insights_daily_data_cap_in_gb must be non-negative."
  }
}

variable "application_insights_daily_data_cap_notifications_disabled" {
  description = "Whether Application Insights daily data cap notifications are disabled."
  type        = bool
  default     = false
}

variable "application_insights_disable_ip_masking" {
  description = "Whether Application Insights IP masking is disabled. Defaults to false so masking remains enabled."
  type        = bool
  default     = false
}

variable "application_insights_internet_ingestion_enabled" {
  description = "Whether Application Insights ingestion over the public internet is enabled. Keep enabled until Azure Monitor Private Link Scope plumbing is implemented."
  type        = bool
  default     = true
}

variable "application_insights_internet_query_enabled" {
  description = "Whether Application Insights querying over the public internet is enabled. Keep enabled until Azure Monitor Private Link Scope plumbing is implemented."
  type        = bool
  default     = true
}

variable "application_insights_local_authentication_disabled" {
  description = "Whether Application Insights local authentication is disabled."
  type        = bool
  default     = true
}

variable "application_insights_retention_in_days" {
  description = "Retention period for Application Insights data."
  type        = number
  default     = 90

  validation {
    condition     = var.application_insights_retention_in_days == 0 || (var.application_insights_retention_in_days >= 30 && var.application_insights_retention_in_days <= 730)
    error_message = "application_insights_retention_in_days must be 0 for unlimited or between 30 and 730."
  }
}

variable "application_insights_sampling_percentage" {
  description = "Application Insights sampling percentage."
  type        = number
  default     = 100

  validation {
    condition     = var.application_insights_sampling_percentage >= 0 && var.application_insights_sampling_percentage <= 100
    error_message = "application_insights_sampling_percentage must be between 0 and 100."
  }
}

variable "monitor_workspace_name" {
  description = "Optional explicit Azure Monitor workspace name. When null, a deterministic stage name is used."
  type        = string
  default     = null

  validation {
    condition     = var.monitor_workspace_name == null || can(regex("^[A-Za-z0-9_.-]{3,63}$", var.monitor_workspace_name))
    error_message = "monitor_workspace_name must be 3 to 63 valid Azure Monitor workspace name characters when supplied."
  }
}

variable "monitor_workspace_public_network_access_enabled" {
  description = "Whether public network access is enabled for the Azure Monitor workspace. Keep enabled until Azure Monitor Private Link Scope plumbing is implemented."
  type        = bool
  default     = true
}
