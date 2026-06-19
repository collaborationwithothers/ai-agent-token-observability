## Purpose

This document defines the Terraform structure for the Azure Production MVP and the Multi-Tenant SaaS Target State.

It turns the agreed Terraform principles into implementation-ready stage boundaries, remote state rules, workspace naming, module conventions, and public-repository workflow guardrails.

The repository now contains the Terraform stage tree and the first deployment-adjacent workflow set. Current workflow coverage is split as follows:

- `.github/workflows/terraform-plan.yml` selects a normal Terraform deploy scope, plans each selected stage, waits for `terraform-apply` environment approval, and applies the exact reviewed saved plan artifact for that stage before planning the next dependent stage.
- `.github/workflows/terraform-destroy-plan.yml` creates guarded destroy plans and applies approved same-run destroy plan artifacts for disposable stages.
- `.github/workflows/edge-origin-validation.yml` validates Front Door hostnames and direct Azure Container Apps origin isolation.
- `.github/workflows/acr-image-publish.yml` builds and publishes the Product Dashboard, Product API, Product Ingestion Endpoint, and Product Jobs images to ACR and emits digest-pinned Terraform inputs.

Image build and publish is a separate production path from Terraform plan and apply. Terraform stages consume reviewed image references or digests; they do not build or publish container images.

## Source Documents

- [Azure Production MVP PRD](../../prd/azure-production-mvp.md)
- [Production Target State Spec](../../specs/production-target-state.md)
- [Azure Production Architecture](../azure-production-architecture.md)
- [Identity And Authorization Architecture](../identity-and-authorization.md)
- [Runtime Service Topology](../runtime-topology.md)
- [Content Capture And Redaction Architecture](../content-capture-and-redaction.md)
- [Recommendation Engine Architecture](../recommendation-engine.md)

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
- Normal Terraform apply must apply an exact reviewed saved plan artifact, not a fresh unreviewed plan.
- Runtime image publish workflows are manual, guarded, and ACR-scoped; they are separate from Terraform plan and apply workflows.
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

Operator-owned setup is defined in [Manual Terraform Remote State Foundation](../../operations/manual-terraform-remote-state.md).

Runbook path:

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
| 1 | Network private data plane | `stages/network_private_data_plane` | VNet, subnets, and network security boundaries |
| 2 | Observability foundation | `stages/observability_foundation` | Log Analytics, Application Insights, Azure Monitor workspace or managed Prometheus foundation |
| 3 | Data platform | `stages/data_platform` | PostgreSQL Flexible Server, product Blob Storage, backup and lifecycle settings |
| 4 | AI services | `stages/ai_services` | Azure AI Language, Azure AI Content Safety, Azure OpenAI or Foundry resources, deployment aliases, and diagnostics |
| 5 | App runtime | `stages/app_runtime` | Container Apps environment, Product API, Product Ingestion Endpoint, Product Dashboard, Container Apps Jobs, managed identities, app configuration |
| 6 | Managed Grafana | `stages/managed_grafana` | Azure Managed Grafana workspace, data source wiring, repo-versioned dashboard JSON deployment, Grafana folders, Grafana role integration |
| 7 | Edge | `stages/edge` | Azure Front Door Premium, WAF policy, routes, managed certificates, custom domains, rate-limit rules where supported |

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
| Network | `rg-network` | VNet, subnets, and network security groups |
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
| Deferred network hardening | AVM wrapper when reintroduced | AzureRM |
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
| `enable_zone_redundancy` | Environment-specific | Required decision for `pp` and `pd` where supported |
| `public_ingress_hostnames` | Stage-specific | Used by edge and app runtime |

Edge and origin evidence variables:

| Variable | Required | Notes |
| --- | --- | --- |
| `front_door_sku` | Yes | Must be `Premium_AzureFrontDoor` for production edge capabilities |
| `front_door_custom_domains` | Yes | Explicit first-release hostnames: `app`, `api`, and `ingest` under `tokenobs.consultwithcloud.com` |
| `use_front_door_managed_certificates` | Yes | Must be true for first release unless the certificate decision is reopened |
| `aca_origin_fqdns` | Stage output/input | Generated ACA FQDNs used as Front Door origin host names and origin host headers |

Edge and origin evidence validation:

- `front_door_sku` must be `Premium_AzureFrontDoor` in `pp` and `pd`.
- `use_front_door_managed_certificates` must be true for first-release hostnames unless a later ADR reopens the certificate decision.
- App runtime and edge stages must expose a proof command or test showing Front Door access succeeds against generated ACA FQDN origins.

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
