# Production Codebase Transition

## Purpose

This document defines how the current local-first implementation transitions to the Azure Production MVP.

It prevents implementation issues from evolving superseded local-only code in place when the production direction requires different runtime boundaries, frontend technology, ingestion shape, identity, storage, and deployment assumptions.

## Source Documents

- [Azure Production MVP PRD](../prd/azure-production-mvp.md)
- [Azure Production Architecture](./azure-production-architecture.md)
- [Runtime Service Topology](./runtime-topology.md)
- [Product Dashboard UX Architecture](./product-dashboard-ux.md)
- [Codex Production Ingestion Contract](./codex-production-ingestion-contract.md)
- [Production Data Model](./data-model.md)
- [ADR 0002](../adr/0002-replace-local-first-with-azure-production-saas.md)
- [ADR 0003](../adr/0003-use-react-spa-for-production-dashboard.md)

## Decision

The current local-first implementation is superseded scaffolding.

The production implementation must not preserve local-only mode as a supported product path. Existing code can be mined for ideas, tests, naming, or small utilities only when it aligns with production contracts.

Transition categories:

| Category | Meaning |
| --- | --- |
| `delete` | Remove from the production solution because it exists only for local-first behavior |
| `replace` | Build a production implementation with the same broad product purpose but different runtime or contract |
| `retain` | Keep and evolve because it aligns with production contracts |
| `quarantine` | Move out of the active production path as historical reference or future-adapter evidence |

## Current Solution Inventory

The current solution contains these projects:

| Project | Current role | Production disposition | Reason |
| --- | --- | --- | --- |
| `src/AiAgentTokenObservability.AppHost` | .NET Aspire local orchestration for API, worker, dashboard web, and PostgreSQL | `delete` | Local-only orchestration is not a production runtime boundary. Azure Container Apps and Terraform own production hosting |
| `src/AiAgentTokenObservability.Dashboard.Web` | Blazor Local Dashboard | `replace` | ADR 0003 chooses React, TypeScript, and Vite for the production Product Dashboard |
| `src/AiAgentTokenObservability.Dashboard.Api` | Local dashboard API with status, sessions, and insights endpoints | `replace` | Production Product API has tenant-aware authorization, admin routes, session investigation, content review, recommendations, pricing, budgets, and audit contracts |
| `src/AiAgentTokenObservability.Ingestion.Worker` | Direct file import and repo context enrichment worker | `replace` | Production ingestion is authenticated Codex OTLP/HTTP through Product Ingestion Endpoint plus ACA Jobs for bounded background work |
| `src/AiAgentTokenObservability.Storage` | Local import model, in-memory store, PostgreSQL local store, Copilot JSONL import, repo enrichment | `replace` with selective reuse | Production data model uses tenant-aware metadata, scoped credentials, content references, pricing, recommendations, audit events, and separated observability stores |
| `src/AiAgentTokenObservability.Contracts` | Local dashboard response contracts | `replace` with selective reuse | Production API contracts are broader and versioned under `/api/v1`; any compatible DTOs can be copied intentionally |
| `src/AiAgentTokenObservability.ServiceDefaults` | Local .NET service defaults | `retain` only if production-safe | Cross-cutting .NET diagnostics and health setup may be reusable, but Aspire-local assumptions must be removed |
| `tests/AiAgentTokenObservability.Tests` | Local import, dashboard status, and parser behavior tests | `quarantine` then replace | Copilot JSONL tests are historical adapter evidence. Production tests must target Codex ingestion, product API, authorization, jobs, and Terraform guardrails |

## Target Production Shape

Target source layout should be created intentionally during implementation. Suggested shape:

```text
src/
  Product.Api/
  Product.Ingestion/
  Product.Jobs/
  Product.Domain/
  Product.Infrastructure/
  Product.Contracts/
web/
  product-dashboard/
tests/
  Product.Api.Tests/
  Product.Ingestion.Tests/
  Product.Jobs.Tests/
  Product.Domain.Tests/
  Product.Infrastructure.Tests/
  Product.Dashboard.Tests/
```

Rules:

