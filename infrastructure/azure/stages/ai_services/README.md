# AI Services Stage

Responsibility: Azure AI Language, Azure AI Content Safety, Azure OpenAI or Foundry resources, deployment aliases, and private access where feasible.

Backend key: `ai_services.tfstate`

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

This stage is a skeleton only. It does not deploy Azure resources yet.
