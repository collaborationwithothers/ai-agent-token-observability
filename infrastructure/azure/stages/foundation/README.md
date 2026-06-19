# Foundation Stage

Responsibility: Resource groups, shared tags, deployment identities, Key Vault, role assignment foundations, and shared container image infrastructure.

Backend key: `foundation.tfstate`

Remote backend example: `backend.azurerm.tf.example`

## Ownership

This retained shared stage owns:

- The foundation resource group.
- The shared Azure Container Registry used by guarded image publish workflows.
- The shared foundation Key Vault.
- The optional foundation deployment user-assigned managed identity.
- Key Vault scoped RBAC assignments for created or referenced deployment identities and any explicitly supplied Key Vault role assignment foundations.

It does not create remote state storage, networks, deferred network-hardening resources, data platform resources, AI services, app runtime resources, Managed Grafana resources, edge resources, public DNS, secrets, keys, or certificates.

## Downstream Outputs

The stage exposes only non-secret downstream values:

- `resource_group_ids.foundation`
- `container_registry_id`
- `container_registry_name`
- `container_registry_login_server`
- `container_registry_resource_group_id`
- `key_vault_id`
- `key_vault_name`
- `key_vault_resource_group_name`
- `key_vault_uri`
- `deployment_identities`

The deployment identity output contains resource IDs and Microsoft Entra IDs only. It does not expose credentials, tokens, secret values, or Key Vault secret identifiers.

## Retained Behavior

The foundation stage remains outside the disposable destroy stage order so shared image infrastructure and foundation identities survive environment-stage cleanup.

Existing ACR behavior is retained:

- Admin access remains disabled.
- SKU is still controlled by `container_registry_sku`.
- The image publish workflow outputs remain available through the existing ACR output names.

Workspace validation remains tied to:

```text
{environment}_{azureRegion}_{customerOrganizationSlug}
```

Azure-changing operations must run from the selected Terraform workspace matching that value. The default workspace is rejected by resource preconditions.

## AVM And Provider Exceptions

Key Vault uses the local `../../modules/key_vault` wrapper around `Azure/avm-res-keyvault-vault/azurerm` pinned to `0.10.2`.

Managed identity uses the local `../../modules/managed_identity` wrapper around `Azure/avm-res-managedidentity-userassignedidentity/azurerm` pinned to `0.5.0`.

The foundation resource group and ACR remain direct AzureRM resources in this stage to preserve existing state addresses and ACR image publish behavior. No AzAPI resource is used by this stage.

Local validation:

```bash
terraform init -backend=false
terraform validate
terraform workspace select dv_eastus2_internal || terraform workspace new dv_eastus2_internal
terraform plan -input=false -lock=false \
  -var="environment=dv" \
  -var="azure_region=eastus2" \
  -var="customer_organization_slug=internal" \
  -var="terraform_workspace_name=dv_eastus2_internal" \
  -var="resource_instance=core" \
  -var='tags={environment="dv",region="eastus2",product="token-observability",owner="platform",data_classification="internal",managed_by="terraform"}'
```

Run the repo guardrail check from the repository root:

```bash
scripts/validate-terraform-foundation-acr.sh
```
