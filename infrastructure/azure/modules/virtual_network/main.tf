module "virtual_network" {
  source  = "Azure/avm-res-network-virtualnetwork/azurerm"
  version = "0.19.0"

  address_space    = toset(var.address_space)
  enable_telemetry = false
  location         = var.location
  name             = var.name
  parent_id        = var.parent_id
  subnets          = var.subnets
  tags             = var.tags
}
