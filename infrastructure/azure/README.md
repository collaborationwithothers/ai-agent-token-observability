# Azure Terraform Skeleton

This directory contains the Terraform stages for the Azure Production MVP.

Resource implementation issues must add resources behind local wrapper modules and follow this order:

1. Azure Verified Modules (AVM) through a local wrapper module.
2. AzureRM resources when no suitable AVM exists.
3. AzAPI only when AzureRM cannot model the required feature.

Stages keep inactive backend examples so validation can run without Azure deployment or remote state access.

## Stage Order

1. `foundation`
2. `public_dns`
3. `network_private_data_plane`
4. `observability_foundation`
5. `data_platform`
6. `ai_services`
7. `app_runtime`
8. `managed_grafana`
9. `edge`

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

The manually created Azure Blob remote state foundation supplies backend values at production init time. Do not create remote state storage from these stages.

Each stage exposes an optional `terraform_workspace_name` variable for automation to validate the selected workspace against the environment, region, and customer organization slug inputs. Local skeleton validation can omit it, which keeps `terraform validate` usable from the default workspace without Azure access.

Each stage includes `backend.azurerm.tf.example` to show the intended Azure Blob remote state backend shape. The example is not active Terraform code so the skeleton can be checked with backend-free `terraform plan`.

## Local Validation

Run from `infrastructure/azure`:

```bash
terraform fmt -check -recursive
```

Run from each disposable environment stage directory:

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

The retained `public_dns` stage is intentionally pinned to the single owner workspace `pd_eastus2_internal` so the delegated shared product DNS zone has one Terraform workspace owner. Use the stage-specific example in `stages/public_dns/README.md` for that stage.
