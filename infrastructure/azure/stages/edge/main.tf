resource "azurerm_resource_group" "edge" {
  name     = local.edge_resource_group_name
  location = var.azure_region
  tags     = local.common_tags

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

    precondition {
      condition     = var.front_door_sku == "Premium_AzureFrontDoor"
      error_message = "The edge stage must use Premium_AzureFrontDoor."
    }

    precondition {
      condition     = !contains(["pp", "pd"], var.environment) || local.waf_policy_mode == "Prevention"
      error_message = "pp and pd edge deployments must use WAF Prevention mode."
    }

  }
}

module "front_door_edge" {
  source = "../../modules/front_door_edge"

  name_prefix                = local.name_prefix
  resource_group_name        = azurerm_resource_group.edge.name
  location                   = azurerm_resource_group.edge.location
  tags                       = local.common_tags
  front_door_sku             = var.front_door_sku
  waf_policy_mode            = local.waf_policy_mode
  waf_rate_limits            = var.waf_rate_limits
  public_ingress_hostnames   = var.public_ingress_hostnames
  container_app_fqdns        = var.container_app_fqdns
  azure_dns_zone             = var.azure_dns_zone
  log_analytics_workspace_id = var.log_analytics_workspace_id
}
