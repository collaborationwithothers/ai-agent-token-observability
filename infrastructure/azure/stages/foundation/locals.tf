locals {
  stage_name                     = "foundation"
  expected_workspace_name        = "${var.environment}_${var.azure_region}_${var.customer_organization_slug}"
  configured_workspace_name      = coalesce(var.terraform_workspace_name, terraform.workspace)
  workspace_name_matches_context = local.configured_workspace_name == local.expected_workspace_name
  workspace_parts                = split("_", terraform.workspace)
  foundation_resource_group_name = coalesce(var.foundation_resource_group_name, "rg-to-${var.environment}-${var.azure_region}-${var.customer_organization_slug}-foundation")
  container_registry_name        = coalesce(var.container_registry_name, replace("to${var.environment}${var.azure_region}${var.customer_organization_slug}${var.resource_instance}acr", "-", ""))

  common_tags = merge(var.tags, {
    environment                = var.environment
    region                     = var.azure_region
    customer_organization_slug = var.customer_organization_slug
    terraform_stage            = local.stage_name
  })
}
