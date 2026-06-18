data "azurerm_client_config" "current" {}

resource "terraform_data" "workspace_guard" {
  input = local.expected_workspace_name

  lifecycle {
    precondition {
      condition     = local.workspace_name_matches_context
      error_message = "The selected Terraform workspace must match {environment}_{azureRegion}_{customerOrganizationSlug}, must not be default, and terraform_workspace_name must match it when supplied."
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

resource "terraform_data" "upstream_contract_guard" {
  input = local.stage_name

  lifecycle {
    precondition {
      condition     = !var.enable_private_endpoints || local.postgresql_delegated_subnet_id != null
      error_message = "network_subnet_ids must include postgresql_delegated when private data platform access is enabled."
    }

    precondition {
      condition     = !var.enable_private_endpoints || local.storage_private_endpoint_subnet_id != null
      error_message = "network_subnet_ids must include private_endpoints when private data platform access is enabled."
    }

    precondition {
      condition     = !var.enable_private_endpoints || local.postgresql_private_dns_zone_id != null
      error_message = "private_dns_zone_ids must include postgresql_private_access when private data platform access is enabled."
    }

    precondition {
      condition     = !var.enable_private_endpoints || local.blob_private_dns_zone_id != null
      error_message = "private_dns_zone_ids must include blob when private data platform access is enabled."
    }

    precondition {
      condition     = local.diagnostic_workspace_resource_id != null
      error_message = "diagnostic_destinations must include data_platform.log_analytics_workspace_resource_id from observability_foundation."
    }

    precondition {
      condition     = length(var.postgresql_ad_administrators) > 0
      error_message = "postgresql_ad_administrators must include at least one Microsoft Entra administrator because password authentication is disabled."
    }
  }
}

module "data_resource_group" {
  source = "../../modules/resource_group"

  name     = local.data_resource_group_name
  location = var.azure_region
  tags     = local.common_tags

  depends_on = [terraform_data.workspace_guard]
}

module "product_metadata_store" {
  source = "../../modules/postgresql_flexible_server"

  name                                     = local.postgresql_server_name
  resource_group_name                      = module.data_resource_group.name
  location                                 = module.data_resource_group.location
  tenant_id                                = data.azurerm_client_config.current.tenant_id
  delegated_subnet_id                      = local.postgresql_delegated_subnet_id
  private_dns_zone_id                      = local.postgresql_private_dns_zone_id
  server_version                           = var.postgresql_server_version
  sku_name                                 = var.postgresql_sku_name
  storage_mb                               = var.postgresql_storage_mb
  storage_tier                             = var.postgresql_storage_tier
  backup_retention_days                    = var.postgresql_backup_retention_days
  geo_redundant_backup_enabled             = var.postgresql_geo_redundant_backup_enabled
  auto_grow_enabled                        = var.postgresql_auto_grow_enabled
  zone_redundant_high_availability_enabled = var.enable_zone_redundancy
  ad_administrators                        = var.postgresql_ad_administrators
  diagnostic_settings                      = local.postgresql_diagnostic_settings
  role_assignments                         = var.postgresql_role_assignments
  user_assigned_identity_resource_ids      = var.postgresql_user_assigned_identity_resource_ids
  tags                                     = local.common_tags

  databases = {
    product_metadata = {
      name      = var.postgresql_database_name
      charset   = "UTF8"
      collation = "en_US.utf8"
    }
  }

  depends_on = [terraform_data.upstream_contract_guard]
}

module "product_storage" {
  source = "../../modules/storage_account"

  name                                = local.storage_account_name
  parent_id                           = module.data_resource_group.id
  location                            = module.data_resource_group.location
  account_sku_name                    = var.storage_account_sku_name
  blob_properties                     = local.storage_blob_properties
  containers                          = local.storage_containers
  diagnostic_settings_blob            = local.storage_blob_diagnostic_settings
  diagnostic_settings_storage_account = local.storage_account_diagnostic_settings
  network_rules                       = var.storage_network_rules
  private_endpoints                   = local.storage_private_endpoints
  role_assignments                    = var.storage_account_role_assignments
  storage_management_policy_rule      = local.storage_management_policy_rules
  tags                                = local.common_tags

  managed_identities = {
    system_assigned            = true
    user_assigned_resource_ids = var.storage_user_assigned_identity_resource_ids
  }

  depends_on = [terraform_data.upstream_contract_guard]
}
