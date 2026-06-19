output "stage_name" {
  description = "Terraform stage name."
  value       = local.stage_name
}

output "expected_workspace_name" {
  description = "Expected workspace name for workflow guardrails."
  value       = local.expected_workspace_name
}

output "resource_group_ids" {
  description = "Resource group IDs created by this stage."
  value = {
    ai = module.ai_resource_group.id
  }
}

output "ai_resource_group_name" {
  description = "Name of the resource group containing AI service resources."
  value       = module.ai_resource_group.name
}

output "ai_services_account_resource_id" {
  description = "Resource ID of the Azure AI Services account."
  value       = module.ai_services_account.resource_id
}

output "ai_services_account_name" {
  description = "Name of the Azure AI Services account."
  value       = module.ai_services_account.name
}

output "ai_services_account_endpoint" {
  description = "Endpoint for Azure AI Language, Azure AI Content Safety, and model deployment consumers."
  value       = module.ai_services_account.endpoint
}

output "ai_services_managed_identity_principal_id" {
  description = "Principal ID of the system-assigned managed identity on the Azure AI Services account."
  value       = module.ai_services_account.system_assigned_mi_principal_id
}

output "ai_services_private_endpoint_ids" {
  description = "Private endpoint resource IDs by key when private access is supplied."
  value       = module.ai_services_account.private_endpoint_ids
}

output "ai_services_rai_policy_id" {
  description = "RAI policy ID or IDs created for model deployments."
  value       = module.ai_services_account.rai_policy_id
}

output "recommendation_model_deployment_aliases" {
  description = "Approved recommendation model deployment aliases configured in this stage."
  value       = keys(var.model_deployments)
}

output "recommendation_model_deployment_resource_ids" {
  description = "Recommendation model deployment resource IDs by deployment alias."
  value       = module.ai_services_account.deployment_resource_ids
}

output "recommendation_model_deployment_contracts" {
  description = "Non-secret deployment contracts for app runtime and recommendation jobs."
  value       = local.model_deployment_contracts
}

output "language_pii_detection_contract" {
  description = "Non-secret Azure AI Language PII detection configuration reference for redaction consumers."
  value = {
    account_resource_id        = module.ai_services_account.resource_id
    endpoint                   = module.ai_services_account.endpoint
    required_before_storage    = true
    stable_categories_required = true
    preview_categories_enabled = false
    local_auth_enabled         = false
    authentication             = "managed_identity"
  }
}

output "content_safety_contract" {
  description = "Non-secret Azure AI Content Safety configuration reference for classification and validation consumers."
  value = {
    account_resource_id           = module.ai_services_account.resource_id
    endpoint                      = module.ai_services_account.endpoint
    prompt_shields_required       = true
    protected_material_checks     = true
    groundedness_checks_available = true
    redaction_engine              = false
    local_auth_enabled            = false
    authentication                = "managed_identity"
  }
}

output "ai_services_configuration_contract" {
  description = "Non-secret AI service configuration contract for downstream app runtime and jobs."
  value = {
    account_resource_id                     = module.ai_services_account.resource_id
    account_name                            = module.ai_services_account.name
    endpoint                                = module.ai_services_account.endpoint
    managed_identity_principal_id           = module.ai_services_account.system_assigned_mi_principal_id
    recommendation_model_deployment_aliases = keys(var.model_deployments)
    public_network_access_enabled           = var.ai_services_public_network_access_enabled
    private_endpoint_ids                    = module.ai_services_account.private_endpoint_ids
    diagnostics_workspace_resource_id       = local.diagnostic_workspace_resource_id
  }
}
