locals {
  stage_name                         = "data_platform"
  expected_workspace_name            = "${var.environment}_${var.azure_region}_${var.customer_organization_slug}"
  configured_workspace_name          = coalesce(var.terraform_workspace_name, terraform.workspace)
  workspace_name_matches_context     = terraform.workspace == local.expected_workspace_name && local.configured_workspace_name == terraform.workspace && terraform.workspace != "default"
  workspace_parts                    = split("_", terraform.workspace)
  name_prefix                        = "to-${var.environment}-${var.resource_instance}"
  data_resource_group_name           = coalesce(var.data_resource_group_name, "rg-to-${var.environment}-${var.azure_region}-${var.customer_organization_slug}-data")
  postgresql_server_name             = coalesce(var.postgresql_server_name, "psql-${local.name_prefix}-${var.azure_region}-${var.customer_organization_slug}")
  region_code                        = lookup(local.storage_region_codes, var.azure_region, substr(replace(var.azure_region, "-", ""), 0, 6))
  storage_account_name               = coalesce(var.storage_account_name, substr("stto${var.environment}${local.region_code}${replace(var.resource_instance, "-", "")}${replace(var.customer_organization_slug, "-", "")}", 0, 24))
  postgresql_delegated_subnet_id     = try(var.network_subnet_ids["postgresql_delegated"], null)
  storage_private_endpoint_subnet_id = try(var.network_subnet_ids["private_endpoints"], null)
  postgresql_private_dns_zone_id     = try(var.private_dns_zone_ids["postgresql_private_access"], null)
  blob_private_dns_zone_id           = try(var.private_dns_zone_ids["blob"], null)
  diagnostic_workspace_resource_id   = try(var.diagnostic_destinations["data_platform"].log_analytics_workspace_resource_id, null)
  diagnostic_destination_type        = try(var.diagnostic_destinations["data_platform"].destination_type, "Dedicated")

  storage_region_codes = {
    eastus     = "eus"
    eastus2    = "eus2"
    westeurope = "weu"
  }

  common_tags = merge(var.tags, {
    environment                = var.environment
    region                     = var.azure_region
    customer_organization_slug = var.customer_organization_slug
    terraform_stage            = local.stage_name
  })

  runtime_storage_role_assignments = {
    for key, principal_id in var.runtime_managed_identity_principal_ids : key => {
      role_definition_id_or_name = "Storage Blob Data Contributor"
      principal_id               = principal_id
      principal_type             = "ServicePrincipal"
      description                = "Data platform Blob access for runtime managed identity ${key}."
    }
  }

  storage_containers = {
    captured_content = {
      name          = var.captured_content_container_name
      public_access = "None"
      metadata = {
        data_classification = "captured-content-redacted"
        retention_class     = "short"
        product_boundary    = "content-capture"
      }
      role_assignments = local.runtime_storage_role_assignments
    }
    content_review_artifacts = {
      name          = var.content_review_artifacts_container_name
      public_access = "None"
      metadata = {
        data_classification = "review-artifacts-redacted"
        retention_class     = "short"
        product_boundary    = "content-review"
      }
      role_assignments = local.runtime_storage_role_assignments
    }
    operational_artifacts = {
      name          = var.operational_artifacts_container_name
      public_access = "None"
      metadata = {
        data_classification = "operational-metadata"
        retention_class     = "operational"
        product_boundary    = "restore-lifecycle-validation"
      }
      role_assignments = local.runtime_storage_role_assignments
    }
  }

  storage_private_endpoints = var.enable_private_endpoints ? {
    blob = {
      name                          = "pe-${local.name_prefix}-blob"
      subnet_resource_id            = local.storage_private_endpoint_subnet_id
      subresource_name              = "blob"
      private_dns_zone_group_name   = "default"
      private_dns_zone_resource_ids = toset([local.blob_private_dns_zone_id])
      tags                          = local.common_tags
    }
  } : {}

  postgresql_diagnostic_settings = local.diagnostic_workspace_resource_id == null ? {} : {
    log_analytics = {
      name                           = "diag-${local.name_prefix}-postgresql"
      log_groups                     = toset(["allLogs"])
      metric_categories              = toset(["AllMetrics"])
      log_analytics_destination_type = local.diagnostic_destination_type
      workspace_resource_id          = local.diagnostic_workspace_resource_id
    }
  }

  storage_account_diagnostic_settings = local.diagnostic_workspace_resource_id == null ? {} : {
    log_analytics = {
      name                           = "diag-${local.name_prefix}-storage"
      metrics                        = toset([{ category = "AllMetrics", enabled = true }])
      log_analytics_destination_type = local.diagnostic_destination_type
      workspace_resource_id          = local.diagnostic_workspace_resource_id
    }
  }

  storage_blob_diagnostic_settings = local.diagnostic_workspace_resource_id == null ? {} : {
    log_analytics = {
      name                           = "diag-${local.name_prefix}-blob"
      logs                           = toset([{ category_group = "allLogs", enabled = true }])
      metrics                        = toset([{ category = "AllMetrics", enabled = true }])
      log_analytics_destination_type = local.diagnostic_destination_type
      workspace_resource_id          = local.diagnostic_workspace_resource_id
    }
  }

  storage_blob_properties = {
    versioning_enabled = true
    change_feed = {
      enabled           = true
      retention_in_days = var.captured_content_retention_days
    }
    container_delete_retention_policy = {
      enabled = true
      days    = var.blob_container_delete_retention_days
    }
    delete_retention_policy = {
      enabled = true
      days    = var.blob_delete_retention_days
    }
    restore_policy = {
      enabled = true
      days    = var.blob_point_in_time_restore_days
    }
    last_access_time_tracking_policy = {
      enable = true
      name   = "AccessTimeTracking"
    }
  }

  storage_management_policy_rules = {
    captured_content_retention = {
      enabled = true
      name    = "captured-content-retention"
      actions = {
        base_blob = {
          delete_after_days_since_creation_greater_than = var.captured_content_retention_days
        }
      }
      filters = {
        blob_types = toset(["blockBlob"])
        prefix_match = toset([
          "${var.captured_content_container_name}/",
          "${var.content_review_artifacts_container_name}/"
        ])
      }
    }
    operational_artifacts_retention = {
      enabled = true
      name    = "operational-artifacts-retention"
      actions = {
        base_blob = {
          delete_after_days_since_creation_greater_than = var.operational_artifacts_retention_days
        }
      }
      filters = {
        blob_types   = toset(["blockBlob"])
        prefix_match = toset(["${var.operational_artifacts_container_name}/lifecycle-validation/"])
      }
    }
  }
}
