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

module "observability_resource_group" {
  source = "../../modules/resource_group"

  name     = local.observability_resource_group_name
  location = var.azure_region
  tags     = local.common_tags

  depends_on = [terraform_data.workspace_guard]
}

module "log_analytics_workspace" {
  source = "../../modules/log_analytics_workspace"

  name                         = local.log_analytics_workspace_name
  resource_group_name          = module.observability_resource_group.name
  location                     = module.observability_resource_group.location
  sku                          = var.log_analytics_workspace_sku
  retention_in_days            = var.log_analytics_retention_in_days
  daily_quota_gb               = var.log_analytics_daily_quota_gb
  internet_ingestion_enabled   = var.log_analytics_internet_ingestion_enabled
  internet_query_enabled       = var.log_analytics_internet_query_enabled
  local_authentication_enabled = var.log_analytics_local_authentication_enabled
  tags                         = local.common_tags

  depends_on = [terraform_data.workspace_guard]
}

module "application_insights" {
  source = "../../modules/application_insights"

  name                                  = local.application_insights_name
  resource_group_name                   = module.observability_resource_group.name
  location                              = module.observability_resource_group.location
  workspace_id                          = module.log_analytics_workspace.resource_id
  application_type                      = var.application_insights_application_type
  daily_data_cap_in_gb                  = var.application_insights_daily_data_cap_in_gb
  daily_data_cap_notifications_disabled = var.application_insights_daily_data_cap_notifications_disabled
  disable_ip_masking                    = var.application_insights_disable_ip_masking
  internet_ingestion_enabled            = var.application_insights_internet_ingestion_enabled
  internet_query_enabled                = var.application_insights_internet_query_enabled
  local_authentication_disabled         = var.application_insights_local_authentication_disabled
  retention_in_days                     = var.application_insights_retention_in_days
  sampling_percentage                   = var.application_insights_sampling_percentage
  tags                                  = local.common_tags

  depends_on = [terraform_data.workspace_guard]
}

module "monitor_workspace" {
  source = "../../modules/monitor_workspace"

  name                          = local.monitor_workspace_name
  resource_group_name           = module.observability_resource_group.name
  location                      = module.observability_resource_group.location
  public_network_access_enabled = var.monitor_workspace_public_network_access_enabled
  tags                          = local.common_tags

  depends_on = [terraform_data.workspace_guard]
}
