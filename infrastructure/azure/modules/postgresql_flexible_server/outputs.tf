output "database_names" {
  description = "PostgreSQL database names by key."
  value       = { for key, database in var.databases : key => database.name }
}

output "database_resource_ids" {
  description = "PostgreSQL database resource IDs by key."
  value       = { for key, database in module.postgresql_flexible_server.database_resource_ids : key => database.resource_id }
}

output "fqdn" {
  description = "Fully qualified domain name of the PostgreSQL Flexible Server."
  value       = module.postgresql_flexible_server.fqdn
}

output "name" {
  description = "Name of the PostgreSQL Flexible Server."
  value       = module.postgresql_flexible_server.name
}

output "resource_id" {
  description = "Resource ID of the PostgreSQL Flexible Server."
  value       = module.postgresql_flexible_server.resource_id
}
