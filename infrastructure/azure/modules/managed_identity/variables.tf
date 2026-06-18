variable "federated_identity_credentials" {
  description = "Federated identity credentials to create on the user-assigned managed identity."
  type = map(object({
    audience = list(string)
    issuer   = string
    name     = string
    subject  = string
  }))
  default = {}
}

variable "location" {
  description = "Azure region for the user-assigned managed identity."
  type        = string
}

variable "name" {
  description = "User-assigned managed identity name."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group name for the user-assigned managed identity."
  type        = string
}

variable "role_assignments" {
  description = "Role assignments granted to this user-assigned managed identity."
  type = map(object({
    role_definition_id_or_name             = string
    scope                                  = string
    condition                              = optional(string, null)
    condition_version                      = optional(string, null)
    delegated_managed_identity_resource_id = optional(string, null)
    description                            = optional(string, null)
    skip_service_principal_aad_check       = optional(bool, false)
  }))
  default = {}
}

variable "tags" {
  description = "Tags assigned to the user-assigned managed identity."
  type        = map(string)
  default     = {}
}
