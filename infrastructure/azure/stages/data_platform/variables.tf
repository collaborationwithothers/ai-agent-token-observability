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

variable "data_resource_group_name" {
  description = "Optional explicit resource group name for data platform resources. When null, a deterministic stage name is used."
  type        = string
  default     = null

  validation {
    condition     = var.data_resource_group_name == null || can(regex("^[A-Za-z0-9._() -]{1,90}$", var.data_resource_group_name))
    error_message = "data_resource_group_name must be a valid Azure resource group name when supplied."
  }
}

variable "diagnostic_destinations" {
  description = "Diagnostic destination contracts from observability_foundation outputs."
  type = object({
    data_platform = optional(object({
      log_analytics_workspace_resource_id = string
      destination_type                    = optional(string, "Dedicated")
    }))
  })
  default = {}
}

variable "postgresql_server_name" {
  description = "Optional explicit PostgreSQL Flexible Server name. When null, a deterministic stage name is used."
  type        = string
  default     = null

  validation {
    condition     = var.postgresql_server_name == null || can(regex("^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$", var.postgresql_server_name))
    error_message = "postgresql_server_name must be 3 to 63 lowercase letters, numbers, or hyphens when supplied."
  }
}

variable "postgresql_database_name" {
  description = "Product Metadata Store database name."
  type        = string
  default     = "token_observability"

  validation {
    condition     = can(regex("^[a-zA-Z_][a-zA-Z0-9_]{0,62}$", var.postgresql_database_name))
    error_message = "postgresql_database_name must be a valid bounded PostgreSQL identifier."
  }
}

variable "postgresql_server_version" {
  description = "PostgreSQL Flexible Server major version."
  type        = string
  default     = "16"

  validation {
    condition     = contains(["11", "12", "13", "14", "15", "16"], var.postgresql_server_version)
    error_message = "postgresql_server_version must be a supported PostgreSQL Flexible Server major version."
  }
}

variable "postgresql_sku_name" {
  description = "PostgreSQL Flexible Server SKU name."
  type        = string
  default     = "GP_Standard_D2s_v3"
}

variable "postgresql_storage_mb" {
  description = "PostgreSQL Flexible Server storage size in MB."
  type        = number
  default     = 32768
}

variable "postgresql_storage_tier" {
  description = "Optional PostgreSQL Flexible Server storage tier."
  type        = string
  default     = null
}

variable "postgresql_backup_retention_days" {
  description = "PostgreSQL Flexible Server automatic backup retention days."
  type        = number
  default     = 35

  validation {
    condition     = var.postgresql_backup_retention_days >= 7 && var.postgresql_backup_retention_days <= 35
    error_message = "postgresql_backup_retention_days must be between 7 and 35."
  }
}

variable "postgresql_geo_redundant_backup_enabled" {
  description = "Whether geo-redundant PostgreSQL backups are enabled. Keep false unless the selected region and SKU support the operational cost."
  type        = bool
  default     = false
}

variable "postgresql_auto_grow_enabled" {
  description = "Whether PostgreSQL storage auto-grow is enabled."
  type        = bool
  default     = true
}

variable "postgresql_ad_administrators" {
  description = "Additional Microsoft Entra PostgreSQL administrators. The sql-admins group is always included by this stage; external user-assigned identities can be supplied here when their principal metadata is known."
  type = map(object({
    tenant_id      = string
    object_id      = string
    principal_name = string
    principal_type = string
  }))
  default = {}
}

variable "postgresql_firewall_rules" {
  description = "PostgreSQL public firewall rules. The default allows only the managed VNet runner NAT gateway public IP."
  type = map(object({
    name             = string
    start_ip_address = string
    end_ip_address   = string
  }))
  default = {
    github_actions_runner_nat = {
      name             = "allow-github-actions-runner-nat"
      start_ip_address = "74.234.84.247"
      end_ip_address   = "74.234.84.247"
    }
  }
}

