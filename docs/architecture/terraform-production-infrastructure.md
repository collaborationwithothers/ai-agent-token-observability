# Terraform Production Infrastructure

## Purpose

This document defines the Terraform structure for the Azure Production MVP and the Multi-Tenant SaaS Target State.

It turns the agreed Terraform principles into implementation-ready stage boundaries, remote state rules, workspace naming, module conventions, and public-repository workflow guardrails.

This repository currently has no Terraform stage tree or deployment workflow tree. This document is therefore a greenfield contract for future infrastructure issues.

## Source Documents

- [Azure Production MVP PRD](../prd/azure-production-mvp.md)
- [Production Target State Spec](../specs/production-target-state.md)
- [Azure Production Architecture](./azure-production-architecture.md)
- [Identity And Authorization Architecture](./identity-and-authorization.md)
- [Runtime Service Topology](./runtime-topology.md)
- [Content Capture And Redaction Architecture](./content-capture-and-redaction.md)
- [Recommendation Engine Architecture](./recommendation-engine.md)

## Decisions

- Terraform is the only production infrastructure language for this product.
- Azure Blob Storage is the Terraform remote state backend.
- Terraform remote state storage is created manually before Terraform production stages run.
- Workspaces are scoped by environment, Azure region, and Customer Organization slug.
- Azure Verified Modules are preferred where a suitable module exists.
- AzureRM resources are used when no suitable AVM exists.
- AzAPI is used only for Azure provider gaps that AzureRM cannot model.
- Deployment-capable workflows are manual only.
- Production applies are guarded and must not use `terraform apply -auto-approve`.
- No Customer Managed Keys are offered.
- Platform-managed encryption is the only encryption mode.

## Workspace Contract

Workspace format:

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

Rules:

- `environment` must be one of `dv`, `qa`, `pp`, or `pd`.
- `azureRegion` must be the lowercase Azure location name, such as `eastus`, `eastus2`, or `westeurope`.
- `customerOrganizationSlug` must be lowercase, URL-safe, and stable for the Customer Organization.
- The first-release Single-Enterprise Release uses `internal` until a real Customer Organization slug is chosen.
- The workspace must be derived from workflow inputs and verified before `terraform init`, `terraform plan`, or `terraform apply`.
- The default Terraform workspace must never be used for Azure-changing plans or applies.
- All remote state data source references must use the same workspace unless an explicit cross-region or shared-platform dependency is documented in the stage.

Target-state tenancy rule:

- Shared SaaS platform stages can use a shared platform slug when they do not hold customer data.
- Customer-data stages must include the Customer Organization slug or an approved tenant isolation key.
- Dedicated-tenant stages must never share state with shared SaaS customer-data stages.

## Manual Remote State Foundation

The remote state storage account and blob container are created manually before normal production stages run.

There is no Terraform stage that creates the production remote state storage account.

Recommended operator-owned runbook path:

```text
docs/operations/manual-terraform-remote-state.md
```

Manual setup responsibilities:

- Create the remote state resource group.
- Create the remote state storage account.
- Create the `tfstate` blob container.
- Disable public blob access.
- Enable blob versioning and soft delete where supported.
- Restrict public network access for production use where feasible.
- Assign the workflow deployment identity `Storage Blob Data Contributor` at the container or storage account scope.
- Record non-secret backend values for stage initialization.

Backend rules:

- Stages use the `azurerm` backend.
- Backend authentication uses Microsoft Entra ID and OIDC in GitHub Actions.
- Backend secrets, storage account keys, SAS tokens, or access keys must not be stored in workflow secrets for normal production workflows.
- Backend configuration values that are not sensitive can be supplied through backend config files or workflow inputs.
- Each stage uses a stable backend key based on the stage name, for example `network_private_data_plane.tfstate`.
- Workspace isolation is mandatory in addition to the stage-specific backend key.

Example backend key pattern:

```text
{stageName}.tfstate
```

The backend storage account is a shared platform dependency. It is not recreated per customer unless the target-state dedicated-tenant tier requires separate state custody.

## Stage Layout

Stages live under:

```text
infrastructure/azure/stages
```

Each stage contains:

- `backend.tf`
- `providers.tf`
- `versions.tf`
- `variables.tf`
- `locals.tf`
- `main.tf`
- `outputs.tf`
- optional `data.tf`
- optional `README.md`

Required stages:

