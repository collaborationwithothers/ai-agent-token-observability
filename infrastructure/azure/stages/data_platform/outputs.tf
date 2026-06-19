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
    data = module.data_resource_group.id
  }
}

output "data_resource_group_name" {
  description = "Name of the resource group containing data platform resources."
  value       = module.data_resource_group.name
}

output "postgresql_server_resource_id" {
  description = "Resource ID of the Product Metadata Store PostgreSQL Flexible Server."
  value       = module.product_metadata_store.resource_id
}

output "postgresql_server_name" {
  description = "Name of the Product Metadata Store PostgreSQL Flexible Server."
  value       = module.product_metadata_store.name
}

output "postgresql_server_fqdn" {
  description = "PostgreSQL Flexible Server FQDN for public firewall allowlisted runtime configuration."
  value       = module.product_metadata_store.fqdn
}

output "postgresql_database_names" {
  description = "PostgreSQL database names by stable downstream contract key."
  value       = module.product_metadata_store.database_names
}

output "postgresql_restore_contract" {
  description = "Non-secret PostgreSQL restore-readiness settings for restore drill and operations issues."
  value = {
    server_resource_id                = module.product_metadata_store.resource_id
    database_names                    = module.product_metadata_store.database_names
    backup_retention_days             = var.postgresql_backup_retention_days
    geo_redundant_backup_enabled      = var.postgresql_geo_redundant_backup_enabled
    point_in_time_restore_supported   = true
    restore_target_must_be_new_server = true
  }
}

output "storage_account_resource_id" {
  description = "Resource ID of the product storage account."
  value       = module.product_storage.resource_id
}

output "storage_account_name" {
  description = "Name of the product storage account."
  value       = module.product_storage.name
}

output "storage_blob_fqdn" {
  description = "Blob service FQDN for public network allowlisted runtime configuration."
  value       = try(module.product_storage.fqdn.blob, null)
}

output "storage_container_names" {
  description = "Product storage container names by stable downstream contract key."
  value       = module.product_storage.container_names
}

output "captured_content_storage_contract" {
  description = "Non-secret storage contract for policy-approved redacted content blobs and review artifacts."
  value = {
    storage_account_resource_id       = module.product_storage.resource_id
    captured_content_container_name   = module.product_storage.container_names["captured_content"]
    content_review_container_name     = module.product_storage.container_names["content_review_artifacts"]
    captured_content_prefix_template  = "customer-organization-id={customerOrganizationId}/yyyy={yyyy}/mm={mm}/dd={dd}/session-id={sessionId}/content-reference-id={contentReferenceId}/"
    content_review_prefix_template    = "customer-organization-id={customerOrganizationId}/yyyy={yyyy}/mm={mm}/dd={dd}/content-reference-id={contentReferenceId}/"
    retention_days                    = var.captured_content_retention_days
    redaction_required_before_storage = true
    public_access                     = "Disabled"
  }
}

output "operational_storage_contract" {
  description = "Non-secret storage contract for restore drill and lifecycle validation artifacts."
  value = {
    storage_account_resource_id = module.product_storage.resource_id
    operational_container_name  = module.product_storage.container_names["operational_artifacts"]
    restore_drill_prefix        = "restore-drills/"
    lifecycle_validation_prefix = "lifecycle-validation/"
    retention_days              = var.operational_artifacts_retention_days
    public_access               = "Disabled"
  }
}

output "storage_lifecycle_contract" {
  description = "Non-secret lifecycle policy settings for operations validation."
  value = {
    captured_content_retention_days      = var.captured_content_retention_days
    operational_artifacts_retention_days = var.operational_artifacts_retention_days
    blob_delete_retention_days           = var.blob_delete_retention_days
    container_delete_retention_days      = var.blob_container_delete_retention_days
    point_in_time_restore_days           = var.blob_point_in_time_restore_days
  }
}
