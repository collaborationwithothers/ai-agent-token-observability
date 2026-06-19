locals {
  stage_name                        = "observability_foundation"
  expected_workspace_name           = "${var.environment}_${var.azure_region}_${var.customer_organization_slug}"
  configured_workspace_name         = coalesce(var.terraform_workspace_name, terraform.workspace)
  workspace_name_matches_context    = terraform.workspace == local.expected_workspace_name && local.configured_workspace_name == terraform.workspace && terraform.workspace != "default"
  workspace_parts                   = split("_", terraform.workspace)
  name_prefix                       = "to-${var.environment}-${var.resource_instance}"
  observability_resource_group_name = coalesce(var.observability_resource_group_name, "rg-to-${var.environment}-${var.azure_region}-${var.customer_organization_slug}-observability")
  log_analytics_workspace_name      = coalesce(var.log_analytics_workspace_name, "log-${local.name_prefix}-${var.azure_region}-${var.customer_organization_slug}")
  application_insights_name         = coalesce(var.application_insights_name, "appi-${local.name_prefix}-${var.azure_region}-${var.customer_organization_slug}")
  monitor_workspace_name            = coalesce(var.monitor_workspace_name, "amw-${local.name_prefix}-${var.azure_region}-${var.customer_organization_slug}")

  common_tags = merge(var.tags, {
    environment                = var.environment
    region                     = var.azure_region
    customer_organization_slug = var.customer_organization_slug
    terraform_stage            = local.stage_name
  })

  diagnostic_destination_contracts = {
    container_apps = {
      log_analytics_workspace_resource_id = module.log_analytics_workspace.resource_id
      destination_type                    = "Dedicated"
      expected_log_groups                 = ["allLogs"]
      expected_metric_categories          = ["AllMetrics"]
      consumer_stage                      = "app_runtime"
    }
    container_app_jobs = {
      log_analytics_workspace_resource_id = module.log_analytics_workspace.resource_id
      destination_type                    = "Dedicated"
      expected_log_groups                 = ["allLogs"]
      expected_metric_categories          = ["AllMetrics"]
      consumer_stage                      = "app_runtime"
    }
    front_door = {
      log_analytics_workspace_resource_id = module.log_analytics_workspace.resource_id
      destination_type                    = "Dedicated"
      expected_log_categories             = ["FrontDoorAccessLog", "FrontDoorHealthProbeLog", "FrontDoorWebApplicationFirewallLog"]
      expected_metric_categories          = ["AllMetrics"]
      consumer_stage                      = "edge"
    }
    data_platform = {
      log_analytics_workspace_resource_id = module.log_analytics_workspace.resource_id
      destination_type                    = "Dedicated"
      expected_log_groups                 = ["audit", "allLogs"]
      expected_metric_categories          = ["AllMetrics"]
      consumer_stage                      = "data_platform"
    }
    ai_services = {
      log_analytics_workspace_resource_id = module.log_analytics_workspace.resource_id
      application_insights_resource_id    = module.application_insights.resource_id
      destination_type                    = "Dedicated"
      expected_log_groups                 = ["audit", "allLogs"]
      expected_metric_categories          = ["AllMetrics"]
      consumer_stage                      = "ai_services"
    }
  }

  aggregate_metrics_data_source = {
    type                                = "azure_monitor_workspace"
    resource_id                         = module.monitor_workspace.resource_id
    name                                = module.monitor_workspace.name
    query_endpoint                      = module.monitor_workspace.query_endpoint
    default_data_collection_endpoint_id = module.monitor_workspace.default_data_collection_endpoint_id
    default_data_collection_rule_id     = module.monitor_workspace.default_data_collection_rule_id
    consumer_stages                     = ["managed_grafana", "app_runtime", "slo", "alerts"]
    boundary                            = "aggregate_metrics_only"
  }

  trace_log_data_source = {
    type                                = "application_insights_log_analytics"
    application_insights_resource_id    = module.application_insights.resource_id
    application_insights_app_id         = module.application_insights.app_id
    log_analytics_workspace_resource_id = module.log_analytics_workspace.resource_id
    log_analytics_workspace_id          = module.log_analytics_workspace.workspace_id
    consumer_stages                     = ["app_runtime", "data_platform", "ai_services", "alerts"]
    boundary                            = "operational_traces_logs_and_diagnostics"
  }
}
