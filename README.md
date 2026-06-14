# ai-agent-token-observability

AI Agent Token Observability is being reworked into an Azure Production MVP for AI coding-agent token burn, cost visibility, hotspot evidence, and non-punitive workflow optimization.

The current production direction supersedes the previous local-first MVP.

## Production Direction

First release:

- Tenant-aware Single-Enterprise Release.
- Codex CLI as the first production harness.
- Manual harness telemetry setup.
- Authenticated OTLP/HTTP ingestion through the Product Ingestion Endpoint.
- Product services hosted on Azure Container Apps.
- Public ingress through Azure Front Door Premium WAF.
- Azure Front Door managed certificates for explicit product hostnames.
- Azure Front Door Private Link to Azure Container Apps origins.
- Azure Managed Grafana for aggregate dashboards.
- Product Dashboard for session investigation, recommendations, content review, and governance.
- Terraform with Azure Blob Storage remote state.

Target state:

- Vendor-operated multi-tenant SaaS.
- Additional harnesses such as VS Code Copilot and Claude Code after Codex production ingestion is proven.
- Optional future BYOC wildcard certificate workflow only if the managed-certificate decision is reopened.

## Documentation Entry Points

Read these before creating implementation issues or changing behavior:

- [CONTEXT.md](./CONTEXT.md)
- [Azure Production MVP PRD](./docs/prd/azure-production-mvp.md)
- [Production Target State Spec](./docs/specs/production-target-state.md)
- [Azure Production Architecture](./docs/architecture/azure-production-architecture.md)
- [Codex Production Ingestion Contract](./docs/architecture/codex-production-ingestion-contract.md)
- [Production Codebase Transition](./docs/architecture/production-codebase-transition.md)
- [Implementation Readiness Review](./docs/architecture/implementation-readiness-review.md)
- [Production Implementation Roadmap](./docs/planning/production-implementation-roadmap.md)

Superseded references:

- [Local-First MVP PRD](./docs/prd/local-first-mvp.md)
- [ADR 0001](./docs/adr/0001-use-dotnet-aspire-and-blazor-for-local-first-mvp.md)
- [Copilot OTel Field Mapping](./docs/architecture/copilot-otel-field-mapping.md)

The superseded docs are historical or future-adapter reference material. They must not drive production MVP implementation issues.

## Current Codebase State

The current source tree still contains local-first scaffolding:

- Aspire AppHost.
- Blazor Local Dashboard.
- Dashboard API.
- Direct file import worker.
- Local storage/import services.
- Copilot JSONL tests.

That code is planned to be deleted, replaced, retained, or quarantined according to [Production Codebase Transition](./docs/architecture/production-codebase-transition.md). Do not evolve local-only mode as a product feature.

## Current Validation Commands

Until the production solution skeleton replaces the local-first scaffolding, use the current .NET commands only to keep the existing tree buildable during transition:

```bash
dotnet restore AiAgentTokenObservability.slnx
dotnet build AiAgentTokenObservability.slnx
dotnet test AiAgentTokenObservability.slnx --no-restore
```

These commands validate the current repository state. They are not production deployment commands.

## Infrastructure Direction

Terraform is the production infrastructure path.

- Remote state is Azure Blob Storage and is created manually before Terraform stages run.
- Workspaces use `{environment}_{azureRegion}_{customerOrganizationSlug}`.
- Azure Verified Modules are preferred where suitable.
- Deployment-capable GitHub workflows are manual-only and guarded for a public repository.
- Infrastructure deletion uses guarded Terraform destroy plans and retains shared resources.

See [Terraform Production Infrastructure](./docs/architecture/terraform-production-infrastructure.md) and [Infrastructure Deletion Workflow](./docs/operations/infrastructure-deletion.md).
