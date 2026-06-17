# Foundation Stage

Responsibility: Resource groups, shared tags, deployment identities, Key Vault, role assignment foundations, and shared container image infrastructure.

Backend key: `foundation.tfstate`

Remote backend example: `backend.azurerm.tf.example`

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

This retained shared stage deploys the Azure Container Registry used by the guarded image publish workflow. It is intentionally excluded from the Terraform deletion workflow's disposable stage order so published images survive environment-stage cleanup.
