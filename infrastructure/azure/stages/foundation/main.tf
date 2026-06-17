resource "azurerm_resource_group" "foundation" {
  name     = local.foundation_resource_group_name
  location = var.azure_region
  tags     = local.common_tags

  lifecycle {
    precondition {
      condition     = local.workspace_name_matches_context
      error_message = "The selected Terraform workspace must match {environment}_{azureRegion}_{customerOrganizationSlug}, and terraform_workspace_name must match it when supplied."
    }
  }
}

resource "azurerm_container_registry" "shared" {
  name                = local.container_registry_name
  resource_group_name = azurerm_resource_group.foundation.name
  location            = azurerm_resource_group.foundation.location
  sku                 = var.container_registry_sku
  admin_enabled       = false
  tags                = local.common_tags

  lifecycle {
    precondition {
      condition     = local.workspace_name_matches_context
      error_message = "The selected Terraform workspace must match {environment}_{azureRegion}_{customerOrganizationSlug}, and terraform_workspace_name must match it when supplied."
    }
  }
}