| Order | Stage | Path | Responsibility |
| --- | --- | --- | --- |
| 0 | Foundation | `stages/foundation` | Resource groups, shared tags, deployment identities, Key Vault, role assignment foundations |
| 1 | Network private data plane | `stages/network_private_data_plane` | VNet, subnets, private DNS zones, private endpoints, network security boundaries |
| 2 | Observability foundation | `stages/observability_foundation` | Log Analytics, Application Insights, Azure Monitor workspace or managed Prometheus foundation |
| 3 | Data platform | `stages/data_platform` | PostgreSQL Flexible Server, product Blob Storage, backup and lifecycle settings |
| 4 | AI services | `stages/ai_services` | Azure AI Language, Azure AI Content Safety, Azure OpenAI or Foundry resources, deployment aliases, private access where feasible |
| 5 | App runtime | `stages/app_runtime` | Container Apps environment, Product API, Product Ingestion Endpoint, Product Dashboard, Container Apps Jobs, managed identities, app configuration |
| 6 | Managed Grafana | `stages/managed_grafana` | Azure Managed Grafana workspace, data source wiring, repo-versioned dashboard JSON deployment, Grafana folders, Grafana role integration |
| 7 | Edge | `stages/edge` | Azure Front Door Premium, WAF policy, routes, Private Link origins, managed certificates, custom domains, rate-limit rules where supported |

Stage dependency flow:

```text
manual_remote_state
  -> foundation
  -> network_private_data_plane
  -> observability_foundation
  -> data_platform
  -> ai_services
  -> app_runtime
  -> managed_grafana
  -> edge
```

Notes:

- `edge` is last because it binds public routes to deployed app origins.
- `managed_grafana` is after app runtime so dashboard links and identity assignments can reference runtime resources.
- `ai_services` is before app runtime because recommendation jobs need endpoint and identity configuration.
- `observability_foundation` is before app runtime because Container Apps and jobs need diagnostic destinations.

## Resource Group Boundaries

Use naming conventions in code, but keep these logical boundaries:

| Boundary | Example logical name | Contents |
| --- | --- | --- |
| State | `rg-state` | Terraform state storage only |
| Foundation | `rg-foundation` | Key Vault, deployment identities, shared role infrastructure |
| Network | `rg-network` | VNet, subnets, private DNS, private endpoints |
| Observability | `rg-observability` | Log Analytics, Application Insights, Azure Monitor workspace, Managed Grafana if not split |
| Data | `rg-data` | PostgreSQL, product Blob Storage |
| AI services | `rg-ai` | Azure AI Language, Content Safety, Azure OpenAI or Foundry |
| App runtime | `rg-app` | Container Apps environment, Container Apps, jobs, managed identities |
| Edge | `rg-edge` | Front Door, WAF, custom domains |

The exact Azure resource names are generated by the naming module when implementation starts. The logical boundary names above are not literal resource names.

## Module Conventions

Reusable modules live under:

```text
infrastructure/azure/modules
```

Wrapper rule:

- Stages call local wrapper modules.
- Local wrapper modules may call AVM modules.
- Stages must not call external AVM modules directly.

Selection order:

1. Use an Azure Verified Module through a local wrapper when a suitable resource or pattern module exists.
2. Use AzureRM resources inside a local wrapper when no suitable AVM module exists.
3. Use AzAPI inside a local wrapper only when AzureRM cannot model the required resource or feature.

Wrapper module structure:

```text
infrastructure/azure/modules/<capability>/main.tf
infrastructure/azure/modules/<capability>/variables.tf
infrastructure/azure/modules/<capability>/outputs.tf
```

Module rules:

- No provider blocks inside modules.
- Providers are configured only in stages.
- Use stable `for_each` keys for repeatable resources.
- Pin AVM module versions.
- Pin provider versions.
- Set `enable_telemetry = false` where an AVM module supports it.
- Outputs must expose only downstream-required values.
- Modules must not output secrets.

## AVM And Provider Map

| Capability | Preferred implementation | Fallback |
| --- | --- | --- |
| Resource groups | AVM wrapper | AzureRM |
| Key Vault | AVM wrapper | AzureRM |
| Storage accounts and containers | AVM wrapper | AzureRM |
| Virtual network and subnets | AVM wrapper | AzureRM |
| Private endpoints and private DNS | AVM wrapper | AzureRM |
| Log Analytics workspace | AVM wrapper | AzureRM |
| Application Insights | AVM wrapper | AzureRM |
| Azure Monitor workspace or managed Prometheus | AVM wrapper if available | AzureRM or AzAPI |
| PostgreSQL Flexible Server | AVM wrapper if available | AzureRM |
| Azure Container Apps managed environment | AVM wrapper if available | AzureRM |
| Azure Container Apps and jobs | AVM wrapper if available | AzureRM |
| Azure Managed Grafana | AVM wrapper if available | AzureRM or AzAPI |
| Azure Front Door and WAF | AVM wrapper if available | AzureRM |
| Azure AI Language | AzureRM if supported | AzAPI |
| Azure AI Content Safety | AzureRM if supported | AzAPI |
| Azure OpenAI or Foundry model deployments | AzureRM if supported | AzAPI |

