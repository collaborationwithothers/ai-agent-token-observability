module "cognitive_services_account" {
  source  = "Azure/avm-res-cognitiveservices-account/azurerm"
  version = "0.11.0"

  enable_telemetry = false

  name      = var.name
  parent_id = var.parent_id
  location  = var.location
  tags      = var.tags

  allow_project_management                = var.allow_project_management
  cognitive_deployments                   = var.cognitive_deployments
  custom_subdomain_name                   = var.custom_subdomain_name
  deployment_serialization_enabled        = true
  diagnostic_settings                     = var.diagnostic_settings
  kind                                    = var.kind
  local_auth_enabled                      = false
  managed_identities                      = var.managed_identities
  network_acls                            = var.network_acls
  network_injections                      = var.network_injections
  outbound_network_access_restricted      = var.outbound_network_access_restricted
  private_endpoints                       = var.private_endpoints
  private_endpoints_manage_dns_zone_group = var.private_endpoints_manage_dns_zone_group
  public_network_access_enabled           = var.public_network_access_enabled
  rai_policies                            = var.rai_policies
  role_assignments                        = var.role_assignments
  sku_name                                = var.sku_name
}
