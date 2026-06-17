module "managed_identity" {
  source  = "Azure/avm-res-managedidentity-userassignedidentity/azurerm"
  version = "0.5.0"

  enable_telemetry               = false
  federated_identity_credentials = var.federated_identity_credentials
  location                       = var.location
  name                           = var.name
  resource_group_name            = var.resource_group_name
  role_assignments               = var.role_assignments
  tags                           = var.tags
}