Implementation issues must verify the current AVM and provider support before choosing the fallback.

## Stage Variables

Every stage must accept:

| Variable | Required | Notes |
| --- | --- | --- |
| `environment` | Yes | One of `dv`, `qa`, `pp`, `pd` |
| `azure_region` | Yes | Lowercase Azure location |
| `customer_organization_slug` | Yes | `internal` for first-release internal enterprise setup |
| `resource_instance` | Yes | Stable short instance identifier |
| `tags` | Yes | Must include environment, region, product, owner, data classification, and managed-by |
| `allowed_regions` | Yes | Validation list for workflow input and Terraform variable validation |
| `allowed_customer_organization_slugs` | Yes | Validation list for deployment scope |
| `enable_private_endpoints` | Yes | Defaults true for customer-data resources |
| `enable_zone_redundancy` | Environment-specific | Required decision for `pp` and `pd` where supported |
| `public_ingress_hostnames` | Stage-specific | Used by edge and app runtime |

Edge and origin isolation variables:

| Variable | Required | Notes |
| --- | --- | --- |
| `front_door_sku` | Yes | Must be `Premium_AzureFrontDoor` for production because Private Link origins are required |
| `enable_front_door_private_link_origins` | Yes | Must be true for `pp` and `pd` |
| `disable_container_apps_public_network_access` | Yes | Must be true for `pp` and `pd` |
| `front_door_custom_domains` | Yes | Explicit first-release hostnames: `app`, `api`, and `ingest` under `tokenobs.consultwithcloud.com` |
| `use_front_door_managed_certificates` | Yes | Must be true for first release unless the certificate decision is reopened |
| `aca_origin_fqdns` | Stage output/input | Generated ACA FQDNs used as Front Door origin host names and origin host headers |

Edge and origin isolation validation:

- `front_door_sku` must be `Premium_AzureFrontDoor` in `pp` and `pd`.
- `enable_front_door_private_link_origins` must be true in `pp` and `pd`.
- `disable_container_apps_public_network_access` must be true in `pp` and `pd`.
- `use_front_door_managed_certificates` must be true for first-release hostnames unless a later ADR reopens the certificate decision.
- App runtime and edge stages must expose a non-production proof command or test showing direct generated ACA FQDN access is blocked while Front Door access succeeds.

Managed Grafana stage variables:

| Variable | Required | Notes |
| --- | --- | --- |
| `grafana_admin_group_object_id` | Yes | Microsoft Entra group object ID for Grafana Admin |
| `grafana_editor_group_object_id` | Environment-specific | Microsoft Entra group object ID for Grafana Editor |
| `grafana_viewer_group_object_id` | Yes | Microsoft Entra group object ID for Grafana Viewer |
| `allow_production_grafana_editors` | Yes | Defaults false; explicit exception gate for `pp` or `pd` Editor assignment |
| `grafana_provider_auth_mode` | Yes | Defaults `entra_oidc`; `service_account_token` only after provider proof failure |
| `allow_grafana_service_account_fallback` | Yes | Defaults false; explicit gate for Grafana service account token fallback |
| `grafana_service_account_token_secret_name` | Fallback only | Key Vault secret name for Grafana service account token when fallback is approved |

Managed Grafana role-assignment validation:

- Use Microsoft Entra group object IDs, not display names, for Grafana role assignments.
- `grafana_viewer_group_object_id` must be set for every workspace.
- `grafana_admin_group_object_id` must be set for every workspace and must point to a small break-glass or platform operations group.
- `grafana_editor_group_object_id` may be set in `dv` and `qa`.
- In `pp` and `pd`, `grafana_editor_group_object_id` must be null or empty unless `allow_production_grafana_editors = true`.
- `allow_production_grafana_editors` must default to `false`.
- Terraform validation or precondition checks must fail before apply when `environment` is `pp` or `pd`, `grafana_editor_group_object_id` is set, and `allow_production_grafana_editors` is false.
- GitHub environment protection must be used for a `pd` apply where `allow_production_grafana_editors = true`.
- `grafana_provider_auth_mode` must default to `entra_oidc`.
- `grafana_provider_auth_mode = service_account_token` must fail unless `allow_grafana_service_account_fallback = true`.
- `allow_grafana_service_account_fallback` must default to `false`.
- Service account fallback must require a documented non-production proof that Entra OIDC is incompatible with the selected Grafana provider path.
- Service account fallback tokens must be read from Key Vault at workflow runtime and must not be output from Terraform.

