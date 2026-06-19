# Network Private Data Plane Stage

Responsibility: VNet, subnets, and network security boundaries.

Backend key: `network_private_data_plane.tfstate`

Remote backend example: `backend.azurerm.tf.example`

## Topology

This stage owns shared network plumbing for the Azure Production MVP. It creates:

- One deterministic network resource group.
- One deployment virtual network.
- Stable subnets for Container Apps infrastructure, shared networking capacity, and reserved future capacity.
- One Network Security Group per subnet boundary.

This stage does not create PostgreSQL, Blob Storage, AI services, Container Apps, Grafana, Front Door, public DNS records, captured content stores, secrets, connection strings, or application configuration.

Private endpoint hardening is deferred to a later issue and is not part of the current deployable Terraform path. This stage does not create network subnets or DNS contracts for that deferred hardening.

## Subnets

| Key | Default prefix | Purpose |
| --- | --- | --- |
| `container_apps_infrastructure` | `10.40.0.0/23` | Infrastructure subnet consumed later by the `app_runtime` Container Apps managed environment. |
| `shared_networking` | `10.40.4.0/24` | Shared capacity for private validation runners, DNS forwarding, or approved appliances. |
| `reserved` | `10.40.8.0/21` | Reserved capacity for later approved topology slices. |

## Downstream Outputs

The stage outputs only non-secret values:

- Resource group ID and name.
- Virtual network ID, name, and address space.
- Subnet IDs, names, address prefixes, and purpose text by stable key.
- Network Security Group IDs and names by stable key.

It does not output secrets, connection strings, Terraform state, captured content, tenant-private data, command output, or tool results.

## Lifecycle And Deletion

The network resource group is disposable environment-scoped infrastructure. It can be destroyed only through the guarded Terraform destroy workflow for disposable stages. Subnets in this stage are shared plumbing for downstream stages, so operators must destroy dependent downstream stages first or Terraform will correctly fail deletion while resources still depend on the VNet or subnets.

Public DNS remains retained shared infrastructure in the `public_dns` stage and is not managed here.

## Provider And AVM Choices

This stage follows the repo order:

1. AVM through local wrapper modules for the resource group, virtual network and subnets, and Network Security Groups.
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
