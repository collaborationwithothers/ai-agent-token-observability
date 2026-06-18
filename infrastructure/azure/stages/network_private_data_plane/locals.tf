locals {
  stage_name                       = "network_private_data_plane"
  expected_workspace_name          = "${var.environment}_${var.azure_region}_${var.customer_organization_slug}"
  configured_workspace_name        = coalesce(var.terraform_workspace_name, terraform.workspace)
  workspace_name_matches_context   = terraform.workspace == local.expected_workspace_name && local.configured_workspace_name == terraform.workspace && terraform.workspace != "default"
  workspace_parts                  = split("_", terraform.workspace)
  name_prefix                      = "to-${var.environment}-${var.resource_instance}"
  network_resource_group_name      = coalesce(var.network_resource_group_name, "rg-to-${var.environment}-${var.azure_region}-${var.customer_organization_slug}-network")
  virtual_network_name             = coalesce(var.virtual_network_name, "vnet-${local.name_prefix}-${var.azure_region}-${var.customer_organization_slug}")
  postgresql_private_dns_zone_name = "to-${var.environment}-${var.azure_region}-${var.customer_organization_slug}.postgres.database.azure.com"

  common_tags = merge(var.tags, {
    environment                = var.environment
    region                     = var.azure_region
    customer_organization_slug = var.customer_organization_slug
    terraform_stage            = local.stage_name
  })

  nsg_names = {
    container_apps_infrastructure = "nsg-${local.name_prefix}-aca-infra"
    private_endpoints             = "nsg-${local.name_prefix}-pe"
    postgresql_delegated          = "nsg-${local.name_prefix}-postgres"
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

  postgresql_nsg_security_rules = merge(local.base_nsg_security_rules, {
    allow_postgresql_inbound = {
      name                       = "AllowPostgreSqlInbound"
      priority                   = 120
      direction                  = "Inbound"
      access                     = "Allow"
      protocol                   = "Tcp"
      source_port_range          = "*"
      destination_port_range     = "5432"
      source_address_prefix      = "VirtualNetwork"
      destination_address_prefix = "VirtualNetwork"
      description                = "Allow private PostgreSQL traffic from workloads in the virtual network."
    }
    allow_storage_outbound = {
      name                       = "AllowStorageOutbound"
      priority                   = 120
      direction                  = "Outbound"
      access                     = "Allow"
      protocol                   = "Tcp"
      source_port_range          = "*"
      destination_port_range     = "443"
      source_address_prefix      = "VirtualNetwork"
      destination_address_prefix = "Storage"
      description                = "Allow Azure Storage traffic required by PostgreSQL Flexible Server operations."
    }
    allow_entra_outbound = {
      name                       = "AllowEntraOutbound"
      priority                   = 130
      direction                  = "Outbound"
      access                     = "Allow"
      protocol                   = "Tcp"
      source_port_range          = "*"
      destination_port_range     = "443"
      source_address_prefix      = "VirtualNetwork"
      destination_address_prefix = "AzureActiveDirectory"
      description                = "Allow Microsoft Entra authentication traffic for PostgreSQL Flexible Server."
    }
  })

  nsg_security_rules = {
    container_apps_infrastructure = local.base_nsg_security_rules
    private_endpoints             = local.base_nsg_security_rules
    postgresql_delegated          = local.postgresql_nsg_security_rules
    reserved                      = local.base_nsg_security_rules
    shared_networking             = local.base_nsg_security_rules
  }

  subnet_definitions = {
    container_apps_infrastructure = {
      name                            = "snet-${local.name_prefix}-aca-infra"
      address_prefix                  = var.subnet_address_prefixes.container_apps_infrastructure
      private_endpoint_network_policy = "Enabled"
      nsg_key                         = "container_apps_infrastructure"
      purpose                         = "Container Apps workload-profile environment infrastructure subnet for downstream app_runtime."
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
    private_endpoints = {
      name                            = "snet-${local.name_prefix}-private-endpoints"
      address_prefix                  = var.subnet_address_prefixes.private_endpoints
      private_endpoint_network_policy = "Enabled"
      nsg_key                         = "private_endpoints"
      purpose                         = "Shared private endpoint subnet for downstream data, AI, storage, and origin private link resources."
      service_endpoints               = []
      delegations                     = []
    }
    postgresql_delegated = {
      name                            = "snet-${local.name_prefix}-postgres"
      address_prefix                  = var.subnet_address_prefixes.postgresql_delegated
      private_endpoint_network_policy = "Enabled"
      nsg_key                         = "postgresql_delegated"
      purpose                         = "Delegated subnet reserved for Azure Database for PostgreSQL Flexible Server private access."
      service_endpoints = [
        {
          service   = "Microsoft.Storage"
          locations = [var.azure_region]
        }
      ]
      delegations = [
        {
          name = "postgresql-flexible-server"
          service_delegation = {
            name = "Microsoft.DBforPostgreSQL/flexibleServers"
          }
        }
      ]
    }
    reserved = {
      name                            = "snet-${local.name_prefix}-reserved"
      address_prefix                  = var.subnet_address_prefixes.reserved
      private_endpoint_network_policy = "Enabled"
      nsg_key                         = "reserved"
      purpose                         = "Reserved network capacity for later approved production topology slices."
      service_endpoints               = []
      delegations                     = []
    }
    shared_networking = {
      name                            = "snet-${local.name_prefix}-shared"
      address_prefix                  = var.subnet_address_prefixes.shared_networking
      private_endpoint_network_policy = "Enabled"
      nsg_key                         = "shared_networking"
      purpose                         = "Shared networking capacity for private validation runners, DNS forwarding, or future approved appliances."
      service_endpoints               = []
      delegations                     = []
    }
  }

  virtual_network_subnets = {
    for key, subnet in local.subnet_definitions : key => {
      name                                          = subnet.name
      address_prefix                                = subnet.address_prefix
      network_security_group                        = { id = module.network_security_groups[subnet.nsg_key].id }
      private_endpoint_network_policies             = subnet.private_endpoint_network_policy
      private_link_service_network_policies_enabled = true
      default_outbound_access_enabled               = false
      service_endpoints_with_location               = subnet.service_endpoints
      delegations                                   = subnet.delegations
    }
  }

  default_private_dns_zones = {
    postgresql_private_access = {
      domain_name = local.postgresql_private_dns_zone_name
      purpose     = "PostgreSQL Flexible Server private access with virtual network integration."
    }
    postgresql_private_endpoint = {
      domain_name = "privatelink.postgres.database.azure.com"
      purpose     = "PostgreSQL Flexible Server private endpoint pattern if downstream data_platform chooses Private Link."
    }
    blob = {
      domain_name = "privatelink.blob.core.windows.net"
      purpose     = "Blob Storage private endpoints for policy-approved captured content."
    }
    queue = {
      domain_name = "privatelink.queue.core.windows.net"
      purpose     = "Queue Storage private endpoints if downstream jobs use queue-triggered work."
    }
    table = {
      domain_name = "privatelink.table.core.windows.net"
      purpose     = "Table Storage private endpoints if downstream operational storage needs table APIs."
    }
    azure_ai_services = {
      domain_name = "privatelink.cognitiveservices.azure.com"
      purpose     = "Azure AI Language and Content Safety private endpoints."
    }
    azure_openai = {
      domain_name = "privatelink.openai.azure.com"
      purpose     = "Azure OpenAI private endpoints."
    }
    azure_ai_foundry_services = {
      domain_name = "privatelink.services.ai.azure.com"
      purpose     = "Azure AI Foundry services private endpoint DNS when downstream AI services require it."
    }
    container_apps_environment = {
      domain_name = "privatelink.${var.azure_region}.azurecontainerapps.io"
      purpose     = "Container Apps managed environment private endpoints."
    }
    key_vault = {
      domain_name = "privatelink.vaultcore.azure.net"
      purpose     = "Foundation Key Vault private endpoint DNS when a later slice creates private endpoints."
    }
  }

  private_dns_zones = merge(local.default_private_dns_zones, var.additional_private_dns_zones)
}
