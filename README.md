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
- [Codex Manual Telemetry Setup](./docs/operations/codex-manual-telemetry-setup.md)
- [Production Codebase Transition](./docs/architecture/production-codebase-transition.md)
- [Implementation Readiness Review](./docs/architecture/implementation-readiness-review.md)
- [Production Implementation Roadmap](./docs/planning/production-implementation-roadmap.md)

Superseded references:

- [Local-First MVP PRD](./docs/archive/local-first/local-first-mvp.md)
- [ADR 0001](./docs/archive/local-first/0001-use-dotnet-aspire-and-blazor-for-local-first-mvp.md)
- [Copilot OTel Field Mapping](./docs/archive/future-adapters/copilot-otel-field-mapping.md)

The superseded docs are historical or future-adapter reference material. They must not drive production MVP implementation issues.

## Current Codebase State

The active source tree is production-shaped.

- The root solution and production solution reference the Token Observability production skeleton.
- The Aspire AppHost, Blazor Local Dashboard, local dashboard API, direct file import worker, local storage/import services, and Copilot JSONL tests have been removed from the active code path.
- Superseded local-first documents are archived as historical reference only.

Do not reintroduce local-only mode as a product feature.

## Production Validation

The root solution is production-shaped. The explicit production solution remains as a stable validation entrypoint for production-only commands.

```bash
dotnet restore AiAgentTokenObservability.slnx
dotnet build AiAgentTokenObservability.slnx --no-restore
dotnet test AiAgentTokenObservability.slnx --no-restore
dotnet restore AiAgentTokenObservability.Production.slnx
dotnet build AiAgentTokenObservability.Production.slnx --no-restore
dotnet test tests/TokenObservability.Skeleton.Tests/TokenObservability.Skeleton.Tests.csproj --no-restore
dotnet test tests/TokenObservability.Runtime.Tests/TokenObservability.Runtime.Tests.csproj --no-restore
npm --prefix web/token-observability-dashboard ci
npm --prefix web/token-observability-dashboard run build
```

These commands validate the production project structure and runtime placeholders for the Azure Production MVP. Full product behavior is added by later implementation issues.

## Infrastructure Direction

Terraform is the production infrastructure path.

- Remote state is Azure Blob Storage and is created manually before Terraform stages run.
- Workspaces use `{environment}_{azureRegion}_{customerOrganizationSlug}`.
- Azure Verified Modules are preferred where suitable.
- Deployment-capable GitHub workflows are manual-only and guarded for a public repository.
- Infrastructure deletion uses guarded Terraform destroy plans and retains shared resources.

See [Terraform Production Infrastructure](./docs/architecture/terraform-production-infrastructure.md) and [Infrastructure Deletion Workflow](./docs/operations/infrastructure-deletion.md).
