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

module "network_resource_group" {
  source = "../../modules/resource_group"

  name     = local.network_resource_group_name
  location = var.azure_region
  tags     = local.common_tags

  depends_on = [terraform_data.workspace_guard]
}

module "network_security_groups" {
  source   = "../../modules/network_security_group"
  for_each = local.nsg_names

  name                = each.value
  resource_group_name = module.network_resource_group.name
  location            = module.network_resource_group.location
  security_rules      = local.nsg_security_rules[each.key]
  tags                = local.common_tags

  depends_on = [terraform_data.workspace_guard]
}

module "virtual_network" {
  source = "../../modules/virtual_network"

  name          = local.virtual_network_name
  parent_id     = module.network_resource_group.id
  location      = module.network_resource_group.location
  address_space = var.virtual_network_address_space
  subnets       = local.virtual_network_subnets
  tags          = local.common_tags

  depends_on = [terraform_data.workspace_guard]
}

module "private_dns_zones" {
  source   = "../../modules/private_dns_zone"
  for_each = local.private_dns_zones

  domain_name = each.value.domain_name
  parent_id   = module.network_resource_group.id
  tags        = local.common_tags

  virtual_network_links = {
    primary = {
      name                 = "link-${local.name_prefix}-${each.key}"
      virtual_network_id   = module.virtual_network.id
      registration_enabled = false
      resolution_policy    = "Default"
      tags                 = local.common_tags
    }
  }

  depends_on = [terraform_data.workspace_guard]
}
