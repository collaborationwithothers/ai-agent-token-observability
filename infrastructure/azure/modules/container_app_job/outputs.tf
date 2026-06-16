output "managed_identities" {
  description = "Managed identity details exported by the Container Apps Job AVM module."
  value       = module.container_app_job.managed_identities
}

output "name" {
  description = "Container Apps Job name."
  value       = module.container_app_job.container_app_job_name
}

output "resource_id" {
  description = "Container Apps Job resource ID."
  value       = module.container_app_job.resource_id
}