- Names can be adjusted during implementation, but runtime boundaries must match the production docs.
- React Product Dashboard lives outside the .NET Blazor project path.
- Product API and Product Ingestion Endpoint are separate deployable services.
- Jobs are command-based ACA Jobs, not a local direct-import worker.
- Shared domain and infrastructure libraries must not recreate local-only mode.
- Test projects should map to production contracts rather than local-first fixtures.

## Deletion Plan

First production transition slice:

1. Create production solution structure and project names.
2. Add Product API health and readiness endpoints.
3. Add Product Ingestion Endpoint skeleton for authenticated OTLP/HTTP.
4. Add Product Jobs skeleton with explicit commands.
5. Add React Product Dashboard skeleton.
6. Add production contract tests for health, readiness, API route versioning, and ingestion authentication failure.
7. Remove `AppHost` from the production solution.
8. Remove Blazor Dashboard Web from the production solution.
9. Remove direct file import worker from the production solution.
10. Move Copilot JSONL fixture/parser tests to a historical or future-adapter test area if they are retained at all.

Do not delete all local-first files in the same issue that introduces production skeletons unless the issue can still be reviewed safely. Prefer small vertical slices that leave the repository buildable after each slice.

## Quarantine Rules

Historical or future-adapter material can be retained only when clearly marked.

Allowed quarantine examples:

- Copilot OTel field mapping as Phase 0 evidence for a future Copilot adapter.
- Copilot JSONL parser fixtures if moved under a future adapter or historical test namespace.
- Local demo runbooks marked superseded or presentation-only.

Forbidden quarantine examples:

- A runnable local-only product mode.
- Direct file import presented as a production ingestion path.
- Blazor dashboard code left as the production dashboard.
- Aspire AppHost left as the production orchestration path.

## Migration Rules

- Do not mutate local-first tables into production tables in place.
- Introduce production schema objects that match [data-model.md](./data-model.md).
- Treat old local import data as disposable development data unless a migration requirement is explicitly added later.
- Product ingestion must authenticate by Scoped Ingestion Credential.
- Harness-emitted identity must remain evidence, not authorization authority.
- Content capture must follow the pre-storage redaction and redaction failure gates.
- Direct-to-monitor-only ingestion must not be accepted as the product source of record.

## Test Transition

Production tests must cover:

- Product API health and readiness behavior.
- Product API authorization and tenant-scope rejection.
- Product Ingestion Endpoint authentication rejection.
- Codex production ingestion envelope validation.
- Null-versus-zero token metric semantics.
- Redaction failure gate.
- Recommendation evidence and LLM authority boundaries.
- Non-punitive dashboard and aggregate metric constraints.
- Terraform workflow guardrails.
- Edge-origin validation command or workflow behavior.

Historical tests may remain only if:

- They are outside the production test path.
- Their names say Copilot, historical, fixture, or future adapter clearly.
- They do not block production refactoring with local-first assumptions.

## Implementation Acceptance Criteria

- The production solution does not depend on `AiAgentTokenObservability.AppHost`.
- The production dashboard path is React, TypeScript, and Vite.
- Product API, Product Ingestion Endpoint, and Product Jobs are separate production runtime boundaries.
- Direct file import is absent from the production ingestion path.
- Local-only mode is not documented as supported.
- Copilot JSONL import tests are either removed or quarantined as future-adapter evidence.
- Production tests cover Codex-first ingestion and Product API contracts before local-first tests are removed from active CI.
- The repository remains buildable after each transition slice.

## Verified Repository Facts

- `AiAgentTokenObservability.slnx` currently lists `AppHost`, `Contracts`, `Dashboard.Api`, `Dashboard.Web`, `Ingestion.Worker`, `ServiceDefaults`, `Storage`, and one test project.
- `src/AiAgentTokenObservability.AppHost/AiAgentTokenObservability.AppHost.csproj` uses the Aspire AppHost SDK.
- `src/AiAgentTokenObservability.Dashboard.Web/AiAgentTokenObservability.Dashboard.Web.csproj` is the current Blazor dashboard project.
- `src/AiAgentTokenObservability.Ingestion.Worker/ImportCommandOptions.cs` contains `DirectFileImport` configuration.
- `tests/AiAgentTokenObservability.Tests/CopilotJsonlImportTests.cs` contains current Copilot JSONL import coverage.
