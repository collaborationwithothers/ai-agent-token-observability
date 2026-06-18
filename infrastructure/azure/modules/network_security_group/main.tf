module "network_security_group" {
  source  = "Azure/avm-res-network-networksecuritygroup/azurerm"
  version = "0.5.1"

  enable_telemetry    = false
  location            = var.location
  name                = var.name
  resource_group_name = var.resource_group_name
  security_rules      = var.security_rules
  tags                = var.tags
}
