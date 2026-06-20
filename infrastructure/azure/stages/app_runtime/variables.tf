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
  description = "Public network access for the Container Apps environment. Keep Enabled until deferred origin isolation hardening is implemented."
  type        = string
  default     = "Enabled"

  validation {
    condition     = contains(["Enabled", "Disabled"], var.container_app_environment_public_network_access)
    error_message = "container_app_environment_public_network_access must be Enabled or Disabled."
  }
}

variable "container_app_environment_infrastructure_subnet_id" {
  description = "Optional explicit infrastructure subnet ID for workload-profile Container Apps environment networking and zone redundancy. When null, network_subnet_ids.container_apps_infrastructure is used."
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

  validation {
    condition     = can(regex("^[A-Za-z0-9]+[.]azurecr[.]io/product-dashboard@sha256:[0-9a-f]{64}$", var.dashboard_image))
    error_message = "dashboard_image must be a digest-pinned ACR image for product-dashboard, for example registry.azurecr.io/product-dashboard@sha256:<64 lowercase hex characters>."
  }
}

variable "product_api_image" {
  description = "Container image for the Product API Container App."
  type        = string

  validation {
    condition     = can(regex("^[A-Za-z0-9]+[.]azurecr[.]io/product-api@sha256:[0-9a-f]{64}$", var.product_api_image))
    error_message = "product_api_image must be a digest-pinned ACR image for product-api, for example registry.azurecr.io/product-api@sha256:<64 lowercase hex characters>."
  }
}

variable "product_ingestion_image" {
  description = "Container image for the Product Ingestion Endpoint Container App."
  type        = string

  validation {
    condition     = can(regex("^[A-Za-z0-9]+[.]azurecr[.]io/product-ingestion@sha256:[0-9a-f]{64}$", var.product_ingestion_image))
    error_message = "product_ingestion_image must be a digest-pinned ACR image for product-ingestion, for example registry.azurecr.io/product-ingestion@sha256:<64 lowercase hex characters>."
  }
}

variable "shared_jobs_image" {
  description = "Shared container image for finite Product Jobs commands."
  type        = string

  validation {
    condition     = can(regex("^[A-Za-z0-9]+[.]azurecr[.]io/product-jobs@sha256:[0-9a-f]{64}$", var.shared_jobs_image))
    error_message = "shared_jobs_image must be a digest-pinned ACR image for product-jobs, for example registry.azurecr.io/product-jobs@sha256:<64 lowercase hex characters>."
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
  description = "Optional private container registry server. When supplied, each Container App uses its managed identity for image pulls and container_registry_id must also be supplied."
  type        = string
  default     = null
}

variable "container_registry_id" {
  description = "Resource ID of the private Azure Container Registry. Required when container_registry_server is supplied so runtime managed identities receive AcrPull."
  type        = string
  default     = null

  validation {
    condition     = var.container_registry_id == null || can(regex("^/subscriptions/[^/]+/resourceGroups/[^/]+/providers/Microsoft[.]ContainerRegistry/registries/[^/]+$", var.container_registry_id))
    error_message = "container_registry_id must be an Azure Container Registry resource ID when supplied."
  }
}

variable "ai_services_configuration_contract" {
  description = "Non-secret AI service configuration contract from ai_services."
  type = object({
    account_resource_id                     = string
    account_name                            = string
    endpoint                                = string
    managed_identity_principal_id           = string
    recommendation_model_deployment_aliases = list(string)
    public_network_access_enabled           = bool
    diagnostics_workspace_resource_id       = string
  })
}

variable "content_safety_contract" {
  description = "Non-secret Azure AI Content Safety configuration contract from ai_services."
  type = object({
    account_resource_id           = string
    endpoint                      = string
    prompt_shields_required       = bool
    protected_material_checks     = bool
    groundedness_checks_available = bool
    redaction_engine              = bool
    local_auth_enabled            = bool
    authentication                = string
  })
}

variable "data_platform_configuration_contract" {
  description = "Non-secret data platform configuration contract for app runtime and jobs."
  type = object({
    postgresql_server_fqdn    = string
    postgresql_database_names = map(string)
    storage_account_name      = string
    storage_container_names   = map(string)
    captured_content_storage_contract = object({
      storage_account_resource_id       = string
      captured_content_container_name   = string
      content_review_container_name     = string
      captured_content_prefix_template  = string
      content_review_prefix_template    = string
      retention_days                    = number
      redaction_required_before_storage = bool
      public_access                     = string
    })
    operational_storage_contract = object({
      storage_account_resource_id = string
      operational_container_name  = string
      restore_drill_prefix        = string
      lifecycle_validation_prefix = string
      retention_days              = number
      public_access               = string
    })
    storage_lifecycle_contract = object({
      captured_content_retention_days      = number
      operational_artifacts_retention_days = number
      blob_delete_retention_days           = number
      container_delete_retention_days      = number
      point_in_time_restore_days           = number
    })
  })
}

variable "diagnostic_destinations" {
  description = "Non-secret diagnostic destination contracts from observability_foundation."
  type = map(object({
    log_analytics_workspace_resource_id = optional(string)
    application_insights_resource_id    = optional(string)
    destination_type                    = string
    expected_log_groups                 = optional(list(string))
    expected_log_categories             = optional(list(string))
    expected_metric_categories          = list(string)
    consumer_stage                      = string
  }))

  validation {
    condition = (
      can(var.diagnostic_destinations["container_apps"]) &&
      can(var.diagnostic_destinations["container_app_jobs"]) &&
      var.diagnostic_destinations["container_apps"].consumer_stage == "app_runtime" &&
      var.diagnostic_destinations["container_app_jobs"].consumer_stage == "app_runtime"
    )
    error_message = "diagnostic_destinations must include container_apps and container_app_jobs contracts for app_runtime."
  }
}

variable "language_pii_detection_contract" {
  description = "Non-secret Azure AI Language PII detection configuration contract from ai_services."
  type = object({
    account_resource_id        = string
    endpoint                   = string
    required_before_storage    = bool
    stable_categories_required = bool
    preview_categories_enabled = bool
    local_auth_enabled         = bool
    authentication             = string
  })
}

variable "network_subnet_ids" {
  description = "Subnet resource IDs from network_private_data_plane by stable downstream contract key."
  type        = map(string)

  validation {
    condition = (
      can(var.network_subnet_ids["container_apps_infrastructure"]) &&
      can(regex("^/subscriptions/[^/]+/resourceGroups/[^/]+/providers/Microsoft\\.Network/virtualNetworks/[^/]+/subnets/[^/]+$", var.network_subnet_ids["container_apps_infrastructure"]))
    )
    error_message = "network_subnet_ids must include container_apps_infrastructure as an Azure subnet resource ID."
  }
}

variable "recommendation_model_deployment_contracts" {
  description = "Non-secret recommendation model deployment contracts from ai_services."
  type = map(object({
    deployment_alias            = string
    account_resource_id         = string
    account_endpoint            = string
    deployment_resource_id      = optional(string)
    provider                    = string
    region                      = string
    structured_outputs_required = bool
    content_filtering_enabled   = bool
  }))
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
