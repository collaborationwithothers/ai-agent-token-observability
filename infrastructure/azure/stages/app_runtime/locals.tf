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
  container_app_diagnostic_targets   = merge({ environment = azurerm_container_app_environment.this.id }, { for key, app in azurerm_container_app.services : key => app.id }, { for key, job in module.container_app_jobs : key => job.resource_id })
  diagnostic_settings_enabled        = var.log_analytics_workspace_id != null
  log_analytics_environment_required = var.container_app_environment_logs_destination == "log-analytics"
  acr_pull_role_assignments_enabled  = var.container_registry_server != null && var.container_registry_id != null

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

  shared_job_environment = {
    CUSTOMER_ORGANIZATION_SLUG        = var.customer_organization_slug
    TOKENOBSERVABILITY_APP_ROLE       = "product-jobs"
    TOKENOBSERVABILITY_JOB_IMAGE_ROLE = "shared-jobs"
  }

  container_app_jobs = {
    normalize_telemetry = {
      name         = "${local.name_prefix}-jnorm"
      command      = "normalize-telemetry"
      cpu          = 0.5
      memory       = "1.0Gi"
      timeout      = 900
      retry        = 3
      trigger      = local.manual_job_trigger
      max_replicas = 3
      environment = {
        TOKENOBSERVABILITY_JOB_RESPONSIBILITY = "normalization"
      }
    }

    detect_hotspots = {
      name         = "${local.name_prefix}-jhotspot"
      command      = "detect-hotspots"
      cpu          = 0.5
      memory       = "1.0Gi"
      timeout      = 900
      retry        = 3
      trigger      = local.manual_job_trigger
      max_replicas = 3
      environment = {
        TOKENOBSERVABILITY_JOB_RESPONSIBILITY = "hotspot-detection"
      }
    }

    generate_recommendations = {
      name         = "${local.name_prefix}-jrecs"
      command      = "generate-recommendations"
      cpu          = 1.0
      memory       = "2.0Gi"
      timeout      = 1800
      retry        = 2
      trigger      = local.manual_job_trigger
      max_replicas = 2
      environment = {
        TOKENOBSERVABILITY_JOB_RESPONSIBILITY = "recommendation-generation"
      }
    }

    redact_content = {
      name         = "${local.name_prefix}-jredact"
      command      = "redact-content"
      cpu          = 1.0
      memory       = "2.0Gi"
      timeout      = 900
      retry        = 2
      trigger      = local.manual_job_trigger
      max_replicas = 2
      environment = {
        TOKENOBSERVABILITY_JOB_RESPONSIBILITY = "content-redaction"
      }
    }

    refresh_pricing = {
      name         = "${local.name_prefix}-jprice"
      command      = "refresh-pricing"
      cpu          = 0.5
      memory       = "1.0Gi"
      timeout      = 900
      retry        = 2
      trigger      = local.manual_job_trigger
      max_replicas = 1
      environment = {
        TOKENOBSERVABILITY_JOB_RESPONSIBILITY = "pricing-refresh"
      }
    }

    retention_cleanup = {
      name         = "${local.name_prefix}-jretention"
      command      = "retention-cleanup"
      cpu          = 0.5
      memory       = "1.0Gi"
      timeout      = 1800
      retry        = 2
      trigger      = local.manual_job_trigger
      max_replicas = 1
      environment = {
        TOKENOBSERVABILITY_JOB_RESPONSIBILITY = "retention-cleanup"
      }
    }

    reprocess_session = {
      name         = "${local.name_prefix}-jreprocess"
      command      = "reprocess-session"
      cpu          = 0.5
      memory       = "1.0Gi"
      timeout      = 1200
      retry        = 1
      trigger      = local.manual_job_trigger
      max_replicas = 1
      environment = {
        TOKENOBSERVABILITY_JOB_RESPONSIBILITY = "session-reprocessing"
      }
    }

    tenant_maintenance = {
      name         = "${local.name_prefix}-jtenant"
      command      = "tenant-maintenance"
      cpu          = 0.5
      memory       = "1.0Gi"
      timeout      = 900
      retry        = 2
      trigger      = local.manual_job_trigger
      max_replicas = 1
      environment = {
        TOKENOBSERVABILITY_JOB_RESPONSIBILITY = "tenant-maintenance"
      }
    }
  }

  manual_job_trigger = {
    manual_trigger_config = {
      parallelism              = 1
      replica_completion_count = 1
    }
  }

  container_app_job_environment = {
    for job_key, job in local.container_app_jobs :
    job_key => merge(
      local.shared_job_environment,
      job.environment,
      lookup(var.container_app_job_additional_environment, job_key, {}),
      {
        TOKENOBSERVABILITY_JOB_COMMAND = job.command
      }
    )
  }

  container_app_job_key_vault_secrets = {
    for job_key, secrets in var.container_app_job_key_vault_secret_ids :
    job_key => [
      for secret_name, secret_id in secrets : {
        name                = secret_name
        key_vault_secret_id = secret_id
        identity            = azurerm_user_assigned_identity.jobs[job_key].id
      }
    ]
  }
}
