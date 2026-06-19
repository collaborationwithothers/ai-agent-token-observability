output "deployment_resource_ids" {
  description = "Cognitive deployment resource IDs by deployment alias."
  value = {
    for key, deployment in module.cognitive_services_account.resource_cognitive_deployment :
    key => try(deployment.id, null)
  }
}

output "endpoint" {
  description = "Endpoint used to connect to the Cognitive Services account."
  value       = module.cognitive_services_account.endpoint
}

output "name" {
  description = "Name of the Cognitive Services account."
  value       = module.cognitive_services_account.name
}

output "private_endpoint_ids" {
  description = "Private endpoint resource IDs by key."
  value = {
    for key, private_endpoint in module.cognitive_services_account.private_endpoints :
    key => try(private_endpoint.id, null)
  }
}

output "rai_policy_id" {
  description = "RAI policy ID or IDs created by the Cognitive Services account module."
  value       = module.cognitive_services_account.rai_policy_id
}

output "resource_id" {
  description = "Resource ID of the Cognitive Services account."
  value       = module.cognitive_services_account.resource_id
}

output "system_assigned_mi_principal_id" {
  description = "Principal ID of the system-assigned managed identity on the Cognitive Services account."
  value       = module.cognitive_services_account.system_assigned_mi_principal_id
}
