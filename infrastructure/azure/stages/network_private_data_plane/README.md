# Network Private Data Plane Stage

Responsibility: VNet, subnets, private DNS zones, private endpoints, and network security boundaries.

Backend key: `network_private_data_plane.tfstate`

Remote backend example: `backend.azurerm.tf.example`

## Topology

This stage owns shared private network plumbing for the Azure Production MVP. It creates:

- One deterministic network resource group.
- One private data plane virtual network.
- Stable subnets for Container Apps infrastructure, shared private endpoints, PostgreSQL Flexible Server private access, shared networking capacity, and reserved future capacity.
- One Network Security Group per subnet boundary.
- Private DNS zones linked to the private data plane virtual network.

This stage does not create PostgreSQL, Blob Storage, AI services, Container Apps, Grafana, Front Door, private endpoints, public DNS records, captured content stores, secrets, connection strings, or application configuration.

## Subnets

| Key | Default prefix | Purpose |
| --- | --- | --- |
| `container_apps_infrastructure` | `10.40.0.0/23` | Infrastructure subnet consumed later by the `app_runtime` Container Apps managed environment. |
| `private_endpoints` | `10.40.2.0/24` | Shared private endpoint subnet for downstream data, storage, AI, and origin-private resources. |
| `postgresql_delegated` | `10.40.3.0/27` | Delegated subnet for Azure Database for PostgreSQL Flexible Server private access. |
| `shared_networking` | `10.40.4.0/24` | Shared capacity for private validation runners, DNS forwarding, or approved appliances. |
| `reserved` | `10.40.8.0/21` | Reserved capacity for later approved topology slices. |

The PostgreSQL subnet is delegated to `Microsoft.DBforPostgreSQL/flexibleServers`. Private endpoint network policies are enabled on the private endpoint subnet to preserve NSG enforcement for private endpoints.

## Private DNS Zones

The stage creates and links these default private DNS zones:

| Key | Zone name | Downstream use |
| --- | --- | --- |
| `postgresql_private_access` | `to-{environment}-{azure_region}-{customer}.postgres.database.azure.com` | PostgreSQL Flexible Server private access with VNet integration. |
| `postgresql_private_endpoint` | `privatelink.postgres.database.azure.com` | PostgreSQL Flexible Server private endpoint pattern if selected downstream. |
| `blob` | `privatelink.blob.core.windows.net` | Captured-content Blob Storage private endpoints. |
| `queue` | `privatelink.queue.core.windows.net` | Queue Storage private endpoints if downstream jobs use queues. |
| `table` | `privatelink.table.core.windows.net` | Table Storage private endpoints if downstream operational storage requires Table APIs. |
| `azure_ai_services` | `privatelink.cognitiveservices.azure.com` | Azure AI Language and Content Safety private endpoints. |
| `azure_openai` | `privatelink.openai.azure.com` | Azure OpenAI private endpoints. |
| `azure_ai_foundry_services` | `privatelink.services.ai.azure.com` | Azure AI Foundry services private endpoint DNS when required by the downstream AI implementation. |
| `container_apps_environment` | `privatelink.{azure_region}.azurecontainerapps.io` | Container Apps managed environment private endpoints. |
| `key_vault` | `privatelink.vaultcore.azure.net` | Foundation Key Vault private endpoint DNS if a later slice creates Key Vault private endpoints. |

Assumption: downstream AI services may use Azure AI Services, Azure OpenAI, or Foundry private endpoints depending on the final provider shape. This stage exposes a zone map so later stages can consume the exact zone IDs without recreating DNS zones.

## Downstream Outputs

The stage outputs only non-secret values:

- Resource group ID and name.
- Virtual network ID, name, and address space.
- Subnet IDs, names, address prefixes, and purpose text by stable key.
- Network Security Group IDs and names by stable key.
- Private DNS zone IDs, names, and purpose text by stable key.

It does not output private endpoint IP addresses, secrets, connection strings, Terraform state, captured content, tenant-private data, command output, or tool results.

## Lifecycle And Deletion

The network resource group is disposable environment-scoped infrastructure. It can be destroyed only through the guarded Terraform destroy workflow for disposable stages. DNS zones and subnets in this stage are shared plumbing for downstream stages, so operators must destroy dependent downstream stages first or Terraform will correctly fail deletion while resources still depend on the VNet, subnets, or private DNS links.

Public DNS remains retained shared infrastructure in the `public_dns` stage and is not managed here.

## Provider And AVM Choices

This stage follows the repo order:

1. AVM through local wrapper modules for the resource group, virtual network and subnets, Network Security Groups, and private DNS zones.
2. AzureRM only where a later issue proves no suitable AVM exists.
3. AzAPI only for provider gaps.

No provider exceptions are used in this stage.

Local validation:

```bash
terraform init -backend=false
terraform validate
terraform plan -input=false -lock=false \
  -var="environment=dv" \
  -var="azure_region=eastus2" \
  -var="customer_organization_slug=internal" \
  -var="terraform_workspace_name=dv_eastus2_internal" \
  -var="resource_instance=core" \
  -var='tags={environment="dv",region="eastus2",product="token-observability",owner="platform",data_classification="internal",managed_by="terraform"}'
```

The selected workspace must be `dv_eastus2_internal` for that plan example. Backend-free `terraform validate` can run from the default workspace, but Azure-changing plans are guarded by Terraform preconditions and must not use the default workspace.
