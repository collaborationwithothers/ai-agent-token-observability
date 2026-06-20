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

variable "grafana_major_version" {
  description = "Azure Managed Grafana major version."
  type        = number
  default     = 12

  validation {
    condition     = contains([11, 12], var.grafana_major_version)
    error_message = "grafana_major_version must be 11 or 12."
  }
}

variable "grafana_public_network_access_enabled" {
  description = "Whether the native Azure Managed Grafana endpoint is reachable over the public interface."
  type        = bool
  default     = true
}

variable "grafana_sku_size" {
  description = "Azure Managed Grafana SKU size."
  type        = string
  default     = "X1"

  validation {
    condition     = contains(["X1", "X2"], var.grafana_sku_size)
    error_message = "grafana_sku_size must be X1 or X2."
  }
}

variable "grafana_workspace_name" {
  description = "Optional Azure Managed Grafana workspace name override. Defaults to a deterministic environment, region, and customer scoped name."
  type        = string
  default     = null

  validation {
    condition     = var.grafana_workspace_name == null || (length(var.grafana_workspace_name) >= 2 && length(var.grafana_workspace_name) <= 23 && can(regex("^[a-zA-Z0-9][a-zA-Z0-9-]*[a-zA-Z0-9]$", var.grafana_workspace_name)))
    error_message = "grafana_workspace_name must be 2 to 23 characters, alphanumeric or hyphen, and start and end with an alphanumeric character."
  }
}

variable "metrics_data_source_identifiers" {
  description = "Non-secret aggregate metrics data source identifiers from observability_foundation."
  type = map(object({
    type                                = string
    resource_id                         = string
    name                                = string
    query_endpoint                      = string
    default_data_collection_endpoint_id = optional(string)
    default_data_collection_rule_id     = optional(string)
    consumer_stages                     = list(string)
    boundary                            = string
  }))

  validation {
    condition = (
      can(var.metrics_data_source_identifiers["aggregate_metrics"]) &&
      var.metrics_data_source_identifiers["aggregate_metrics"].type == "azure_monitor_workspace" &&
      var.metrics_data_source_identifiers["aggregate_metrics"].boundary == "aggregate_metrics_only" &&
      contains(var.metrics_data_source_identifiers["aggregate_metrics"].consumer_stages, "managed_grafana") &&
      can(regex("^/subscriptions/[^/]+/resourceGroups/[^/]+/providers/Microsoft\\.Monitor/accounts/[^/]+$", var.metrics_data_source_identifiers["aggregate_metrics"].resource_id))
    )
    error_message = "metrics_data_source_identifiers must include aggregate_metrics for an aggregate-only Azure Monitor workspace that allows managed_grafana."
  }
}

variable "observability_resource_group_id" {
  description = "Resource ID of the observability resource group exported by observability_foundation."
  type        = string

  validation {
    condition     = can(regex("^/subscriptions/[^/]+/resourceGroups/[^/]+$", var.observability_resource_group_id))
    error_message = "observability_resource_group_id must be an Azure resource group resource ID."
  }
}

variable "observability_resource_group_name" {
  description = "Name of the observability resource group exported by observability_foundation."
  type        = string

  validation {
    condition     = can(regex("^rg-[a-z0-9-]+$", var.observability_resource_group_name))
    error_message = "observability_resource_group_name must be a lowercase Azure resource group name starting with rg-."
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
