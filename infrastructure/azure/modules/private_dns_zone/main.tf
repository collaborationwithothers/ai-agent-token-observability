module "private_dns_zone" {
  source  = "Azure/avm-res-network-privatednszone/azurerm"
  version = "0.5.0"

  domain_name           = var.domain_name
  enable_telemetry      = false
  parent_id             = var.parent_id
  tags                  = var.tags
  virtual_network_links = var.virtual_network_links
}
