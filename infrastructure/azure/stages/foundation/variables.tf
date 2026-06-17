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

variable "enable_private_endpoints" {
  description = "Whether this stage should prefer private endpoints when resources are added."
  type        = bool
  default     = true
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

variable "foundation_resource_group_name" {
  description = "Optional override for the retained foundation resource group name."
  type        = string
  default     = null

  validation {
    condition     = var.foundation_resource_group_name == null || can(regex("^[A-Za-z0-9._() -]{1,90}$", var.foundation_resource_group_name))
    error_message = "foundation_resource_group_name must be a valid Azure resource group name when supplied."
  }
}

variable "container_registry_name" {
  description = "Optional override for the shared Azure Container Registry name. Must be globally unique."
  type        = string
  default     = null

  validation {
    condition     = var.container_registry_name == null || can(regex("^[A-Za-z0-9]{5,50}$", var.container_registry_name))
    error_message = "container_registry_name must be 5 to 50 alphanumeric characters when supplied."
  }
}

variable "container_registry_sku" {
  description = "SKU for the shared Azure Container Registry."
  type        = string
  default     = "Standard"

  validation {
    condition     = contains(["Basic", "Standard", "Premium"], var.container_registry_sku)
    error_message = "container_registry_sku must be Basic, Standard, or Premium."
  }
}

variable "key_vault_name" {
  description = "Optional override for the shared foundation Key Vault name. Must be globally unique."
  type        = string
  default     = null

  validation {
    condition     = var.key_vault_name == null || (can(regex("^[A-Za-z][A-Za-z0-9-]{1,22}[A-Za-z0-9]$", var.key_vault_name)) && !can(regex("--", var.key_vault_name)))
    error_message = "key_vault_name must be 3 to 24 alphanumeric or hyphen characters, start with a letter, end with an alphanumeric character, and not contain consecutive hyphens."
  }
}

variable "key_vault_sku_name" {
  description = "SKU for the shared foundation Key Vault."
  type        = string
  default     = "standard"

  validation {
    condition     = contains(["standard", "premium"], var.key_vault_sku_name)
    error_message = "key_vault_sku_name must be standard or premium."
  }
}

variable "key_vault_public_network_access_enabled" {
  description = "Whether public network access is enabled for the shared foundation Key Vault."
  type        = bool
  default     = false
}

variable "key_vault_purge_protection_enabled" {
  description = "Whether purge protection is enabled for the shared foundation Key Vault."
  type        = bool
  default     = true
}

variable "create_deployment_identity" {
  description = "Whether to create the foundation deployment user-assigned managed identity."
  type        = bool
  default     = true
}

variable "deployment_identity_name" {
  description = "Optional override for the foundation deployment user-assigned managed identity name."
  type        = string
  default     = null

  validation {
    condition     = var.deployment_identity_name == null || can(regex("^[A-Za-z0-9_-]{3,128}$", var.deployment_identity_name))
    error_message = "deployment_identity_name must be 3 to 128 characters and contain only letters, numbers, underscores, or hyphens."
  }
}

variable "deployment_identity_references" {
  description = "Existing deployment identity references to grant least-privilege Key Vault access when create_deployment_identity is false or additional identities are required."
  type = map(object({
    principal_id = string
    client_id    = optional(string, null)
    id           = optional(string, null)
    name         = optional(string, null)
    tenant_id    = optional(string, null)
  }))
  default = {}

  validation {
    condition = alltrue([
      for _, identity in var.deployment_identity_references :
      can(regex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", identity.principal_id)) &&
      (identity.client_id == null || can(regex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", identity.client_id))) &&
      (identity.tenant_id == null || can(regex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", identity.tenant_id))) &&
      (identity.name == null || can(regex("^[A-Za-z0-9_.() -]{1,128}$", identity.name)))
    ])
    error_message = "deployment_identity_references must use GUID principal_id, client_id, and tenant_id values when supplied, and valid identity names when supplied."
  }
}

variable "assign_deployment_identity_key_vault_role" {
  description = "Whether to assign the configured least-privilege Key Vault role to created and referenced deployment identities."
  type        = bool
  default     = true
}

variable "deployment_identity_key_vault_role_definition_name" {
  description = "Least-privilege Key Vault data-plane role assigned to deployment identities."
  type        = string
  default     = "Key Vault Secrets Officer"

  validation {
    condition = contains([
      "Key Vault Reader",
      "Key Vault Secrets User",
      "Key Vault Secrets Officer",
      "Key Vault Crypto User",
      "Key Vault Certificates Officer"
    ], var.deployment_identity_key_vault_role_definition_name)
    error_message = "deployment_identity_key_vault_role_definition_name must be a least-privilege Key Vault data-plane role."
  }
}

variable "key_vault_role_assignments" {
  description = "Additional explicit Azure RBAC role assignments scoped to the foundation Key Vault."
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

  validation {
    condition = alltrue([
      for _, assignment in var.key_vault_role_assignments :
      can(regex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", assignment.principal_id)) &&
      length(trimspace(assignment.role_definition_id_or_name)) > 0 &&
      !contains(["Owner", "Contributor", "User Access Administrator"], assignment.role_definition_id_or_name)
    ])
    error_message = "key_vault_role_assignments must use GUID principal_id values, non-empty role names or IDs, and must not grant broad Owner, Contributor, or User Access Administrator roles."
  }
}
