locals {
  stage_name                     = "managed_grafana"
  expected_workspace_name        = "${var.environment}_${var.azure_region}_${var.customer_organization_slug}"
  configured_workspace_name      = coalesce(var.terraform_workspace_name, terraform.workspace)
  workspace_name_matches_context = terraform.workspace == local.expected_workspace_name && local.configured_workspace_name == terraform.workspace && terraform.workspace != "default"
  workspace_parts                = split("_", terraform.workspace)
  azure_region_code = lookup({
    eastus     = "eus"
    eastus2    = "eus2"
    westeurope = "weu"
  }, var.azure_region, substr(replace(var.azure_region, "-", ""), 0, 6))
  grafana_workspace_name        = coalesce(var.grafana_workspace_name, "amg-${var.environment}-${local.azure_region_code}-${var.customer_organization_slug}")
  aggregate_metrics_data_source = var.metrics_data_source_identifiers["aggregate_metrics"]

  common_tags = merge(var.tags, {
    environment                = var.environment
    region                     = var.azure_region
    customer_organization_slug = var.customer_organization_slug
    terraform_stage            = local.stage_name
  })
}
