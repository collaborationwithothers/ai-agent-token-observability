# Manual Terraform Remote State Foundation

## Purpose

This runbook defines the manual Azure Blob Storage remote state foundation required before Azure Production MVP Terraform stages run against Azure.

Remote state storage is a retained shared dependency. It is not created by repository Terraform stages, and it must not be destroyed by disposable environment deletion workflows.

## Source Documents

- [Terraform Production Infrastructure](../architecture/terraform-production-infrastructure.md)
- [Azure Terraform Skeleton](../../infrastructure/azure/README.md)
- [Infrastructure Deletion Workflow](./infrastructure-deletion.md)
- [Terraform azurerm backend](https://developer.hashicorp.com/terraform/language/backend/azurerm)
- [Store Terraform state in Azure Storage](https://learn.microsoft.com/azure/developer/terraform/store-state-in-azure-storage)
- [Prevent Shared Key authorization for an Azure Storage account](https://learn.microsoft.com/azure/storage/common/shared-key-authorization-prevent)

## Ownership

The platform operator creates and validates these resources before the first production-backed `terraform init`:

- Remote state resource group.
- Remote state storage account.
- Blob container named `tfstate`.
- RBAC assignments for the GitHub Actions workflow deployment identity.

The repository Terraform stages must not create, import, update, or destroy the remote state resource group, storage account, or container.

## Required Azure Shape

Create a dedicated remote state resource group and storage account for Terraform state only.

Required settings:

- Blob public access disabled.
- Blob container access level set to private.
- Blob versioning enabled where supported.
- Blob soft delete enabled with an operator-approved retention period.
- Storage account public network access restricted for production where feasible.
- Platform-managed encryption enabled.
- Shared Key authorization disabled after the Entra ID backend path is verified compatible.

Recommended production network posture:

- Prefer private endpoint or storage firewall access from approved deployment runners.
- If temporary public network access is needed for bootstrap, record it as an exception and remove it before production apply.
- Do not publish private endpoint IDs, private IP addresses, firewall source addresses, or runner network details in public issue comments or workflow summaries.

## Authentication Rules

Normal repository workflows must authenticate to the backend with Microsoft Entra ID and GitHub OIDC.

Required workflow posture:

- Use GitHub Actions `id-token: write` only in jobs that need Azure OIDC.
- Use the workflow deployment identity client ID, tenant ID, and subscription ID as non-secret configuration.
- Set `use_oidc = true` for the Terraform `azurerm` backend path.
- Set `use_azuread_auth = true` for the Terraform `azurerm` backend path.
- Keep backend configuration values outside committed secrets.

Forbidden for normal production workflows:

- Storage account keys.
- Access keys.
- SAS tokens.
- Backend secrets in GitHub workflow secrets.
- Long-lived Azure client secrets for backend access.

Break-glass exceptions must be documented outside public GitHub issue comments and must not change the normal workflow contract.

## RBAC

Assign the workflow deployment identity the minimum data-plane role required to read and write Terraform state:

```text
Storage Blob Data Contributor
```

Preferred scope:

```text
Remote state container scope
```

Acceptable scope when container-scoped assignment is not practical:

```text
Remote state storage account scope
```

The identity also needs any management-plane permissions required by the target stage, but those permissions are not part of this remote state runbook.

## Backend Values

Record these non-secret backend values for workflow initialization:

| Value | Description | Secret |
| --- | --- | --- |
| `subscription_id` | Azure subscription containing the remote state storage account | No |
| `tenant_id` | Microsoft Entra tenant used for workflow identity | No |
| `client_id` | Workflow deployment identity client ID | No |
| `resource_group_name` | Remote state resource group name | No |
| `storage_account_name` | Remote state storage account name | No |
| `container_name` | Blob container name, expected to be `tfstate` | No |
| `key` | Stage backend key, for example `foundation.tfstate` | No |
| `use_oidc` | Backend authentication mode, expected to be `true` | No |
| `use_azuread_auth` | Backend authorization mode, expected to be `true` | No |

Do not record access keys, SAS URLs, connection strings, client secrets, private endpoint details, or Terraform state content.

## Backend Key Pattern

Each stage uses one stable backend key and workspace isolation.

Pattern:

```text
{stageName}.tfstate
```

Examples:

```text
foundation.tfstate
network_private_data_plane.tfstate
observability_foundation.tfstate
data_platform.tfstate
ai_services.tfstate
app_runtime.tfstate
managed_grafana.tfstate
edge.tfstate
```

Workspace isolation remains mandatory:

```text
{environment}_{azureRegion}_{customerOrganizationSlug}
```

Example:

```text
pd_eastus2_internal
```

Do not use the default Terraform workspace for Azure-changing plans or applies.

## Operator Procedure

1. Create the remote state resource group.
2. Create the dedicated remote state storage account.
3. Disable blob public access on the storage account.
4. Create the private `tfstate` blob container.
5. Enable blob versioning where supported.
6. Enable blob soft delete with the approved retention period.
7. Restrict storage network access for production where feasible.
8. Assign `Storage Blob Data Contributor` to the workflow deployment identity at the container scope, or storage account scope if container scope is not practical.
9. Verify the backend can initialize with Entra ID and OIDC.
10. Disable Shared Key authorization after compatibility is verified.
11. Record only the safe evidence fields listed in this runbook.
12. Validate that Terraform can initialize the backend with Entra ID and OIDC before any production stage plan.

## Safe Evidence Record

Use this shape for internal release evidence. Keep operational evidence in approved internal systems, not public GitHub issue comments.

```text
date: 2026-06-17
operator: platform-operator
subscription: sub-production-platform
resource_group: rg-tokenobs-tfstate-pd-eus2
storage_account: sttokenobstfstatepd001
container: tfstate
backend_key_pattern: {stageName}.tfstate
environment: pd
region: eastus2
customer_organization_slug: internal
authentication: entra_oidc
rbac_scope: container
rbac_role: Storage Blob Data Contributor
public_blob_access: disabled
blob_versioning: enabled
blob_soft_delete: enabled
shared_key_authorization: disabled
public_network_access: restricted
validation_result: backend init succeeded for pd_eastus2_internal without default workspace
```

Evidence must not include:

- Credentials.
- Storage account keys.
- SAS URLs or SAS tokens.
- Connection strings.
- Client secrets.
- Private endpoint IDs or IP addresses.
- Tenant-private data.
- Raw Terraform state content.

## Validation Checklist

Before enabling Azure-backed Terraform stages, verify:

- The remote state resource group exists.
- The storage account exists and is dedicated to Terraform state.
- Blob public access is disabled.
- The `tfstate` container exists and is private.
- Blob versioning is enabled where supported.
- Blob soft delete is enabled.
- Shared Key authorization is disabled after Entra ID backend compatibility is verified.
- Production network access is restricted where feasible.
- The workflow deployment identity has `Storage Blob Data Contributor`.
- Normal workflows use Entra ID and OIDC for backend authentication.
- No backend access keys, SAS tokens, client secrets, or connection strings are stored in GitHub workflow secrets.
- Backend keys follow `{stageName}.tfstate`.
- The selected Terraform workspace follows `{environment}_{azureRegion}_{customerOrganizationSlug}`.
- The default workspace is not used for Azure-changing operations.

## Retention And Deletion

Remote state resources are retained shared resources.

Deletion workflows must not delete:

- Remote state resource group.
- Remote state storage account.
- Remote state blob container.
- Terraform state blobs.

After disposable environment deletion, keep state for audit unless a separate approved state-retention policy says otherwise.

Manual state repair is a break-glass operation and requires a separate runbook.

## Verified Platform Facts

- Terraform `azurerm` backend stores Terraform state in Azure Blob Storage and supports backend configuration values including storage account, container, key, subscription, tenant, client ID, and OIDC authentication: https://developer.hashicorp.com/terraform/language/backend/azurerm
- Terraform documentation recommends keeping credentials and sensitive data out of configuration and using environment variables for sensitive values: https://developer.hashicorp.com/terraform/language/backend/azurerm
- Microsoft documents Azure Storage as a Terraform remote state backend and notes that Terraform state can contain sensitive information: https://learn.microsoft.com/azure/developer/terraform/store-state-in-azure-storage
- Microsoft documents Azure Blob backend state locking and encryption at rest for Terraform state stored in Azure Storage: https://learn.microsoft.com/azure/developer/terraform/store-state-in-azure-storage
- Microsoft recommends Microsoft Entra ID over Shared Key authorization for Azure Storage and documents disabling Shared Key authorization after clients use Entra ID: https://learn.microsoft.com/azure/storage/common/shared-key-authorization-prevent
