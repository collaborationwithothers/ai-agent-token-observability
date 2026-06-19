resource "azurerm_resource_group" "app_runtime" {
  name     = local.app_runtime_resource_group_name
  location = var.azure_region
  tags     = local.common_tags

  lifecycle {
    precondition {
      condition     = local.workspace_name_matches_context
      error_message = "The selected Terraform workspace must match {environment}_{azureRegion}_{customerOrganizationSlug}, must not be default, and terraform_workspace_name must match it when supplied."
    }
  }
}

resource "azurerm_container_app_environment" "this" {
  name                               = local.container_app_environment_name
  location                           = azurerm_resource_group.app_runtime.location
  resource_group_name                = azurerm_resource_group.app_runtime.name
  logs_destination                   = var.container_app_environment_logs_destination
  log_analytics_workspace_id         = local.log_analytics_environment_required ? var.log_analytics_workspace_id : null
  infrastructure_subnet_id           = var.container_app_environment_infrastructure_subnet_id
  public_network_access              = var.container_app_environment_public_network_access
  zone_redundancy_enabled            = var.container_app_environment_infrastructure_subnet_id == null ? null : var.enable_zone_redundancy
  infrastructure_resource_group_name = "rg-${local.name_prefix}-aca-infra"
  tags                               = local.common_tags

  workload_profile {
    name                  = "Consumption"
    workload_profile_type = "Consumption"
  }

  lifecycle {
    precondition {
      condition     = local.workspace_name_matches_context
      error_message = "The selected Terraform workspace must match {environment}_{azureRegion}_{customerOrganizationSlug}, must not be default, and terraform_workspace_name must match it when supplied."
    }

    precondition {
      condition     = !local.log_analytics_environment_required || var.log_analytics_workspace_id != null
      error_message = "log_analytics_workspace_id is required when container_app_environment_logs_destination is log-analytics."
    }

    precondition {
      condition     = !var.enable_zone_redundancy || var.container_app_environment_infrastructure_subnet_id != null
      error_message = "container_app_environment_infrastructure_subnet_id is required when enable_zone_redundancy is true."
    }

  }
}

resource "azurerm_user_assigned_identity" "services" {
  for_each = local.long_running_container_apps

  name                = "${each.value.name}-mi"
  location            = azurerm_resource_group.app_runtime.location
  resource_group_name = azurerm_resource_group.app_runtime.name
  tags                = local.common_tags
}

resource "azurerm_container_app" "services" {
  for_each = local.long_running_container_apps

  name                         = each.value.name
  container_app_environment_id = azurerm_container_app_environment.this.id
  resource_group_name          = azurerm_resource_group.app_runtime.name
  revision_mode                = "Single"
  workload_profile_name        = "Consumption"
  tags                         = local.common_tags

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.services[each.key].id]
  }

  dynamic "registry" {
    for_each = var.container_registry_server == null ? [] : [var.container_registry_server]

    content {
      server   = registry.value
      identity = azurerm_user_assigned_identity.services[each.key].id
    }
  }

  dynamic "secret" {
    for_each = lookup(var.container_app_key_vault_secret_ids, each.key, {})

    content {
      name                = secret.key
      key_vault_secret_id = secret.value
      identity            = azurerm_user_assigned_identity.services[each.key].id
    }
  }

  ingress {
    external_enabled           = each.value.external_enabled
    target_port                = each.value.target_port
    transport                  = "http"
    allow_insecure_connections = false

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    min_replicas = each.value.min_replicas
    max_replicas = each.value.max_replicas

    container {
      name   = each.value.container_name
      image  = each.value.image
      cpu    = each.value.cpu
      memory = each.value.memory

      dynamic "env" {
        for_each = local.container_app_environment[each.key]

        content {
          name  = env.key
          value = env.value
        }
      }

      dynamic "env" {
        for_each = lookup(var.container_app_secret_names, each.key, {})

        content {
          name        = env.key
          secret_name = env.value
        }
      }

      liveness_probe {
        transport               = "HTTP"
        port                    = each.value.target_port
        path                    = each.value.liveness_path
        initial_delay           = 10
        interval_seconds        = 30
        timeout                 = 5
        failure_count_threshold = 3
      }

      readiness_probe {
        transport               = "HTTP"
        port                    = each.value.target_port
        path                    = each.value.readiness_path
        interval_seconds        = 10
        timeout                 = 5
        success_count_threshold = 1
        failure_count_threshold = 3
      }
    }
  }
}

module "container_app_jobs" {
  source   = "../../modules/container_app_job"
  for_each = local.container_app_jobs

  name                                  = each.value.name
  resource_group_name                   = azurerm_resource_group.app_runtime.name
  location                              = azurerm_resource_group.app_runtime.location
  container_app_environment_resource_id = azurerm_container_app_environment.this.id
  workload_profile_name                 = "Consumption"
  tags                                  = local.common_tags

  managed_identities = {
    system_assigned = true
  }

  registries                 = local.container_app_job_registries
  key_vault_secrets          = lookup(local.container_app_job_key_vault_secrets, each.key, [])
  replica_retry_limit        = each.value.retry
  replica_timeout_in_seconds = each.value.timeout
  trigger_config             = each.value.trigger

  template = {
    min_replicas = 0
    max_replicas = each.value.max_replicas
    container = {
      name    = "product-jobs"
      image   = var.shared_jobs_image
      cpu     = each.value.cpu
      memory  = each.value.memory
      command = ["dotnet", "TokenObservability.Jobs.dll"]
      args    = [each.value.command]
      env = concat(
        [
          for name, value in local.container_app_job_environment[each.key] : {
            name  = name
            value = value
          }
        ],
        [
          for name, secret_name in lookup(var.container_app_job_secret_names, each.key, {}) : {
            name        = name
            secret_name = secret_name
          }
        ]
      )
    }
  }
}

resource "azurerm_monitor_diagnostic_setting" "container_apps" {
  for_each = local.diagnostic_settings_enabled ? local.container_app_diagnostic_targets : {}

  name                           = "${local.name_prefix}-${each.key}-diag"
  target_resource_id             = each.value
  log_analytics_workspace_id     = var.log_analytics_workspace_id
  log_analytics_destination_type = "Dedicated"

  enabled_log {
    category_group = "allLogs"
  }

  enabled_metric {
    category = "AllMetrics"
  }
}
