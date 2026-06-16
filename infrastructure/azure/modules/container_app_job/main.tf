module "container_app_job" {
  source  = "Azure/avm-res-app-job/azurerm"
  version = "0.2.1"

  enable_telemetry                      = false
  name                                  = var.name
  resource_group_name                   = var.resource_group_name
  location                              = var.location
  container_app_environment_resource_id = var.container_app_environment_resource_id
  workload_profile_name                 = var.workload_profile_name
  tags                                  = var.tags

  managed_identities         = var.managed_identities
  registries                 = var.registries
  replica_retry_limit        = var.replica_retry_limit
  replica_timeout_in_seconds = var.replica_timeout_in_seconds
  secrets                    = var.key_vault_secrets
  template                   = var.template
  trigger_config             = var.trigger_config
}