variable "postgresql_role_assignments" {
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

variable "postgresql_user_assigned_identity_resource_ids" {
  description = "User-assigned managed identity resource IDs to attach to PostgreSQL Flexible Server."
  type        = set(string)
  default     = []
}

variable "storage_account_name" {
  description = "Optional explicit product storage account name. When null, a deterministic stage name is used."
  type        = string
  default     = null

  validation {
    condition     = var.storage_account_name == null || can(regex("^[a-z0-9]{3,24}$", var.storage_account_name))
    error_message = "storage_account_name must be 3 to 24 lowercase letters or numbers when supplied."
  }
}

variable "storage_account_sku_name" {
  description = "Product storage account SKU name."
  type        = string
  default     = "Standard_ZRS"

  validation {
    condition     = can(regex("^(Standard|Premium)(V2)?_(LRS|GRS|RAGRS|ZRS|GZRS|RAGZRS)$", var.storage_account_sku_name))
    error_message = "storage_account_sku_name must be a valid storage account SKU name."
  }
}

variable "captured_content_container_name" {
  description = "Blob container name for policy-approved redacted captured content."
  type        = string
  default     = "captured-content"

  validation {
    condition     = can(regex("^[a-z0-9](?:[a-z0-9-]{1,61}[a-z0-9])?$", var.captured_content_container_name))
    error_message = "captured_content_container_name must be a valid blob container name."
  }
}

variable "content_review_artifacts_container_name" {
  description = "Blob container name for policy-approved review artifacts."
  type        = string
  default     = "content-review-artifacts"

  validation {
    condition     = can(regex("^[a-z0-9](?:[a-z0-9-]{1,61}[a-z0-9])?$", var.content_review_artifacts_container_name))
    error_message = "content_review_artifacts_container_name must be a valid blob container name."
  }
}

variable "operational_artifacts_container_name" {
  description = "Blob container name for restore drill and lifecycle validation artifacts."
  type        = string
  default     = "operational-artifacts"

  validation {
    condition     = can(regex("^[a-z0-9](?:[a-z0-9-]{1,61}[a-z0-9])?$", var.operational_artifacts_container_name))
    error_message = "operational_artifacts_container_name must be a valid blob container name."
  }
}

variable "captured_content_retention_days" {
  description = "Default lifecycle deletion age for redacted captured content and approved excerpts."
  type        = number
  default     = 30

  validation {
    condition     = var.captured_content_retention_days >= 1 && var.captured_content_retention_days <= 30
    error_message = "captured_content_retention_days must be between 1 and 30 for first-release policy."
  }
}

variable "operational_artifacts_retention_days" {
  description = "Default lifecycle deletion age for operational validation artifacts."
  type        = number
  default     = 180

  validation {
    condition     = var.operational_artifacts_retention_days >= 30 && var.operational_artifacts_retention_days <= 365
    error_message = "operational_artifacts_retention_days must be between 30 and 365."
  }
}

variable "blob_delete_retention_days" {
  description = "Storage blob soft-delete retention days."
  type        = number
  default     = 7

  validation {
    condition     = var.blob_delete_retention_days >= 1 && var.blob_delete_retention_days <= 365
    error_message = "blob_delete_retention_days must be between 1 and 365."
  }
}

variable "blob_container_delete_retention_days" {
  description = "Storage container soft-delete retention days."
  type        = number
  default     = 7

  validation {
    condition     = var.blob_container_delete_retention_days >= 1 && var.blob_container_delete_retention_days <= 365
    error_message = "blob_container_delete_retention_days must be between 1 and 365."
  }
}

variable "blob_point_in_time_restore_days" {
  description = "Blob point-in-time restore retention days. Must be lower than blob_delete_retention_days."
  type        = number
  default     = 6

  validation {
    condition     = var.blob_point_in_time_restore_days >= 1 && var.blob_point_in_time_restore_days < var.blob_delete_retention_days
    error_message = "blob_point_in_time_restore_days must be at least 1 and less than blob_delete_retention_days."
  }
}

variable "runtime_managed_identity_principal_ids" {
  description = "Runtime managed identity principal IDs that should receive Blob data access. Downstream runtime issues supply these values."
  type        = map(string)
  default     = {}
}

variable "storage_account_role_assignments" {
  description = "Azure RBAC role assignments scoped to the Storage Account."
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

variable "storage_user_assigned_identity_resource_ids" {
  description = "User-assigned managed identity resource IDs to attach to the Storage Account."
  type        = set(string)
  default     = []
}

variable "storage_network_rules" {
  description = "Network rules for the product Storage Account. Default denies public network access except Azure services, the managed VNet runner NAT gateway public IP, and the managed runner subnet."
  type = object({
    bypass                     = optional(set(string), ["AzureServices"])
    default_action             = optional(string, "Deny")
    ip_rules                   = optional(set(string), [])
    virtual_network_subnet_ids = optional(set(string), [])
    private_link_access = optional(list(object({
      endpoint_resource_id = string
      endpoint_tenant_id   = optional(string)
    })))
    timeouts = optional(object({
      create = optional(string)
      delete = optional(string)
      read   = optional(string)
      update = optional(string)
    }))
  })
  default = {
    bypass                     = ["AzureServices"]
    default_action             = "Deny"
    ip_rules                   = ["74.234.84.247"]
    virtual_network_subnet_ids = ["/subscriptions/c7a1d85d-159f-4cfc-bd13-51295c9acb96/resourceGroups/rg-dv-gh-actions-neu/providers/Microsoft.Network/virtualNetworks/vnet-dv-gh-actions-neu/subnets/snet-github-actions-private-runner-neu"]
  }
}
