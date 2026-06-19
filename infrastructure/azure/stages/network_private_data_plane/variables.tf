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

variable "network_resource_group_name" {
  description = "Optional explicit resource group name for private data plane network resources. When null, a deterministic stage name is used."
  type        = string
  default     = null

  validation {
    condition     = var.network_resource_group_name == null || can(regex("^[A-Za-z0-9._() -]{1,90}$", var.network_resource_group_name))
    error_message = "network_resource_group_name must be a valid Azure resource group name when supplied."
  }
}

variable "virtual_network_name" {
  description = "Optional explicit virtual network name. When null, a deterministic stage name is used."
  type        = string
  default     = null

  validation {
    condition     = var.virtual_network_name == null || can(regex("^[A-Za-z0-9_.-]{2,64}$", var.virtual_network_name))
    error_message = "virtual_network_name must be 2 to 64 valid Azure virtual network name characters when supplied."
  }
}

variable "virtual_network_address_space" {
  description = "Address space assigned to the private data plane virtual network."
  type        = list(string)
  default     = ["10.40.0.0/20"]

  validation {
    condition     = length(var.virtual_network_address_space) > 0 && alltrue([for prefix in var.virtual_network_address_space : can(cidrhost(prefix, 0))])
    error_message = "virtual_network_address_space must contain at least one valid CIDR prefix."
  }
}

variable "subnet_address_prefixes" {
  description = "Stable address prefixes for downstream network subnets."
  type = object({
    container_apps_infrastructure = string
    reserved                      = string
    shared_networking             = string
  })
  default = {
    container_apps_infrastructure = "10.40.0.0/23"
    reserved                      = "10.40.8.0/21"
    shared_networking             = "10.40.4.0/24"
  }

  validation {
    condition     = alltrue([for prefix in values(var.subnet_address_prefixes) : can(cidrhost(prefix, 0))])
    error_message = "subnet_address_prefixes values must be valid CIDR prefixes."
  }
}
