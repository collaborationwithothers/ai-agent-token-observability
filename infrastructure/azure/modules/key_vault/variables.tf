variable "location" {
  description = "Azure region for the Key Vault."
  type        = string
}

variable "name" {
  description = "Key Vault name."
  type        = string
}

variable "public_network_access_enabled" {
  description = "Whether public network access is enabled for the Key Vault."
  type        = bool
  default     = false
}

variable "purge_protection_enabled" {
  description = "Whether purge protection is enabled for the Key Vault."
  type        = bool
  default     = true
}

variable "resource_group_name" {
  description = "Resource group name for the Key Vault."
  type        = string
}

variable "role_assignments" {
  description = "Azure RBAC role assignments scoped to the Key Vault."
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
  description = "Key Vault SKU name."
  type        = string
  default     = "standard"
}

variable "tags" {
  description = "Tags assigned to the Key Vault."
  type        = map(string)
  default     = {}
}

variable "tenant_id" {
  description = "Tenant ID used by the Key Vault."
  type        = string
}
