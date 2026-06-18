# Implementation Readiness Review

## Purpose

This review checks whether the production docs are detailed enough to create GitHub issues without reintroducing local-first assumptions or leaving implementation teams to guess major contracts.

Status: ready for implementation issue creation.

The production docs are specific enough to create GitHub implementation issues. Remaining items are implementation proofs, environment-specific values, or execution tasks that should be captured as issues rather than more requirements discovery.

## Reviewed Documents

- [Production Target State Spec](../specs/production-target-state.md)
- [Azure Production MVP PRD](../prd/azure-production-mvp.md)
- [Azure Production Architecture](./azure-production-architecture.md)
- [Codex Production Ingestion Contract](./codex-production-ingestion-contract.md)
- [Product API Contract](./product-api-contract.md)
- [Runtime Service Topology](./runtime-topology.md)
- [Product Dashboard UX Architecture](./product-dashboard-ux.md)
- [Managed Grafana Dashboards](./managed-grafana-dashboards.md)
- [Aggregate Metrics Contract](./aggregate-metrics-contract.md)
- [Terraform Production Infrastructure](./terraform-production-infrastructure.md)
- [Public DNS And Certificates](./public-dns-and-certificates.md)
- [Edge Origin Validation](../operations/edge-origin-validation.md)
- [Production Operations](../operations/production-operations.md)
- [Production Codebase Transition](./production-codebase-transition.md)
- [Production Implementation Roadmap](../planning/production-implementation-roadmap.md)
- [Certificate Renewal](../operations/certificate-renewal.md) as a deferred BYOC runbook, not a first-release requirement
- [Infrastructure Deletion Workflow](../operations/infrastructure-deletion.md)
- [Production Data Model](./data-model.md)
- [Identity And Authorization Architecture](./identity-and-authorization.md)
- [Content Capture And Redaction Architecture](./content-capture-and-redaction.md)
- [Recommendation Engine Architecture](./recommendation-engine.md)
- [ADR 0002](../adr/0002-replace-local-first-with-azure-production-saas.md)
- [ADR 0003](../adr/0003-use-react-spa-for-production-dashboard.md)

## Ready Areas

These areas are specific enough to become implementation issues.

| Area | Readiness | Evidence |
| --- | --- | --- |
| Product pivot | Ready | ADR 0002 defines Azure Production MVP and Multi-Tenant SaaS Target State |
| Codex-first MVP harness | Ready | PRD and ingestion contract identify Codex CLI as first harness |
| Ingestion transport | Ready | OTLP/HTTP over HTTPS is the first-release public transport |
| Credential-derived identity | Ready | Ingestion and identity docs agree that Scoped Ingestion Credential is authoritative |
| Product metadata model | Ready | Production data model defines tenant-aware logical entities |
| Product API route contract | Ready | Product API Contract defines route prefixes, authorization actions, route responsibilities, errors, idempotency, and MVP acceptance criteria |
| Runtime service topology | Ready | Runtime Service Topology defines three long-running Container Apps, one shared jobs image with explicit commands, Front Door-only public ingress, and production blocking of direct public ACA generated FQDN access |
| Product Dashboard frontend | Ready | Product Dashboard UX Architecture defines React, TypeScript, Vite, route map, role visibility, content states, and UX acceptance criteria |
| Managed Grafana dashboard contract | Ready | Managed Grafana Dashboards and Aggregate Metrics Contract define Azure Monitor workspace or managed Prometheus as the first-release aggregate metrics data source, four first-release dashboards, required panel families, forbidden panels, exact dashboard UIDs, JSON artifact paths, aggregate metric names, labels, PromQL query contracts, Terraform-managed provisioning with repo-versioned dashboard JSON, Provider Authentication Proof Gate, service-account-token fallback constraints, coarse Grafana Admin/Editor/Viewer RBAC, environment-scoped Entra group object ID variables, production human Viewer-only default, and limits Log Analytics or Application Insights to later aggregate operational panels |
| No store-then-redact | Ready | Content architecture and data model both forbid raw durable storage before redaction |
| Redaction execution details | Ready | Content architecture defines MVP recognizers, Azure AI Language PII category set, Content Safety behavior, confidence thresholds, Blob Storage layout, and retention defaults |
| LLM authority boundary | Ready | Recommendation docs distinguish confirmed findings from LLM-inferred candidates |
| Recommendation engine contract | Ready | Recommendation Engine Architecture defines deterministic rules, evidence packet schema, structured LLM output schema, validation gates, prompt templates, deployment aliases, evaluator set, and candidate review workflow |
| Non-punitive product posture | Ready | Target spec, PRD, identity, recommendation, and data model align |
| Terraform production infrastructure | Ready | Terraform Production Infrastructure defines stage directories, manually created remote state foundation, backend rules, workspace validation, AVM/AzureRM/AzAPI selection, resource group boundaries, workflow gates, Front Door Premium Private Link origin variables, managed certificate variables, ACA public-network-disable validation, and guardrail validator rules |
| Public DNS, certificate, and edge-origin boundary | Ready at boundary level | Public DNS And Certificates defines Cloudflare apex ownership, Azure DNS delegated product zone, product hostnames, Azure Front Door managed certificates for explicit hostnames, Front Door Premium Private Link to ACA origins, ACA direct-bypass prevention, and retained shared DNS resources |
| Edge-origin validation | Ready | Edge Origin Validation defines DNS, managed certificate, Front Door route, Private Link origin, direct ACA bypass, origin host header, auth callback, and sanitized-output proof requirements |
| Day-1 operations baseline | Ready | Production Operations defines health and readiness endpoints, Container Apps probes, internal SLOs, minimum Azure Monitor alerts, private action groups, PostgreSQL restore drill, Blob lifecycle validation, audit export, and required incident runbooks |
| Codebase transition | Ready | Production Codebase Transition inventories current local-first projects and assigns delete, replace, retain, or quarantine disposition so production issues do not evolve local-only mode in place |
| Implementation roadmap | Ready | Production Implementation Roadmap defines milestone-based vertical slices, issue shape requirements, label model, and first milestone as Production Skeleton And Guardrails |
| Deferred BYOC certificate renewal runtime | Deferred | Certificate Renewal is retained as a future BYOC wildcard runbook only. It must not drive first-release implementation issues unless the Front Door managed certificate decision is reopened |
| Infrastructure deletion workflow | Ready | Infrastructure Deletion Workflow defines Terraform-only stage-based destroy, reverse dependency order, retained shared resources, workflow gates, command contract, state handling, and guardrail validator rules |

