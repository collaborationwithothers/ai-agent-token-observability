resource "azurerm_dashboard_grafana" "this" {
  name                          = var.name
  resource_group_name           = var.resource_group_name
  location                      = var.location
  grafana_major_version         = var.grafana_major_version
  api_key_enabled               = false
  public_network_access_enabled = var.public_network_access_enabled
  sku                           = "Standard"
  sku_size                      = var.sku_size
  tags                          = var.tags
  zone_redundancy_enabled       = var.zone_redundancy_enabled

  identity {
    type = "SystemAssigned"
  }

  azure_monitor_workspace_integrations {
    resource_id = var.aggregate_metrics_data_source.resource_id
  }
}

resource "azurerm_role_assignment" "aggregate_metrics_data_reader" {
  scope                            = var.aggregate_metrics_data_source.resource_id
  role_definition_name             = "Monitoring Data Reader"
  principal_id                     = azurerm_dashboard_grafana.this.identity[0].principal_id
  principal_type                   = "ServicePrincipal"
  skip_service_principal_aad_check = true
}
