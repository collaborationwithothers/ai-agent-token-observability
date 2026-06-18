variable "ad_administrators" {
  description = "Microsoft Entra administrators for PostgreSQL Flexible Server."
  type = map(object({
    tenant_id      = string
    object_id      = string
    principal_name = string
    principal_type = string
  }))
  default = {}
}

variable "auto_grow_enabled" {
  description = "Whether PostgreSQL storage auto-grow is enabled."
  type        = bool
  default     = true
}

variable "backup_retention_days" {
  description = "PostgreSQL Flexible Server backup retention days."
  type        = number
  default     = 35

  validation {
    condition     = var.backup_retention_days >= 7 && var.backup_retention_days <= 35
    error_message = "backup_retention_days must be between 7 and 35."
  }
}

variable "databases" {
  description = "PostgreSQL databases to create."
  type = map(object({
    name      = string
    charset   = optional(string)
    collation = optional(string)
    timeouts = optional(object({
      create = optional(string)
      delete = optional(string)
      read   = optional(string)
    }))
  }))
  default = {}
}

variable "delegated_subnet_id" {
  description = "Delegated subnet resource ID for PostgreSQL private access."
  type        = string
}

variable "diagnostic_settings" {
  description = "Diagnostic settings for PostgreSQL Flexible Server."
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

variable "geo_redundant_backup_enabled" {
  description = "Whether geo-redundant PostgreSQL backup is enabled."
  type        = bool
  default     = false
}

variable "location" {
  description = "Azure region for PostgreSQL Flexible Server."
  type        = string
}

variable "name" {
  description = "PostgreSQL Flexible Server name."
  type        = string
}

variable "private_dns_zone_id" {
  description = "Private DNS zone resource ID for PostgreSQL private access."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group name for PostgreSQL Flexible Server."
  type        = string
}

variable "role_assignments" {
  description = "Azure RBAC role assignments scoped to PostgreSQL Flexible Server."
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

variable "server_version" {
  description = "PostgreSQL server major version."
  type        = string
  default     = "16"

  validation {
    condition     = contains(["11", "12", "13", "14", "15", "16"], var.server_version)
    error_message = "server_version must be a supported PostgreSQL Flexible Server major version."
  }
}

variable "sku_name" {
  description = "PostgreSQL Flexible Server SKU name."
  type        = string
  default     = "GP_Standard_D2s_v3"
}

variable "storage_mb" {
  description = "PostgreSQL Flexible Server storage size in MB."
  type        = number
  default     = 32768
}

variable "storage_tier" {
  description = "Optional PostgreSQL storage tier."
  type        = string
  default     = null
}

variable "tags" {
  description = "Tags assigned to PostgreSQL Flexible Server."
  type        = map(string)
  default     = {}
}

variable "tenant_id" {
  description = "Microsoft Entra tenant ID used for PostgreSQL authentication."
  type        = string
}

variable "user_assigned_identity_resource_ids" {
  description = "User-assigned managed identity resource IDs to attach to PostgreSQL Flexible Server."
  type        = set(string)
  default     = []
}

variable "zone_redundant_high_availability_enabled" {
  description = "Whether to enable zone-redundant PostgreSQL high availability."
  type        = bool
  default     = false
}
