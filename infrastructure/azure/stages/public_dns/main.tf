resource "azurerm_resource_group" "public_dns" {
  name     = local.public_dns_resource_group_name
  location = var.azure_region
  tags     = local.common_tags

  lifecycle {
    precondition {
      condition     = local.workspace_name_matches_context
      error_message = "The selected Terraform workspace must match {environment}_{azureRegion}_{customerOrganizationSlug}, must not be default, and terraform_workspace_name must match it when supplied."
    }

    precondition {
      condition     = local.expected_workspace_name == "pd_eastus2_internal"
      error_message = "The retained public_dns stage must be planned from pd_eastus2_internal so the shared product DNS zone has one Terraform owner."
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

resource "azurerm_dns_zone" "product" {
  name                = var.product_dns_zone_name
  resource_group_name = azurerm_resource_group.public_dns.name
  tags                = local.common_tags

  lifecycle {
    prevent_destroy = true

    precondition {
      condition     = var.product_dns_zone_name == "tokenobs.consultwithcloud.com"
      error_message = "Only the delegated tokenobs.consultwithcloud.com product subdomain is managed by this stage."
    }
  }
}
