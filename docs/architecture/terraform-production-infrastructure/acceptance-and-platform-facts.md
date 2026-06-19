## Resource Acceptance Criteria

Terraform implementation issues must verify:

- Remote state is stored in Azure Blob Storage.
- Backend authentication uses Entra ID and OIDC for GitHub Actions.
- The default workspace cannot be used for Azure-changing workflows.
- Workspaces follow `{environment}_{azureRegion}_{customerOrganizationSlug}`.
- Each stage has an explicit backend key.
- Each stage has variable validation for environment and region.
- AVM availability is checked before falling back to AzureRM or AzAPI.
- Data stores use public access only through approved firewall or network allowlists until deferred network hardening is implemented.
- Public HTTPS ingress exists only through Azure Front Door for Product Dashboard, Product Ingestion Endpoint, and Product API routes that must be browser-facing.
- Front Door Premium WAF protects public product ingress.
- Front Door managed certificates serve the first-release product hostnames.
- Container Apps origin hardening is deferred and must not rely on approval-based origin isolation in the current Terraform path.
- Direct public access to generated ACA FQDNs is blocked in production.
- Container Apps host product services and Container Apps Jobs host bounded background work.
- Managed Grafana is wired to Azure Monitor workspace or managed Prometheus aggregate metrics as the first-release data source.
- Managed Grafana dashboard JSON is versioned in the repo and deployed through Terraform; production dashboards are not manual UI-only state.
- Managed Grafana production role assignments default human users to Grafana Viewer; Grafana Editor is rejected in production unless an explicit exception is approved.
- Managed Grafana role assignment variables use environment-scoped Entra group object IDs and reject display-name based authorization.
- `pp` and `pd` Grafana Editor assignments fail validation unless `allow_production_grafana_editors` is true.
- Managed Grafana provider authentication uses Entra OIDC by default, with service account token fallback disabled unless a non-production proof records provider incompatibility.
- Terraform state does not output or store application secrets.
- Production workflows are manual, guarded, and OIDC based.
- Runtime image build and publish workflows are manual, guarded, ACR-only, and separate from Terraform plan and apply.
- Public-repository workflow guardrail tests fail unsafe examples.

## Verified Platform Facts

- Terraform `azurerm` backend stores state as a blob in an Azure Storage container and supports state locking and consistency checking with Azure Blob Storage native capabilities: https://developer.hashicorp.com/terraform/language/backend/azurerm
- HashiCorp documents Microsoft Entra ID with OIDC and workload identity federation as a recommended authentication path for the `azurerm` backend: https://developer.hashicorp.com/terraform/language/backend/azurerm
- Microsoft documents creating an Azure Storage account and container before using Azure Storage as a Terraform backend, and notes that Terraform state can contain secrets and must be secured: https://learn.microsoft.com/en-us/azure/developer/terraform/store-state-in-azure-storage
- Microsoft describes Azure Verified Modules as reusable Infrastructure as Code modules for Azure, available for Bicep and Terraform, developed and maintained for consistency and best-practice alignment: https://learn.microsoft.com/en-us/community/content/azure-verified-modules
- GitHub Actions OIDC lets workflows request short-lived cloud access tokens instead of storing long-lived cloud credentials in GitHub secrets: https://docs.github.com/en/actions/concepts/security/openid-connect
- GitHub recommends least-privilege workflow credentials and limiting `GITHUB_TOKEN` permissions to the minimum required: https://docs.github.com/en/actions/reference/security/secure-use
- Microsoft documents publishing Docker images from GitHub Actions to Azure Container Registry by authenticating to the registry and running Docker build and push commands: https://learn.microsoft.com/en-us/azure/app-service/deploy-container-github-action
- Docker build-push-action exposes a digest output from successful image builds for downstream evidence and artifact generation: https://github.com/docker/build-push-action/blob/master/README.md
- Terraform `plan -out=FILE` saves a generated plan that can later be passed to `terraform apply` in automation: https://developer.hashicorp.com/terraform/cli/commands/plan
- Terraform saved plan mode applies the operations in the saved plan file when that file is passed to `terraform apply`: https://developer.hashicorp.com/terraform/cli/commands/apply
- Azure Container Apps is the Azure service for running containerized applications without managing orchestration infrastructure: https://learn.microsoft.com/en-us/azure/container-apps/
- Azure Front Door has Terraform quickstart support for Front Door Standard and Premium profiles: https://learn.microsoft.com/en-us/azure/frontdoor/create-front-door-terraform
- Azure Front Door managed certificates use DNS TXT validation for custom domains: https://learn.microsoft.com/en-us/azure/frontdoor/domain
- Azure Managed Grafana is a fully managed Azure service for Grafana dashboards: https://learn.microsoft.com/en-us/azure/managed-grafana/overview
- Azure Managed Grafana supports assigning Grafana roles to Microsoft Entra users and groups: https://learn.microsoft.com/en-us/azure/managed-grafana/how-to-manage-access-permissions-users-identities
- Terraform variables support validation rules that can reject invalid input before Terraform completes planning: https://developer.hashicorp.com/terraform/language/block/variable#validation
