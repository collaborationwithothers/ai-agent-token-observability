variable "daily_quota_gb" {
  description = "Daily ingestion quota for the Log Analytics workspace. Use -1 for unlimited."
  type        = number
  default     = -1

  validation {
    condition     = var.daily_quota_gb == -1 || var.daily_quota_gb >= 0
    error_message = "daily_quota_gb must be -1 for unlimited or a non-negative number."
  }
}

variable "internet_ingestion_enabled" {
  description = "Whether Log Analytics ingestion over the public internet is enabled."
  type        = bool
  default     = false
}

variable "internet_query_enabled" {
  description = "Whether Log Analytics querying over the public internet is enabled."
  type        = bool
  default     = false
}

variable "local_authentication_enabled" {
  description = "Whether local/shared-key authentication is enabled for the Log Analytics workspace."
  type        = bool
  default     = false
}

variable "location" {
  description = "Azure region for the Log Analytics workspace."
  type        = string
}

variable "name" {
  description = "Log Analytics workspace name."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group name for the Log Analytics workspace."
  type        = string
}

variable "retention_in_days" {
  description = "Retention period for Log Analytics workspace data."
  type        = number
  default     = 30

  validation {
    condition     = var.retention_in_days == 7 || (var.retention_in_days >= 30 && var.retention_in_days <= 730)
    error_message = "retention_in_days must be 7 for Free tier or between 30 and 730."
  }
}

variable "sku" {
  description = "Log Analytics workspace SKU."
  type        = string
  default     = "PerGB2018"

  validation {
    condition     = contains(["Free", "PerNode", "Premium", "Standard", "Standalone", "Unlimited", "CapacityReservation", "PerGB2018"], var.sku)
    error_message = "sku must be a valid Log Analytics workspace SKU."
  }
}

variable "tags" {
  description = "Tags assigned to the Log Analytics workspace."
  type        = map(string)
  default     = {}
}
