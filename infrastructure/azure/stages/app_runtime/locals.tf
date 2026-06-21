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

  app_runtime_resource_group_name = coalesce(var.app_runtime_resource_group_name, "rg-to-${var.environment}-${var.azure_region}-${var.customer_organization_slug}-app")
  container_app_environment_name  = coalesce(var.container_app_environment_name, "${local.name_prefix}-env")
  container_app_environment_diagnostic_targets = {
    environment = azurerm_container_app_environment.this.id
  }
  container_app_service_diagnostic_targets  = { for key, app in azurerm_container_app.services : key => app.id }
  container_app_job_diagnostic_targets      = { for key, job in module.container_app_jobs : key => job.resource_id }
  container_apps_diagnostic_contract        = var.diagnostic_destinations["container_apps"]
  container_jobs_diagnostic_contract        = var.diagnostic_destinations["container_app_jobs"]
  container_apps_log_analytics_workspace_id = coalesce(var.log_analytics_workspace_id, local.container_apps_diagnostic_contract.log_analytics_workspace_resource_id)
  container_jobs_log_analytics_workspace_id = coalesce(var.log_analytics_workspace_id, local.container_jobs_diagnostic_contract.log_analytics_workspace_resource_id)
  diagnostic_settings_enabled               = local.container_apps_log_analytics_workspace_id != null && local.container_jobs_log_analytics_workspace_id != null
  log_analytics_environment_required        = var.container_app_environment_logs_destination == "log-analytics"
  acr_pull_role_assignments_enabled         = var.container_registry_server != null && var.container_registry_id != null
  container_app_environment_subnet_id       = var.container_app_environment_infrastructure_subnet_id
  primary_database_name                     = try(var.data_platform_configuration_contract.postgresql_database_names["product_metadata"], null)
  product_api_metadata_store_secret_configured = (
    contains(keys(lookup(var.container_app_secret_names, "product_api", {})), "ConnectionStrings__ProductMetadataStore") &&
    contains(keys(lookup(var.container_app_key_vault_secret_ids, "product_api", {})), lookup(var.container_app_secret_names, "product_api", {})["ConnectionStrings__ProductMetadataStore"])
  )

  data_platform_environment = {
    TOKENOBSERVABILITY_POSTGRESQL_SERVER_FQDN              = var.data_platform_configuration_contract.postgresql_server_fqdn
    TOKENOBSERVABILITY_POSTGRESQL_DATABASE_NAME            = local.primary_database_name
    TOKENOBSERVABILITY_STORAGE_ACCOUNT_NAME                = var.data_platform_configuration_contract.storage_account_name
    TOKENOBSERVABILITY_CAPTURED_CONTENT_CONTAINER_NAME     = var.data_platform_configuration_contract.captured_content_storage_contract.captured_content_container_name
    TOKENOBSERVABILITY_CONTENT_REVIEW_CONTAINER_NAME       = var.data_platform_configuration_contract.captured_content_storage_contract.content_review_container_name
    TOKENOBSERVABILITY_OPERATIONAL_ARTIFACT_CONTAINER_NAME = var.data_platform_configuration_contract.operational_storage_contract.operational_container_name
  }

  ai_services_environment = {
    TOKENOBSERVABILITY_AI_SERVICES_ENDPOINT             = var.ai_services_configuration_contract.endpoint
    TOKENOBSERVABILITY_AI_SERVICES_ACCOUNT_NAME         = var.ai_services_configuration_contract.account_name
    TOKENOBSERVABILITY_RECOMMENDATION_MODEL_ALIASES     = join(",", var.ai_services_configuration_contract.recommendation_model_deployment_aliases)
    TOKENOBSERVABILITY_LANGUAGE_PII_ENDPOINT            = var.language_pii_detection_contract.endpoint
    TOKENOBSERVABILITY_CONTENT_SAFETY_ENDPOINT          = var.content_safety_contract.endpoint
    TOKENOBSERVABILITY_RECOMMENDATION_DEPLOYMENT_COUNT  = tostring(length(var.recommendation_model_deployment_contracts))
    TOKENOBSERVABILITY_AI_PUBLIC_NETWORK_ACCESS_ENABLED = tostring(var.ai_services_configuration_contract.public_network_access_enabled)
  }

  upstream_contract_environment = {
    for key, value in merge(local.data_platform_environment, local.ai_services_environment) : key => value
    if value != null && value != ""
  }

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
      startup_path     = "/healthz"
      liveness_path    = "/healthz"
      readiness_path   = "/readyz"
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
      startup_path     = "/health/live"
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
      startup_path     = "/health/live"
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
    app_key => merge(app.environment, local.upstream_contract_environment, lookup(var.container_app_additional_environment, app_key, {}))
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
      local.upstream_contract_environment,
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
