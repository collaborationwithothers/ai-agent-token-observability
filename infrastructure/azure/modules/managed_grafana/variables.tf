variable "aggregate_metrics_data_source" {
  description = "Aggregate-only Azure Monitor workspace data source contract from observability_foundation."
  type = object({
    type                                = string
    resource_id                         = string
    name                                = string
    query_endpoint                      = string
    default_data_collection_endpoint_id = optional(string)
    default_data_collection_rule_id     = optional(string)
    consumer_stages                     = list(string)
    boundary                            = string
  })

  validation {
    condition = (
      var.aggregate_metrics_data_source.type == "azure_monitor_workspace" &&
      var.aggregate_metrics_data_source.boundary == "aggregate_metrics_only" &&
      contains(var.aggregate_metrics_data_source.consumer_stages, "managed_grafana") &&
      can(regex("^/subscriptions/[^/]+/resourceGroups/[^/]+/providers/Microsoft\\.Monitor/accounts/[^/]+$", var.aggregate_metrics_data_source.resource_id))
    )
    error_message = "aggregate_metrics_data_source must be the aggregate-only Azure Monitor workspace contract that allows managed_grafana."
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

variable "location" {
  description = "Azure region for Azure Managed Grafana."
  type        = string
}

variable "name" {
  description = "Azure Managed Grafana workspace name."
  type        = string

  validation {
    condition     = length(var.name) >= 2 && length(var.name) <= 23 && can(regex("^[a-zA-Z0-9][a-zA-Z0-9-]*[a-zA-Z0-9]$", var.name))
    error_message = "name must be 2 to 23 characters, alphanumeric or hyphen, and start and end with an alphanumeric character."
  }
}

variable "public_network_access_enabled" {
  description = "Whether the native Azure Managed Grafana endpoint is reachable over the public interface."
  type        = bool
  default     = true
}

variable "resource_group_name" {
  description = "Resource group name for Azure Managed Grafana."
  type        = string
}

variable "sku_size" {
  description = "Azure Managed Grafana SKU size."
  type        = string
  default     = "X1"

  validation {
    condition     = contains(["X1", "X2"], var.sku_size)
    error_message = "sku_size must be X1 or X2."
  }
}

variable "tags" {
  description = "Tags assigned to Azure Managed Grafana."
  type        = map(string)
  default     = {}
}

variable "zone_redundancy_enabled" {
  description = "Whether Azure Managed Grafana zone redundancy is enabled."
  type        = bool
  default     = false
}
