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
    public_dns = azurerm_resource_group.public_dns.id
  }
}

output "product_dns_zone" {
  description = "Delegated Azure DNS zone object for the edge stage azure_dns_zone input."
  value = {
    id                  = azurerm_dns_zone.product.id
    name                = azurerm_dns_zone.product.name
    resource_group_name = azurerm_resource_group.public_dns.name
    manage_records      = true
  }
}

output "product_dns_zone_name_servers" {
  description = "Azure DNS authoritative name servers to delegate from Cloudflare for tokenobs.consultwithcloud.com."
  value       = azurerm_dns_zone.product.name_servers
}

output "cloudflare_delegation_ns_records" {
  description = "NS records that must exist in the Cloudflare apex zone to delegate only tokenobs.consultwithcloud.com to Azure DNS."
  value = [
    for name_server in azurerm_dns_zone.product.name_servers : {
      zone  = "consultwithcloud.com"
      name  = "tokenobs"
      type  = "NS"
      value = name_server
    }
  ]
}

output "first_release_product_hostnames" {
  description = "Public product hostnames that must resolve inside tokenobs.consultwithcloud.com."
  value       = var.public_ingress_hostnames
}
