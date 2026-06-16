# App Runtime Stage

Responsibility: Container Apps environment, Product API, Product Ingestion Endpoint, Product Dashboard, Container Apps Jobs, managed identities, and app configuration.

This stage currently deploys the long-running runtime services only:

- Product Dashboard.
- Product API.
- Product Ingestion Endpoint.

Container Apps Jobs are implemented by the shared jobs image issue.

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