## Implementation Proof Items

### 1. Managed Grafana Implementation Proof

Status: Grafana requirements are defined. The remaining work is implementation proof and environment-specific values, not requirements discovery.

Implementation issue creation needs:

- KQL query contract only if a later aggregate operational panel requires Log Analytics.
- Environment-specific values for `grafana_admin_group_object_id`, `grafana_editor_group_object_id`, and `grafana_viewer_group_object_id`.
- Terraform validation implementation for rejecting `pp` or `pd` Grafana Editor assignment unless `allow_production_grafana_editors = true`.
- Grafana provider Entra token compatibility proof.
- Key Vault-backed service account fallback only if Entra token compatibility fails.
- No-individual-ranking dashboard constraints.
- Product Dashboard implementation of Grafana link query-parameter validation.
- Native Azure Managed Grafana endpoint usage, with no first-release Grafana vanity hostname.

Recommended next step: create implementation issues for Grafana Terraform provisioning, provider-auth proof, dashboard JSON artifacts, role assignment validation, and Product Dashboard link allowlist.

### 2. Edge And Origin Proof

Status: DNS delegation, explicit hostname certificate scope, Front Door managed certificate use, Front Door Premium Private Link origins, ACA direct-bypass prevention, and the end-to-end proof contract are defined. The remaining work is implementation, not requirements discovery.

Implementation issue creation needs:

- Front Door managed certificate domain validation records for `app`, `api`, and `ingest`.
- Front Door Premium Private Link origin Terraform implementation for Azure Container Apps.
- ACA environment configuration that disables public network access in production.
- Proof execution showing Front Door hostnames work while generated ACA FQDNs are not publicly reachable.
- Auth callback and forwarded host validation through Front Door.

Recommended next step: create implementation issues for the edge stage, app runtime origin settings, DNS records, and validation workflow.

### 3. Operational Readiness

Status: Day-1 operations requirements are defined. The remaining work is implementation and environment-specific values, not requirements discovery.

Implementation issue creation needs:

- Terraform alert rule implementation.
- Action group target values.
- Health and readiness endpoint implementation per service.
- Container Apps probe settings.
- SLO metric and query implementation.
- Non-production PostgreSQL restore drill execution.
- Blob lifecycle validation execution.
- Runbook files for the required first-release incident paths.

Recommended next step: create implementation issues for health endpoints, probes, alerts, restore drill, lifecycle validation, and runbooks.

### 4. Current Codebase Transition

Status: the codebase transition boundary is defined. Milestone 0 issues implement the transition in slices rather than reopening requirements discovery.

Implementation issue creation needs:

- Keep the production solution skeleton as the active build path.
- Keep the React Product Dashboard separate from the superseded Blazor dashboard.
- Keep Product Ingestion Endpoint and Product Jobs separate from superseded direct file import.
- Keep local-first tests out of active production validation.
- Add production tests for each new production contract before implementing behavior.

Recommended next step: continue the Milestone 0 guardrail issues, then move into Terraform and workflow guardrails.

## Stale Or Superseded Docs

These docs are still useful but must not drive production MVP implementation issues:

