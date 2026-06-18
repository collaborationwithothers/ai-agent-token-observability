module "key_vault" {
  source  = "Azure/avm-res-keyvault-vault/azurerm"
  version = "0.10.2"

  enable_telemetry               = false
  legacy_access_policies_enabled = false
  location                       = var.location
  name                           = var.name
  public_network_access_enabled  = var.public_network_access_enabled
  purge_protection_enabled       = var.purge_protection_enabled
  resource_group_name            = var.resource_group_name
  role_assignments               = var.role_assignments
  sku_name                       = var.sku_name
  tags                           = var.tags
  tenant_id                      = var.tenant_id

  keys          = {}
  secrets       = {}
  secrets_value = null

  network_acls = var.public_network_access_enabled ? null : {
    bypass                     = "None"
    default_action             = "Deny"
    ip_rules                   = []
    virtual_network_subnet_ids = []
  }
}
