output "app_id" {
  description = "Application Insights App ID."
  # The upstream AVM module marks app_id sensitive, but this wrapper exposes it
  # only as a non-secret telemetry configuration reference.
  value = nonsensitive(module.application_insights.app_id)
}

output "name" {
  description = "Name of the Application Insights component."
  value       = module.application_insights.name
}

output "resource_id" {
  description = "Resource ID of the Application Insights component."
  value       = module.application_insights.resource_id
}
