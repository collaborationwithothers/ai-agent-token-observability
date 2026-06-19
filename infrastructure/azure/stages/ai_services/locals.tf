locals {
  stage_name                     = "ai_services"
  expected_workspace_name        = "${var.environment}_${var.azure_region}_${var.customer_organization_slug}"
  configured_workspace_name      = coalesce(var.terraform_workspace_name, terraform.workspace)
  workspace_name_matches_context = terraform.workspace == local.expected_workspace_name && local.configured_workspace_name == terraform.workspace && terraform.workspace != "default"
  workspace_parts                = split("_", terraform.workspace)
  name_prefix                    = "to-${var.environment}-${var.resource_instance}"
  ai_resource_group_name         = coalesce(var.ai_resource_group_name, "rg-to-${var.environment}-${var.azure_region}-${var.customer_organization_slug}-ai")
  ai_services_account_name       = coalesce(var.ai_services_account_name, "ais-${local.name_prefix}-${var.azure_region}-${var.customer_organization_slug}")
  ai_services_subdomain_name     = coalesce(var.ai_services_custom_subdomain_name, local.ai_services_account_name)

  diagnostic_workspace_resource_id = try(var.diagnostic_destinations["ai_services"].log_analytics_workspace_resource_id, null)
  diagnostic_destination_type      = try(var.diagnostic_destinations["ai_services"].destination_type, "Dedicated")

  common_tags = merge(var.tags, {
    environment                = var.environment
    region                     = var.azure_region
    customer_organization_slug = var.customer_organization_slug
    terraform_stage            = local.stage_name
  })

  ai_services_diagnostic_settings = local.diagnostic_workspace_resource_id == null ? {} : {
    log_analytics = {
      name                           = "diag-${local.name_prefix}-ai-services"
      log_groups                     = toset(["allLogs"])
      metric_categories              = toset(["AllMetrics"])
      log_analytics_destination_type = local.diagnostic_destination_type
      workspace_resource_id          = local.diagnostic_workspace_resource_id
    }
  }

  recommendation_model_deployments = {
    for alias, deployment in var.model_deployments : alias => {
      name                       = alias
      rai_policy_name            = try(deployment.rai_policy_name, null)
      version_upgrade_option     = try(deployment.version_upgrade_option, "NoAutoUpgrade")
      dynamic_throttling_enabled = try(deployment.dynamic_throttling_enabled, false)
      model = {
        format  = deployment.model_format
        name    = deployment.model_name
        version = try(deployment.model_version, null)
      }
      scale = {
        type     = deployment.sku_name
        capacity = try(deployment.capacity, 1)
        family   = try(deployment.family, null)
        size     = try(deployment.size, null)
        tier     = try(deployment.tier, null)
      }
      retry    = try(deployment.retry, null)
      timeouts = try(deployment.timeouts, null)
    }
  }

  runtime_ai_role_assignments = {
    for key, principal_id in var.runtime_managed_identity_principal_ids : key => {
      role_definition_id_or_name = "Azure AI User"
      principal_id               = principal_id
      principal_type             = "ServicePrincipal"
      description                = "AI service access for runtime managed identity ${key}."
    }
  }

  ai_services_role_assignments = merge(local.runtime_ai_role_assignments, var.ai_services_role_assignments)

  model_deployment_contracts = {
    for alias, deployment in var.model_deployments : alias => {
      deployment_alias            = alias
      account_resource_id         = module.ai_services_account.resource_id
      account_endpoint            = module.ai_services_account.endpoint
      deployment_resource_id      = try(module.ai_services_account.deployment_resource_ids[alias], null)
      provider                    = "AzureOpenAI"
      region                      = var.azure_region
      structured_outputs_required = try(deployment.structured_outputs_required, true)
      content_filtering_enabled   = true
    }
  }
}
