# App Runtime Stage

Responsibility: Container Apps environment, Product API, Product Ingestion Endpoint, Product Dashboard, Container Apps Jobs, managed identities, and app configuration.

This stage deploys the long-running runtime services:

- Product Dashboard.
- Product API.
- Product Ingestion Endpoint.

It also models the shared Product Jobs image as distinct Azure Container Apps Jobs through a local wrapper around the Azure Verified Module `Azure/avm-res-app-job/azurerm`.

The shared jobs image default is `ghcr.io/collaborationwithothers/ai-agent-token-observability/product-jobs:latest`. Each job uses the same image with explicit `dotnet TokenObservability.Jobs.dll <command>` arguments:

- `normalize-telemetry`.
- `detect-hotspots`.
- `generate-recommendations`.
- `redact-content`.
- `refresh-pricing`.
- `retention-cleanup`.
- `reprocess-session`.
- `tenant-maintenance`.

Jobs use separate Container Apps Job resources and system-assigned managed identities. Job settings support non-secret environment overrides, Key Vault backed secret references, retry limits, timeouts, and independent CPU or memory sizing. The stage does not accept plain secret values for job configuration.

The runtime proof uses manual triggers only. Event or scheduled trigger policies can be added in later issues that own queue and scheduler decisions.

Backend key: `app_runtime.tfstate`

Remote backend example: `backend.azurerm.tf.example`

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

Use `/Users/harisubramaniam/.terraform.versions/terraform_1.14.7` locally when the system `terraform` binary is older than the stage `required_version`.

The stage intentionally uses Container App managed identities and secret references rather than hardcoded secret values. Supply image names, optional registry server, optional Log Analytics workspace ID, and Key Vault secret IDs through environment-specific workflow inputs or variable files.

Do not use `terraform apply -auto-approve`. Production applies must use the guarded manual workflow path defined by the Terraform production infrastructure architecture.
