output "app_id" {
  description = "Application Insights App ID."
  value       = module.application_insights.app_id
}

output "name" {
  description = "Name of the Application Insights component."
  value       = module.application_insights.name
}

output "resource_id" {
  description = "Resource ID of the Application Insights component."
  value       = module.application_insights.resource_id
}
