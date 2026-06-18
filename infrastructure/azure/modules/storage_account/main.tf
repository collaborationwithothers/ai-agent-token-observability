module "storage_account" {
  source  = "Azure/avm-res-storage-storageaccount/azurerm"
  version = "0.7.2"

  enable_telemetry = false

  name      = var.name
  parent_id = var.parent_id
  location  = var.location
  tags      = var.tags

  access_tier                         = "Hot"
  account_kind                        = "StorageV2"
  account_sku_name                    = var.account_sku_name
  allow_nested_items_to_be_public     = false
  blob_properties                     = var.blob_properties
  containers                          = var.containers
  cross_tenant_replication_enabled    = false
  default_to_oauth_authentication     = true
  diagnostic_settings_blob            = var.diagnostic_settings_blob
  diagnostic_settings_storage_account = var.diagnostic_settings_storage_account
  https_traffic_only_enabled          = true
  infrastructure_encryption_enabled   = true
  local_user_enabled                  = false
  managed_identities                  = var.managed_identities
  min_tls_version                     = "TLS1_2"
  network_rules                       = var.network_rules
  private_endpoints                   = var.private_endpoints
  public_network_access_enabled       = false
  role_assignments                    = var.role_assignments
  shared_access_key_enabled           = false
  storage_management_policy_rule      = var.storage_management_policy_rule
}