Environment-specific defaults:

| Environment | Purpose | Guarding expectation |
| --- | --- | --- |
| `dv` | Development integration | Manual workflow, repository and actor validation |
| `qa` | Quality validation | Manual workflow, repository and actor validation |
| `pp` | Pre-production | GitHub environment protection and apply approval |
| `pd` | Production | GitHub environment protection, apply approval, branch restriction, and stricter role assignment |

## Remote State References

Stages may use `terraform_remote_state` only for stable platform outputs:

- Resource group IDs.
- VNet and subnet IDs.
- Private DNS zone IDs.
- Log Analytics workspace IDs.
- Azure Monitor workspace IDs.
- Managed identity principal IDs.
- Storage account IDs.
- PostgreSQL server IDs.
- Container Apps environment IDs.

Rules:

- Do not read secrets from remote state.
- Do not pass credentials through remote state outputs.
- Do not use remote state to couple app configuration that belongs in Key Vault or product metadata.
- Every remote state data source must state the stage it reads from.
- Every remote state data source must use the same workspace unless a documented shared-platform exception applies.
- Cross-region remote state reads are forbidden unless the stage documents a target-state multi-region dependency.

## Public Repository Workflow Guardrails

Deployment-capable GitHub Actions must be designed for a public repository.

Infrastructure deletion is defined separately in [../operations/infrastructure-deletion.md](../operations/infrastructure-deletion.md). Deletion must use guarded Terraform destroy plans by stage and workspace, not tag-based Azure deletion.

Allowed trigger:

```yaml
on:
  workflow_dispatch:
```

Forbidden triggers for Azure-changing workflows:

- `push`
- `pull_request`
- `pull_request_target`
- `workflow_run`
- `schedule`

Required workflow inputs:

- `environment`
- `azure_region`
- `customer_organization_slug`
- `stage`
- `plan_or_apply`
- `confirmation`

Required validations before Azure login:

- `github.repository` equals the expected repository.
- `github.event_name` equals `workflow_dispatch`.
- `github.actor` is in the allowed deployment actor list or belongs to the required protected environment reviewer path.
- `github.ref` is allowed for the requested environment.
- `environment` is one of `dv`, `qa`, `pp`, or `pd`.
- `azure_region` is in the allowed region list.
- `customer_organization_slug` is in the allowed deployment scope list.
- `stage` is in the allow-list of stage directories.
- Derived workspace equals `{environment}_{azureRegion}_{customerOrganizationSlug}`.
- `confirmation` exactly matches the requested operation for apply.

Required GitHub permissions:

```yaml
permissions:
  contents: read
  id-token: write
```

Rules:

- `id-token: write` is allowed only for jobs that need Azure OIDC.
- `GITHUB_TOKEN` permissions must remain least privilege.
- Workflow scripts must not interpolate untrusted context directly into shell commands.
- Forked PRs must never receive Azure credentials or deployment-capable tokens.
- Apply jobs for `pp` and `pd` must use GitHub environments with required reviewers.
- Managed Azure VNet runners may be used for private resource validation and Terraform operations, but they do not replace public repository workflow guards or Front Door origin isolation.
- Plan and apply must be separate jobs or separate workflow modes with explicit apply confirmation.
- Apply must use the exact plan artifact from the guarded plan job where feasible.
- `terraform apply -auto-approve` is forbidden in production workflows.

Guardrail validator:

- A committed validator must inspect `.github/workflows/*.yml`.
- The validator must fail if an Azure-changing workflow has forbidden triggers.
- The validator must fail if an Azure-changing workflow lacks repository, actor, environment, region, workspace, branch, OIDC, permissions, and confirmation checks.
- The validator must fail if `terraform apply -auto-approve` appears in deployment-capable workflows.
- The validator must have tests with unsafe workflow fixtures.

Recommended validator path:

