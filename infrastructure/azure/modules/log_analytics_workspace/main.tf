module "log_analytics_workspace" {
  source  = "Azure/avm-res-operationalinsights-workspace/azurerm"
  version = "0.5.1"

  enable_telemetry = false

  name                = var.name
  location            = var.location
  resource_group_name = var.resource_group_name
  tags                = var.tags

  log_analytics_workspace_sku                          = var.sku
  log_analytics_workspace_retention_in_days            = var.retention_in_days
  log_analytics_workspace_daily_quota_gb               = var.daily_quota_gb
  log_analytics_workspace_internet_ingestion_enabled   = var.internet_ingestion_enabled ? "true" : "false"
  log_analytics_workspace_internet_query_enabled       = var.internet_query_enabled ? "true" : "false"
  log_analytics_workspace_local_authentication_enabled = var.local_authentication_enabled
}
