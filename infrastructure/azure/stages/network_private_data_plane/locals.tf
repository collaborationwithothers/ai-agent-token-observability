locals {
  stage_name                     = "network_private_data_plane"
  expected_workspace_name        = "${var.environment}_${var.azure_region}_${var.customer_organization_slug}"
  configured_workspace_name      = coalesce(var.terraform_workspace_name, terraform.workspace)
  workspace_name_matches_context = terraform.workspace == local.expected_workspace_name && local.configured_workspace_name == terraform.workspace && terraform.workspace != "default"
  workspace_parts                = split("_", terraform.workspace)
  name_prefix                    = "to-${var.environment}-${var.resource_instance}"
  network_resource_group_name    = coalesce(var.network_resource_group_name, "rg-to-${var.environment}-${var.azure_region}-${var.customer_organization_slug}-network")
  virtual_network_name           = coalesce(var.virtual_network_name, "vnet-${local.name_prefix}-${var.azure_region}-${var.customer_organization_slug}")

  common_tags = merge(var.tags, {
    environment                = var.environment
    region                     = var.azure_region
    customer_organization_slug = var.customer_organization_slug
    terraform_stage            = local.stage_name
  })

  nsg_names = {
    container_apps_infrastructure = "nsg-${local.name_prefix}-aca-infra"
    reserved                      = "nsg-${local.name_prefix}-reserved"
    shared_networking             = "nsg-${local.name_prefix}-shared"
  }

  base_nsg_security_rules = {
    allow_vnet_inbound = {
      name                       = "AllowVnetInbound"
      priority                   = 100
      direction                  = "Inbound"
      access                     = "Allow"
      protocol                   = "*"
      source_port_range          = "*"
      destination_port_range     = "*"
      source_address_prefix      = "VirtualNetwork"
      destination_address_prefix = "VirtualNetwork"
      description                = "Allow private traffic from the deployment virtual network."
    }
    allow_azure_load_balancer_inbound = {
      name                       = "AllowAzureLoadBalancerInbound"
      priority                   = 110
      direction                  = "Inbound"
      access                     = "Allow"
      protocol                   = "*"
      source_port_range          = "*"
      destination_port_range     = "*"
      source_address_prefix      = "AzureLoadBalancer"
      destination_address_prefix = "*"
      description                = "Allow Azure platform load balancer health traffic."
    }
    deny_internet_inbound = {
      name                       = "DenyInternetInbound"
      priority                   = 4096
      direction                  = "Inbound"
      access                     = "Deny"
      protocol                   = "*"
      source_port_range          = "*"
      destination_port_range     = "*"
      source_address_prefix      = "Internet"
      destination_address_prefix = "*"
      description                = "Deny inbound traffic sourced from the public internet."
    }
    allow_vnet_outbound = {
      name                       = "AllowVnetOutbound"
      priority                   = 100
      direction                  = "Outbound"
      access                     = "Allow"
      protocol                   = "*"
      source_port_range          = "*"
      destination_port_range     = "*"
      source_address_prefix      = "VirtualNetwork"
      destination_address_prefix = "VirtualNetwork"
      description                = "Allow private outbound traffic inside the deployment virtual network."
    }
  }

  nsg_security_rules = {
    container_apps_infrastructure = local.base_nsg_security_rules
    reserved                      = local.base_nsg_security_rules
    shared_networking             = local.base_nsg_security_rules
  }

  subnet_definitions = {
    container_apps_infrastructure = {
      name           = "snet-${local.name_prefix}-aca-infra"
      address_prefix = var.subnet_address_prefixes.container_apps_infrastructure
      nsg_key        = "container_apps_infrastructure"
      purpose        = "Container Apps workload-profile environment infrastructure subnet for downstream app_runtime."
      service_endpoints = [
        {
          service   = "Microsoft.KeyVault"
          locations = ["*"]
        },
        {
          service   = "Microsoft.Storage"
          locations = [var.azure_region]
        }
      ]
      delegations = [
        {
          name = "container-apps-environment"
          service_delegation = {
            name = "Microsoft.App/environments"
          }
        }
      ]
    }
    reserved = {
      name              = "snet-${local.name_prefix}-reserved"
      address_prefix    = var.subnet_address_prefixes.reserved
      nsg_key           = "reserved"
      purpose           = "Reserved network capacity for later approved production topology slices."
      service_endpoints = []
      delegations       = []
    }
    shared_networking = {
      name              = "snet-${local.name_prefix}-shared"
      address_prefix    = var.subnet_address_prefixes.shared_networking
      nsg_key           = "shared_networking"
      purpose           = "Shared networking capacity for private validation runners, DNS forwarding, or future approved appliances."
      service_endpoints = []
      delegations       = []
    }
  }

  virtual_network_subnets = {
    for key, subnet in local.subnet_definitions : key => {
      name                                          = subnet.name
      address_prefix                                = subnet.address_prefix
      network_security_group                        = { id = module.network_security_groups[subnet.nsg_key].id }
      private_link_service_network_policies_enabled = true
      default_outbound_access_enabled               = false
      service_endpoints_with_location               = subnet.service_endpoints
      delegations                                   = subnet.delegations
    }
  }
}
