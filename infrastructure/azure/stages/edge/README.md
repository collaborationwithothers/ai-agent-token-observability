# Edge Stage

Responsibility: Azure Front Door Premium, WAF policy, service endpoints, service routes, origin groups, generated Container Apps FQDN origins, diagnostic settings, and rate-limit rules.

This stage intentionally leaves managed custom domains and managed certificates to issue #57. It leaves Front Door Private Link origin wiring and Container Apps public access lock-down to issue #58.

AVM check for issue #56:

- The local AVM checkout at `/Users/harisubramaniam/source/BHS/avm-terraform-modules` does not contain a CDN or Front Door profile module.
- The public Terraform Registry has `Azure/avm-res-cdn-profile/azurerm` version `0.1.9`.
- This implementation uses a repo-local wrapper at `../../modules/front_door_edge` with AzureRM resources because the checked-in AVM source used by this repo is unavailable for Front Door, and this issue needs WAF policy, security policy association, diagnostics, and rate-limit rules in one reviewable slice.

Backend key: `edge.tfstate`

Remote backend example: `backend.azurerm.tf.example`

Local validation:

```bash
cd infrastructure/azure
/Users/harisubramaniam/.terraform.versions/terraform_1.14.7 fmt -recursive
cd stages/edge
/Users/harisubramaniam/.terraform.versions/terraform_1.14.7 init -backend=false
/Users/harisubramaniam/.terraform.versions/terraform_1.14.7 validate
/Users/harisubramaniam/.terraform.versions/terraform_1.14.7 plan -input=false -lock=false \
  -var="environment=dv" \
  -var="azure_region=eastus2" \
  -var="customer_organization_slug=internal" \
  -var="terraform_workspace_name=dv_eastus2_internal" \
  -var="resource_instance=core" \
  -var='tags={environment="dv",region="eastus2",product="token-observability",owner="platform",data_classification="internal",managed_by="terraform"}' \
  -var='container_app_fqdns={product_dashboard="dashboard.example.azurecontainerapps.io",product_api="api.example.azurecontainerapps.io",product_ingestion_endpoint="ingest.example.azurecontainerapps.io"}' \
  -var='log_analytics_workspace_id=/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-to-dv-eastus2-internal-observability/providers/Microsoft.OperationalInsights/workspaces/to-dv-core-law'
```

Guarded plan workflow path:

1. Initialize the `edge` stage with the non-production remote backend config.
2. Select the workspace named `{environment}_{azureRegion}_{customerOrganizationSlug}`.
3. Pass `container_app_fqdns` from the `app_runtime` stage output.
4. Pass `log_analytics_workspace_id` from the `observability_foundation` stage output.
5. Run `terraform validate` before `terraform plan`.

Do not run `terraform apply` from local development. Production apply remains a guarded workflow operation.
