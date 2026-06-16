locals {
  stage_name                     = "edge"
  expected_workspace_name        = "${var.environment}_${var.azure_region}_${var.customer_organization_slug}"
  configured_workspace_name      = coalesce(var.terraform_workspace_name, terraform.workspace)
  workspace_name_matches_context = terraform.workspace == local.expected_workspace_name && local.configured_workspace_name == terraform.workspace && terraform.workspace != "default"
  workspace_parts                = split("_", terraform.workspace)
  name_prefix                    = "to-${var.environment}-${var.resource_instance}"

  common_tags = merge(var.tags, {
    environment                = var.environment
    region                     = var.azure_region
    customer_organization_slug = var.customer_organization_slug
    terraform_stage            = local.stage_name
  })

  edge_resource_group_name = coalesce(var.edge_resource_group_name, "rg-to-${var.environment}-${var.azure_region}-${var.customer_organization_slug}-edge")
  waf_policy_mode          = coalesce(var.waf_policy_mode, contains(["pp", "pd"], var.environment) ? "Prevention" : "Detection")
}