| Document | Status |
| --- | --- |
| [Local-First MVP PRD](../archive/local-first/local-first-mvp.md) | Superseded |
| [ADR 0001](../archive/local-first/0001-use-dotnet-aspire-and-blazor-for-local-first-mvp.md) | Superseded |
| [Copilot OTel Field Mapping](../archive/future-adapters/copilot-otel-field-mapping.md) | Historical Phase 0 reference for future Copilot adapter work |
| [Idea: AI Agent Token Burn Observatory](../ideas/IDEA-ai-agent-token-observability.md) | Historical ideation background only |
| [Spec Kit Spec-Bloat Scenario](../archive/demos/spec-kit-spec-bloat.md) | Historical scenario background only |
| [Manual Spec Kit Spec-Bloat Demo Runbook](../archive/demos/manual-spec-kit-spec-bloat.md) | Historical demo background only |

## Issue Creation Recommendation

Create the GitHub implementation backlog as vertical slices.

The final consistency audit found and corrected stale local-first direction in `README.md`, `AGENTS.md`, and this readiness review. The remaining local-first references are in superseded or historical documents, transition docs, or explicit "do not carry forward" guidance.

Use [Production Implementation Roadmap](../planning/production-implementation-roadmap.md) for milestone order, issue shape, and labels.

Create issues as vertical slices, not component-only tasks. Each issue should name:

- User outcome.
- Product surface.
- Data model entities touched.
- Authorization scope.
- Telemetry or content evidence involved.
- Azure resources involved.
- Tests and verification.
- Non-punitive and privacy guardrails.

## Verified Platform Facts

- Azure Container Apps supports managed identities for service-to-service Azure access: https://learn.microsoft.com/en-us/azure/container-apps/managed-identity
- Azure Container Apps security guidance covers identity, secrets management, and token store concerns: https://learn.microsoft.com/en-us/azure/container-apps/security
- OTLP/HTTP uses HTTP POST with protobuf binary or JSON encoding: https://opentelemetry.io/docs/specs/otlp/#otlphttp
- Azure Database for PostgreSQL Flexible Server supports high availability with primary and standby replicas: https://learn.microsoft.com/en-us/azure/postgresql/high-availability/concepts-high-availability
- Azure Database for PostgreSQL Flexible Server supports fully managed backups and point-in-time recovery: https://learn.microsoft.com/en-us/azure/reliability/reliability-database-postgresql#backup-and-restore
- Terraform `azurerm` backend stores state as a blob in an Azure Storage container and supports state locking and consistency checking with Azure Blob Storage native capabilities: https://developer.hashicorp.com/terraform/language/backend/azurerm
- Microsoft documents creating an Azure Storage account and container before using Azure Storage as a Terraform backend, and notes that Terraform state can contain secrets and must be secured: https://learn.microsoft.com/en-us/azure/developer/terraform/store-state-in-azure-storage
- Microsoft describes Azure Verified Modules as reusable Infrastructure as Code modules for Azure, available for Bicep and Terraform, developed and maintained for consistency and best-practice alignment: https://learn.microsoft.com/en-us/community/content/azure-verified-modules
- GitHub Actions OIDC lets workflows request short-lived cloud access tokens instead of storing long-lived cloud credentials in GitHub secrets: https://docs.github.com/en/actions/concepts/security/openid-connect
- GitHub recommends least-privilege workflow credentials and limiting `GITHUB_TOKEN` permissions to the minimum required: https://docs.github.com/en/actions/reference/security/secure-use
- Azure DNS delegation uses NS records in the parent zone to delegate a child zone to Azure DNS authoritative name servers: https://learn.microsoft.com/en-us/azure/dns/dns-domain-delegation
- Cloudflare supports subdomain delegation by creating NS records for a subdomain in the parent zone: https://developers.cloudflare.com/dns/zone-setups/subdomain-setup/setup/
- Azure Front Door managed certificates use DNS TXT validation for custom domains: https://learn.microsoft.com/en-us/azure/frontdoor/domain
- Azure Front Door Premium supports Private Link origins: https://learn.microsoft.com/en-us/azure/frontdoor/private-link
- Azure Container Apps ingress with `external` is reachable by FQDN, which is why production origins need private isolation: https://learn.microsoft.com/en-us/azure/container-apps/ingress-overview
- Azure Container Apps can be exposed through Azure Front Door Premium with public network access disabled: https://learn.microsoft.com/en-us/azure/container-apps/front-door-custom-virtual-network-private-link
- GitHub Actions supports manually triggered workflows with `workflow_dispatch` inputs: https://docs.github.com/actions/using-workflows/workflow-syntax-for-github-actions#onworkflow_dispatch
- GitHub and Microsoft document OIDC authentication from GitHub Actions to Azure without long-lived cloud credentials: https://docs.github.com/actions/deployment/security-hardening-your-deployments/configuring-openid-connect-in-azure and https://learn.microsoft.com/en-us/azure/developer/github/connect-from-azure-openid-connect
- Terraform `destroy` deprovisions objects managed by a Terraform configuration and is commonly suited to temporary infrastructure cleanup rather than long-lived production resources: https://developer.hashicorp.com/terraform/cli/commands/destroy
- Terraform plan supports destroy mode with `terraform plan -destroy`: https://developer.hashicorp.com/terraform/cli/commands/plan
- GitHub environments support required reviewers for deployment jobs: https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments
