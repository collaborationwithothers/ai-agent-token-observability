# Azure Terraform Skeleton

This directory contains the Terraform stage skeleton for the Azure Production MVP.

No Azure resources are created by this skeleton issue. Resource implementation issues must add resources behind local wrapper modules and follow this order:

1. Azure Verified Modules (AVM) through a local wrapper module.
2. AzureRM resources when no suitable AVM exists.
3. AzAPI only when AzureRM cannot model the required feature.

The first skeleton intentionally contains only stage contracts, backend declarations, providers, variables, locals, and outputs so validation can run without Azure deployment.

## Stage Order

1. `foundation`
2. `network_private_data_plane`
3. `observability_foundation`
4. `data_platform`
5. `ai_services`
6. `app_runtime`
7. `managed_grafana`
8. `edge`

## Workspace Contract

Workspaces use:

```text
{environment}_{azureRegion}_{customerOrganizationSlug}
```

Examples:

```text
dv_eastus_internal
qa_westeurope_internal
pp_eastus2_internal
pd_eastus2_internal
```

The manually created Azure Blob remote state foundation supplies backend values at init time. Do not create remote state storage from these stages.

Each stage exposes an optional `terraform_workspace_name` variable for automation to validate the selected workspace against the environment, region, and customer organization slug inputs. Local skeleton validation can omit it, which keeps `terraform validate` usable from the default workspace without Azure access.

## Local Validation

Run from `infrastructure/azure`:

```bash
terraform fmt -check -recursive
```

Run from each stage directory:

```bash
terraform init -backend=false
terraform validate
```
