locals {
  stage_name                       = "foundation"
  expected_workspace_name          = "${var.environment}_${var.azure_region}_${var.customer_organization_slug}"
  configured_workspace_name        = coalesce(var.terraform_workspace_name, terraform.workspace)
  workspace_name_matches_context   = terraform.workspace == local.expected_workspace_name && local.configured_workspace_name == terraform.workspace && terraform.workspace != "default"
  workspace_parts                  = split("_", terraform.workspace)
  foundation_resource_group_name   = coalesce(var.foundation_resource_group_name, "rg-to-${var.environment}-${var.azure_region}-${var.customer_organization_slug}-foundation")
  container_registry_name          = coalesce(var.container_registry_name, replace("to${var.environment}${var.azure_region}${var.customer_organization_slug}${var.resource_instance}acr", "-", ""))
  key_vault_name                   = coalesce(var.key_vault_name, lower("to${var.environment}${substr(var.azure_region, 0, 4)}${substr(var.resource_instance, 0, 4)}${substr(md5(var.customer_organization_slug), 0, 6)}kv"))
  deployment_identity_name         = coalesce(var.deployment_identity_name, "id-to-${var.environment}-${var.azure_region}-${var.customer_organization_slug}-${var.resource_instance}-deployment")
  deployment_identity_configured   = var.create_deployment_identity || length(var.deployment_identity_references) > 0
  deployment_identity_role_enabled = var.assign_deployment_identity_key_vault_role && (var.create_deployment_identity || length(var.deployment_identity_references) > 0)

  created_deployment_identity_key_vault_role_assignments = var.create_deployment_identity && var.assign_deployment_identity_key_vault_role ? {
    deployment = {
      role_definition_id_or_name             = var.deployment_identity_key_vault_role_definition_name
      principal_id                           = module.deployment_identity["deployment"].principal_id
      description                            = "Allows the foundation deployment identity to manage Key Vault secrets without broader Key Vault administration."
      skip_service_principal_aad_check       = false
      condition                              = null
      condition_version                      = null
      delegated_managed_identity_resource_id = null
      principal_type                         = "ServicePrincipal"
    }
  } : {}

  referenced_deployment_identity_key_vault_role_assignments = local.deployment_identity_role_enabled ? {
    for key, identity in var.deployment_identity_references : "reference_${key}" => {
      role_definition_id_or_name             = var.deployment_identity_key_vault_role_definition_name
      principal_id                           = identity.principal_id
      description                            = "Allows the referenced deployment identity to manage Key Vault secrets without broader Key Vault administration."
      skip_service_principal_aad_check       = false
      condition                              = null
      condition_version                      = null
      delegated_managed_identity_resource_id = null
      principal_type                         = "ServicePrincipal"
    }
  } : {}

  explicit_key_vault_role_assignments = {
    for key, assignment in var.key_vault_role_assignments : key => {
      role_definition_id_or_name             = assignment.role_definition_id_or_name
      principal_id                           = assignment.principal_id
      description                            = assignment.description
      skip_service_principal_aad_check       = assignment.skip_service_principal_aad_check
      condition                              = assignment.condition
      condition_version                      = assignment.condition_version
      delegated_managed_identity_resource_id = assignment.delegated_managed_identity_resource_id
      principal_type                         = assignment.principal_type
    }
  }

  key_vault_role_assignments = merge(
    local.created_deployment_identity_key_vault_role_assignments,
    local.referenced_deployment_identity_key_vault_role_assignments,
    local.explicit_key_vault_role_assignments
  )

  common_tags = merge(var.tags, {
    environment                = var.environment
    region                     = var.azure_region
    customer_organization_slug = var.customer_organization_slug
    terraform_stage            = local.stage_name
  })
}
