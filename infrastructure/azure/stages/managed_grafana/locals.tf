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
  grafana_workspace_name         = coalesce(var.grafana_workspace_name, "amg-${var.environment}-${local.azure_region_code}-${var.customer_organization_slug}")
  aggregate_metrics_data_source  = var.metrics_data_source_identifiers["aggregate_metrics"]
  production_environment_codes   = ["pp", "pd"]
  is_production_environment      = contains(local.production_environment_codes, var.environment)
  grafana_admin_group_object_id  = var.grafana_admin_group_object_id == null ? null : trimspace(var.grafana_admin_group_object_id)
  grafana_editor_group_object_id = var.grafana_editor_group_object_id == null ? null : trimspace(var.grafana_editor_group_object_id)
  grafana_viewer_group_object_id = var.grafana_viewer_group_object_id == null ? null : trimspace(var.grafana_viewer_group_object_id)
  grafana_editor_group_is_set    = local.grafana_editor_group_object_id != null && local.grafana_editor_group_object_id != ""
  production_editor_role_blocked = local.is_production_environment && local.grafana_editor_group_is_set && !var.allow_production_grafana_editors
  grafana_rbac_candidate_groups = {
    admin = {
      principal_id         = local.grafana_admin_group_object_id
      role_definition_name = "Grafana Admin"
      description          = "Environment-scoped Azure Managed Grafana admin group."
    }
    editor = {
      principal_id         = local.grafana_editor_group_object_id
      role_definition_name = "Grafana Editor"
      description          = "Environment-scoped Azure Managed Grafana editor group."
    }
    viewer = {
      principal_id         = local.grafana_viewer_group_object_id
      role_definition_name = "Grafana Viewer"
      description          = "Environment-scoped Azure Managed Grafana viewer group."
    }
  }
  grafana_rbac_groups = {
    for key, group in local.grafana_rbac_candidate_groups : key => group
    if group.principal_id != null && group.principal_id != ""
  }

  common_tags = merge(var.tags, {
    environment                = var.environment
    region                     = var.azure_region
    customer_organization_slug = var.customer_organization_slug
    terraform_stage            = local.stage_name
  })
}
