variable "application_type" {
  description = "Application Insights application type."
  type        = string
  default     = "web"

  validation {
    condition     = contains(["web", "ios", "java", "phone", "MobileCenter", "Node.JS", "other", "store"], var.application_type)
    error_message = "application_type must be a supported Application Insights application type."
  }
}

variable "daily_data_cap_in_gb" {
  description = "Daily data cap for Application Insights in GB. Use 0 for unlimited."
  type        = number
  default     = 5

  validation {
    condition     = var.daily_data_cap_in_gb >= 0
    error_message = "daily_data_cap_in_gb must be non-negative."
  }
}

variable "daily_data_cap_notifications_disabled" {
  description = "Whether daily data cap notifications are disabled."
  type        = bool
  default     = false
}

variable "disable_ip_masking" {
  description = "Whether Application Insights IP masking is disabled. Defaults to false so masking remains enabled."
  type        = bool
  default     = false
}

variable "internet_ingestion_enabled" {
  description = "Whether Application Insights ingestion over the public internet is enabled."
  type        = bool
  default     = false
}

variable "internet_query_enabled" {
  description = "Whether Application Insights querying over the public internet is enabled."
  type        = bool
  default     = false
}

variable "local_authentication_disabled" {
  description = "Whether local authentication is disabled for Application Insights."
  type        = bool
  default     = true
}

variable "location" {
  description = "Azure region for Application Insights."
  type        = string
}

variable "name" {
  description = "Application Insights component name."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group name for Application Insights."
  type        = string
}

variable "retention_in_days" {
  description = "Retention period for Application Insights data."
  type        = number
  default     = 90

  validation {
    condition     = var.retention_in_days == 0 || (var.retention_in_days >= 30 && var.retention_in_days <= 730)
    error_message = "retention_in_days must be 0 for unlimited or between 30 and 730."
  }
}

variable "sampling_percentage" {
  description = "Application Insights sampling percentage."
  type        = number
  default     = 100

  validation {
    condition     = var.sampling_percentage >= 0 && var.sampling_percentage <= 100
    error_message = "sampling_percentage must be between 0 and 100."
  }
}

variable "tags" {
  description = "Tags assigned to Application Insights."
  type        = map(string)
  default     = {}
}

variable "workspace_id" {
  description = "Log Analytics workspace resource ID backing this workspace-based Application Insights component."
  type        = string

  validation {
    condition     = can(regex("^/subscriptions/[^/]+/resourceGroups/[^/]+/providers/Microsoft\\.OperationalInsights/workspaces/[^/]+$", var.workspace_id))
    error_message = "workspace_id must be a Log Analytics workspace resource ID."
  }
}
