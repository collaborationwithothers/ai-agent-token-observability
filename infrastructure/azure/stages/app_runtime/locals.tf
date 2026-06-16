locals {
  stage_name                     = "app_runtime"
  expected_workspace_name        = "${var.environment}_${var.azure_region}_${var.customer_organization_slug}"
  configured_workspace_name      = coalesce(var.terraform_workspace_name, terraform.workspace)
  workspace_name_matches_context = terraform.workspace == local.expected_workspace_name && local.configured_workspace_name == terraform.workspace && terraform.workspace != "default"
  workspace_parts                = split("_", terraform.workspace)
  name_prefix                    = "to-${var.environment}-${var.resource_instance}"

  common_tags = merge(var.tags, {
    environment                = var.environment
    region                     = var.azure_region
    customer_organization_slug = var.customer_organization_slug
    terraform_stage            = local.stage_name
  })

  app_runtime_resource_group_name    = coalesce(var.app_runtime_resource_group_name, "rg-to-${var.environment}-${var.azure_region}-${var.customer_organization_slug}-app")
  container_app_environment_name     = coalesce(var.container_app_environment_name, "${local.name_prefix}-env")
  container_app_diagnostic_targets   = merge({ environment = azurerm_container_app_environment.this.id }, { for key, app in azurerm_container_app.services : key => app.id })
  diagnostic_settings_enabled        = var.log_analytics_workspace_id != null
  log_analytics_environment_required = var.container_app_environment_logs_destination == "log-analytics"

  long_running_container_apps = {
    product_dashboard = {
      name             = "${local.name_prefix}-dashboard"
      container_name   = "product-dashboard"
      image            = var.dashboard_image
      target_port      = var.dashboard_target_port
      cpu              = 0.25
      memory           = "0.5Gi"
      min_replicas     = 1
      max_replicas     = 3
      external_enabled = true
      liveness_path    = "/"
      readiness_path   = "/"
      environment = {
        PORT                        = tostring(var.dashboard_target_port)
        TOKENOBSERVABILITY_APP_ROLE = "product-dashboard"
        CUSTOMER_ORGANIZATION_SLUG  = var.customer_organization_slug
        PRODUCT_API_PUBLIC_HOSTNAME = lookup(var.public_ingress_hostnames, "api", "")
        INGESTION_PUBLIC_HOSTNAME   = lookup(var.public_ingress_hostnames, "ingest", "")
      }
    }

    product_api = {
      name             = "${local.name_prefix}-api"
      container_name   = "product-api"
      image            = var.product_api_image
      target_port      = var.product_api_target_port
      cpu              = 0.5
      memory           = "1.0Gi"
      min_replicas     = 1
      max_replicas     = 5
      external_enabled = true
      liveness_path    = "/health/live"
      readiness_path   = "/health/ready"
      environment = {
        ASPNETCORE_HTTP_PORTS       = tostring(var.product_api_target_port)
        TOKENOBSERVABILITY_APP_ROLE = "product-api"
        CUSTOMER_ORGANIZATION_SLUG  = var.customer_organization_slug
        DASHBOARD_PUBLIC_HOSTNAME   = lookup(var.public_ingress_hostnames, "app", "")
      }
    }

    product_ingestion_endpoint = {
      name             = "${local.name_prefix}-ingestion"
      container_name   = "product-ingestion"
      image            = var.product_ingestion_image
      target_port      = var.product_ingestion_target_port
      cpu              = 0.5
      memory           = "1.0Gi"
      min_replicas     = 1
      max_replicas     = 10
      external_enabled = true
      liveness_path    = "/health/live"
      readiness_path   = "/health/ready"
      environment = {
        ASPNETCORE_HTTP_PORTS       = tostring(var.product_ingestion_target_port)
        TOKENOBSERVABILITY_APP_ROLE = "product-ingestion-endpoint"
        CUSTOMER_ORGANIZATION_SLUG  = var.customer_organization_slug
      }
    }
  }

  container_app_environment = {
    for app_key, app in local.long_running_container_apps :
    app_key => merge(app.environment, lookup(var.container_app_additional_environment, app_key, {}))
  }
}
