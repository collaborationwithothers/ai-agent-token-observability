data "azurerm_client_config" "current" {}

resource "terraform_data" "workspace_guard" {
  input = local.expected_workspace_name

  lifecycle {
    precondition {
      condition     = local.workspace_name_matches_context
      error_message = "The selected Terraform workspace must match {environment}_{azureRegion}_{customerOrganizationSlug}, must not be default, and terraform_workspace_name must match it when supplied."
    }

    precondition {
      condition     = contains(var.allowed_regions, var.azure_region)
      error_message = "azure_region must be included in allowed_regions."
    }

    precondition {
      condition     = contains(var.allowed_customer_organization_slugs, var.customer_organization_slug)
      error_message = "customer_organization_slug must be included in allowed_customer_organization_slugs."
    }
  }
}

resource "terraform_data" "upstream_contract_guard" {
  input = local.stage_name

  lifecycle {
    precondition {
      condition     = local.diagnostic_workspace_resource_id != null
      error_message = "diagnostic_destinations must include ai_services.log_analytics_workspace_resource_id from observability_foundation."
    }

    precondition {
      condition     = !var.llm_assisted_recommendations_enabled || contains(keys(var.model_deployments), "recommendation-writer-primary")
      error_message = "model_deployments must include recommendation-writer-primary when llm_assisted_recommendations_enabled is true."
    }
  }
}

module "ai_resource_group" {
  source = "../../modules/resource_group"

  name     = local.ai_resource_group_name
  location = var.azure_region
  tags     = local.common_tags

  depends_on = [terraform_data.workspace_guard]
}

module "ai_services_account" {
  source = "../../modules/cognitive_services_account"

  name                  = local.ai_services_account_name
  parent_id             = module.ai_resource_group.id
  location              = module.ai_resource_group.location
  kind                  = "AIServices"
  sku_name              = var.ai_services_sku_name
  tags                  = local.common_tags
  custom_subdomain_name = local.ai_services_subdomain_name

  allow_project_management = true
  cognitive_deployments    = local.recommendation_model_deployments
  diagnostic_settings      = local.ai_services_diagnostic_settings
  managed_identities = {
    system_assigned            = true
    user_assigned_resource_ids = var.ai_services_user_assigned_identity_resource_ids
  }
  network_acls                            = var.ai_services_network_acls
  network_injections                      = var.ai_services_network_injections
  outbound_network_access_restricted      = var.ai_services_outbound_network_access_restricted
  private_endpoints                       = var.ai_services_private_endpoints
  private_endpoints_manage_dns_zone_group = var.ai_services_private_endpoints_manage_dns_zone_group
  public_network_access_enabled           = var.ai_services_public_network_access_enabled
  rai_policies                            = var.rai_policies
  role_assignments                        = local.ai_services_role_assignments

  depends_on = [terraform_data.upstream_contract_guard]
}