```text
scripts/validate-terraform-workflow-guardrails.sh
tests/workflow-guardrails/
```

## Command Contract

Local validation from a stage directory:

```bash
terraform fmt -check -recursive
terraform init -backend=false
terraform validate
```

Remote plan flow:

```bash
terraform init
terraform workspace select "$TF_WORKSPACE" || terraform workspace new "$TF_WORKSPACE"
terraform validate
terraform plan -out "$PLAN_FILE"
```

Remote apply flow:

```bash
terraform workspace show
terraform apply "$PLAN_FILE"
```

Rules:

- Always run `terraform validate` before `terraform plan`.
- Never plan or apply from the default workspace.
- Never apply without displaying the selected workspace.
- Never apply without an approved plan artifact for `pp` or `pd`.
- Do not use `terraform apply -auto-approve` in guarded production workflows.

The currently installed local Terraform binary in this workspace is `v1.3.5`, which is older than the current Terraform version reported by the CLI. Implementation issues should decide the pinned Terraform version before creating workflow jobs.

## Resource Acceptance Criteria

Terraform implementation issues must verify:

- Remote state is stored in Azure Blob Storage.
- Backend authentication uses Entra ID and OIDC for GitHub Actions.
- The default workspace cannot be used for Azure-changing workflows.
- Workspaces follow `{environment}_{azureRegion}_{customerOrganizationSlug}`.
- Each stage has an explicit backend key.
- Each stage has variable validation for environment and region.
- AVM availability is checked before falling back to AzureRM or AzAPI.
- Data stores are private where feasible.
- Public HTTPS ingress exists only through Azure Front Door for Product Dashboard, Product Ingestion Endpoint, and Product API routes that must be browser-facing.
- Front Door Premium WAF protects public product ingress.
- Front Door managed certificates serve the first-release product hostnames.
- Container Apps origins are reached through Front Door Private Link in production.
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
- Public-repository workflow guardrail tests fail unsafe examples.

## Verified Platform Facts

- Terraform `azurerm` backend stores state as a blob in an Azure Storage container and supports state locking and consistency checking with Azure Blob Storage native capabilities: https://developer.hashicorp.com/terraform/language/backend/azurerm
- HashiCorp documents Microsoft Entra ID with OIDC and workload identity federation as a recommended authentication path for the `azurerm` backend: https://developer.hashicorp.com/terraform/language/backend/azurerm
- Microsoft documents creating an Azure Storage account and container before using Azure Storage as a Terraform backend, and notes that Terraform state can contain secrets and must be secured: https://learn.microsoft.com/en-us/azure/developer/terraform/store-state-in-azure-storage
- Microsoft describes Azure Verified Modules as reusable Infrastructure as Code modules for Azure, available for Bicep and Terraform, developed and maintained for consistency and best-practice alignment: https://learn.microsoft.com/en-us/community/content/azure-verified-modules
- GitHub Actions OIDC lets workflows request short-lived cloud access tokens instead of storing long-lived cloud credentials in GitHub secrets: https://docs.github.com/en/actions/concepts/security/openid-connect
- GitHub recommends least-privilege workflow credentials and limiting `GITHUB_TOKEN` permissions to the minimum required: https://docs.github.com/en/actions/reference/security/secure-use
- Azure Container Apps is the Azure service for running containerized applications without managing orchestration infrastructure: https://learn.microsoft.com/en-us/azure/container-apps/
- Azure Front Door has Terraform quickstart support for Front Door Standard and Premium profiles: https://learn.microsoft.com/en-us/azure/frontdoor/create-front-door-terraform
- Azure Front Door Premium supports Private Link to origins: https://learn.microsoft.com/en-us/azure/frontdoor/private-link
- Azure Container Apps can be exposed through Azure Front Door Premium with public network access disabled: https://learn.microsoft.com/en-us/azure/container-apps/front-door-custom-virtual-network-private-link
- Azure Front Door managed certificates use DNS TXT validation for custom domains: https://learn.microsoft.com/en-us/azure/frontdoor/domain
- Azure Managed Grafana is a fully managed Azure service for Grafana dashboards: https://learn.microsoft.com/en-us/azure/managed-grafana/overview
- Azure Managed Grafana supports assigning Grafana roles to Microsoft Entra users and groups: https://learn.microsoft.com/en-us/azure/managed-grafana/how-to-manage-access-permissions-users-identities
- Terraform variables support validation rules that can reject invalid input before Terraform completes planning: https://developer.hashicorp.com/terraform/language/block/variable#validation
