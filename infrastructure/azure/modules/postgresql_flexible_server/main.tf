module "postgresql_flexible_server" {
  source  = "Azure/avm-res-dbforpostgresql-flexibleserver/azurerm"
  version = "0.2.2"

  enable_telemetry = false

  name                = var.name
  resource_group_name = var.resource_group_name
  location            = var.location
  tags                = var.tags

  administrator_login    = null
  administrator_password = null
  authentication = {
    active_directory_auth_enabled = true
    password_auth_enabled         = false
    tenant_id                     = var.tenant_id
  }

  ad_administrator                        = var.ad_administrators
  auto_grow_enabled                       = var.auto_grow_enabled
  backup_retention_days                   = var.backup_retention_days
  databases                               = var.databases
  delegated_subnet_id                     = var.delegated_subnet_id
  diagnostic_settings                     = var.diagnostic_settings
  firewall_rules                          = {}
  geo_redundant_backup_enabled            = var.geo_redundant_backup_enabled
  private_dns_zone_id                     = var.private_dns_zone_id
  public_network_access_enabled           = false
  role_assignments                        = var.role_assignments
  server_version                          = var.server_version
  sku_name                                = var.sku_name
  storage_mb                              = var.storage_mb
  storage_tier                            = var.storage_tier
  private_endpoints                       = {}
  private_endpoints_manage_dns_zone_group = true

  high_availability = var.zone_redundant_high_availability_enabled ? {
    mode = "ZoneRedundant"
  } : null

  managed_identities = {
    system_assigned            = true
    user_assigned_resource_ids = var.user_assigned_identity_resource_ids
  }
}
