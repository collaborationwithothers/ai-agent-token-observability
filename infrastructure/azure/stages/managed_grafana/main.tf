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
      condition     = local.aggregate_metrics_data_source.type == "azure_monitor_workspace"
      error_message = "metrics_data_source_identifiers.aggregate_metrics.type must be azure_monitor_workspace."
    }

    precondition {
      condition     = local.aggregate_metrics_data_source.boundary == "aggregate_metrics_only"
      error_message = "metrics_data_source_identifiers.aggregate_metrics.boundary must be aggregate_metrics_only."
    }

    precondition {
      condition     = contains(local.aggregate_metrics_data_source.consumer_stages, local.stage_name)
      error_message = "metrics_data_source_identifiers.aggregate_metrics.consumer_stages must include managed_grafana."
    }

    precondition {
      condition     = can(regex("^/subscriptions/[^/]+/resourceGroups/[^/]+/providers/Microsoft\\.Monitor/accounts/[^/]+$", local.aggregate_metrics_data_source.resource_id))
      error_message = "metrics_data_source_identifiers.aggregate_metrics.resource_id must be an Azure Monitor workspace resource ID."
    }
  }
}

module "managed_grafana" {
  source = "../../modules/managed_grafana"

  name                          = local.grafana_workspace_name
  resource_group_name           = var.observability_resource_group_name
  location                      = var.azure_region
  grafana_major_version         = var.grafana_major_version
  aggregate_metrics_data_source = local.aggregate_metrics_data_source
  public_network_access_enabled = var.grafana_public_network_access_enabled
  sku_size                      = var.grafana_sku_size
  tags                          = local.common_tags
  zone_redundancy_enabled       = var.enable_zone_redundancy

  depends_on = [
    terraform_data.workspace_guard,
    terraform_data.upstream_contract_guard,
  ]
}
