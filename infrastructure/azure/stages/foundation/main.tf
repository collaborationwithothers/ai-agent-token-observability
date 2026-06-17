resource "azurerm_resource_group" "foundation" {
  name     = local.foundation_resource_group_name
  location = var.azure_region
  tags     = local.common_tags

  lifecycle {
    precondition {
      condition     = local.workspace_name_matches_context
      error_message = "The selected Terraform workspace must match {environment}_{azureRegion}_{customerOrganizationSlug}, and terraform_workspace_name must match it when supplied."
    }

    precondition {
      condition     = local.deployment_identity_configured
      error_message = "Foundation must either create a deployment identity or receive at least one deployment_identity_references entry."
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

module "deployment_identity" {
  source   = "../../modules/managed_identity"
  for_each = var.create_deployment_identity ? { deployment = local.deployment_identity_name } : {}

  name                = each.value
  resource_group_name = azurerm_resource_group.foundation.name
  location            = azurerm_resource_group.foundation.location
  tags                = local.common_tags
}

module "key_vault" {
  source = "../../modules/key_vault"

  name                          = local.key_vault_name
  resource_group_name           = azurerm_resource_group.foundation.name
  location                      = azurerm_resource_group.foundation.location
  public_network_access_enabled = var.key_vault_public_network_access_enabled
  purge_protection_enabled      = var.key_vault_purge_protection_enabled
  role_assignments              = local.key_vault_role_assignments
  sku_name                      = var.key_vault_sku_name
  tags                          = local.common_tags
  tenant_id                     = data.azurerm_client_config.current.tenant_id
}
