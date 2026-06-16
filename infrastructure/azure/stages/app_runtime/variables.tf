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

variable "app_runtime_resource_group_name" {
  description = "Optional explicit resource group name for app runtime resources. When null, a deterministic stage name is used."
  type        = string
  default     = null

  validation {
    condition     = var.app_runtime_resource_group_name == null || can(regex("^[A-Za-z0-9._() -]{1,90}$", var.app_runtime_resource_group_name))
    error_message = "app_runtime_resource_group_name must be a valid Azure resource group name when supplied."
  }
}

variable "container_app_environment_name" {
  description = "Optional explicit Azure Container Apps environment name. When null, a deterministic stage name is used."
  type        = string
  default     = null

  validation {
    condition     = var.container_app_environment_name == null || can(regex("^[a-z0-9][a-z0-9-]{0,30}[a-z0-9]$", var.container_app_environment_name))
    error_message = "container_app_environment_name must be lowercase alphanumeric or hyphen, start and end with alphanumeric, and be 2 to 32 characters."
  }
}

variable "container_app_environment_logs_destination" {
  description = "Container Apps environment log destination. Use azure-monitor by default; use log-analytics only when log_analytics_workspace_id is supplied."
  type        = string
  default     = "azure-monitor"

  validation {
    condition     = contains(["azure-monitor", "log-analytics"], var.container_app_environment_logs_destination)
    error_message = "container_app_environment_logs_destination must be azure-monitor or log-analytics."
  }
}

variable "container_app_environment_public_network_access" {
  description = "Public network access for the Container Apps environment. Later edge issues can set this to Disabled when Private Link origins are wired."
  type        = string
  default     = "Enabled"

  validation {
    condition     = contains(["Enabled", "Disabled"], var.container_app_environment_public_network_access)
    error_message = "container_app_environment_public_network_access must be Enabled or Disabled."
  }
}

variable "container_app_environment_infrastructure_subnet_id" {
  description = "Optional infrastructure subnet ID for workload-profile Container Apps environment networking and zone redundancy."
  type        = string
  default     = null
}

variable "log_analytics_workspace_id" {
  description = "Optional Log Analytics workspace ID used for Container Apps diagnostic settings and log-analytics environment logs."
  type        = string
  default     = null
}

variable "dashboard_image" {
  description = "Container image for the Product Dashboard Container App."
  type        = string
  default     = "ghcr.io/collaborationwithothers/ai-agent-token-observability/product-dashboard:latest"

  validation {
    condition     = length(trimspace(var.dashboard_image)) > 0
    error_message = "dashboard_image must not be empty."
  }
}

variable "product_api_image" {
  description = "Container image for the Product API Container App."
  type        = string
  default     = "ghcr.io/collaborationwithothers/ai-agent-token-observability/product-api:latest"

  validation {
    condition     = length(trimspace(var.product_api_image)) > 0
    error_message = "product_api_image must not be empty."
  }
}

variable "product_ingestion_image" {
  description = "Container image for the Product Ingestion Endpoint Container App."
  type        = string
  default     = "ghcr.io/collaborationwithothers/ai-agent-token-observability/product-ingestion:latest"

  validation {
    condition     = length(trimspace(var.product_ingestion_image)) > 0
    error_message = "product_ingestion_image must not be empty."
  }
}

variable "shared_jobs_image" {
  description = "Shared container image for finite Product Jobs commands."
  type        = string
  default     = "ghcr.io/collaborationwithothers/ai-agent-token-observability/product-jobs:latest"

  validation {
    condition     = length(trimspace(var.shared_jobs_image)) > 0
    error_message = "shared_jobs_image must not be empty."
  }
}

variable "dashboard_target_port" {
  description = "Listening port for the Product Dashboard container."
  type        = number
  default     = 8080

  validation {
    condition     = var.dashboard_target_port >= 1 && var.dashboard_target_port <= 65535
    error_message = "dashboard_target_port must be between 1 and 65535."
  }
}

variable "product_api_target_port" {
  description = "Listening port for the Product API container."
  type        = number
  default     = 8080

  validation {
    condition     = var.product_api_target_port >= 1 && var.product_api_target_port <= 65535
    error_message = "product_api_target_port must be between 1 and 65535."
  }
}

variable "product_ingestion_target_port" {
  description = "Listening port for the Product Ingestion Endpoint container."
  type        = number
  default     = 8080

  validation {
    condition     = var.product_ingestion_target_port >= 1 && var.product_ingestion_target_port <= 65535
    error_message = "product_ingestion_target_port must be between 1 and 65535."
  }
}

variable "container_registry_server" {
  description = "Optional private container registry server. When supplied, each Container App uses its managed identity for image pulls."
  type        = string
  default     = null
}

variable "container_app_additional_environment" {
  description = "Additional non-secret environment variables by Container App key."
  type        = map(map(string))
  default     = {}
}

variable "container_app_secret_names" {
  description = "Secret-backed environment variable names by Container App key. Values are Container App secret names, not secret values."
  type        = map(map(string))
  default     = {}
}

variable "container_app_key_vault_secret_ids" {
  description = "Key Vault secret IDs by Container App key and Container App secret name. Secret values are never supplied through Terraform variables."
  type        = map(map(string))
  default     = {}
}

variable "container_app_job_additional_environment" {
  description = "Additional non-secret environment variables by Container Apps Job key."
  type        = map(map(string))
  default     = {}
}

variable "container_app_job_secret_names" {
  description = "Secret-backed environment variable names by Container Apps Job key. Values are Container Apps Job secret names, not secret values."
  type        = map(map(string))
  default     = {}
}

variable "container_app_job_key_vault_secret_ids" {
  description = "Key Vault secret IDs by Container Apps Job key and secret name. Plain secret values are not accepted."
  type        = map(map(string))
  default     = {}
}
