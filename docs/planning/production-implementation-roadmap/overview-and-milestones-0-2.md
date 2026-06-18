## Purpose

This document defines the GitHub milestone and issue-shaping contract for the Azure Production MVP implementation backlog.

It turns the implementation-ready production docs into milestone-based vertical slices. It is not a replacement for the PRD, architecture docs, or issue bodies.

## Source Documents

- [Azure Production MVP PRD](../../prd/azure-production-mvp.md)
- [Implementation Readiness Review](../../architecture/implementation-readiness-review.md)
- [Production Codebase Transition](../../architecture/production-codebase-transition.md)
- [Terraform Production Infrastructure](../../architecture/terraform-production-infrastructure.md)
- [Codex Production Ingestion Contract](../../architecture/codex-production-ingestion-contract.md)
- [Product API Contract](../../architecture/product-api-contract.md)
- [Runtime Service Topology](../../architecture/runtime-topology.md)
- [Product Dashboard UX Architecture](../../architecture/product-dashboard-ux.md)
- [Managed Grafana Dashboards](../../architecture/managed-grafana-dashboards.md)
- [Production Operations](../../operations/production-operations.md)
- [GitHub Issue Transition Audit](../github-issue-transition-audit.md)

## Decision

Create GitHub issues under milestones.

Milestones should represent implementation checkpoints. Issues should be vertical slices that produce a reviewable outcome, not component-only task buckets.

GitHub sub-issues may be used only when a parent issue is too large but still has one coherent outcome.

Parent tracking issues are allowed at milestone level when they hold milestone acceptance criteria and link one level of child issues. Do not create deep sub-issue nesting.

Before production issue creation, audit existing issues and separate historical local-first work from the production backlog. Existing local-first issues should not remain open as active production work. Use the [GitHub Issue Transition Audit](../github-issue-transition-audit.md) as the tracker cleanup source.

## Milestones

Milestone title prefixes define the canonical roadmap order. GitHub numeric milestone identifiers are platform identifiers only and may not match roadmap order.

### 0. Production Skeleton And Guardrails

Goal: make the repository safe and shaped for production implementation.

Parent tracking issue: [#17](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/17).

GitHub milestone: [0. Production Skeleton And Guardrails](https://github.com/collaborationwithothers/ai-agent-token-observability/milestone/1).

Includes:

- Production solution skeleton.
- Product API, Product Ingestion Endpoint, Product Jobs, and React Product Dashboard placeholders.
- Removal or quarantine path for local-first AppHost, Blazor Dashboard, direct file import, and Copilot JSONL tests.
- CI updates that keep the repository buildable during transition.
- Public repository GitHub Actions guardrail validator and unsafe workflow fixtures.
- Terraform stage skeletons with backend disabled validation.
- Terraform workspace validation contract.
- Manual-only workflow templates with repository, actor, branch, environment, region, derived workspace, OIDC, permissions, and protected environment gates for normal deploys. Customer organization slug defaults to `internal` and remains overrideable. Retained public DNS is guarded by a fixed owner workspace, protected apply environment, same-run saved plan artifact, delegation output, and public NS verification.
- Initial issue labels and milestones.

Exit criteria:

- Production skeleton builds.
- Local-only mode is absent from the production path.
- Unsafe deployment workflow fixtures fail validation.
- Terraform skeleton validates with backend disabled.
- README and AGENTS guidance match the production direction.

Issue structure:

- Create the Milestone 0 parent tracking issue before creating child issues.
- Create one parent tracking issue for Milestone 0.
- Create one level of child issues under the parent.
- Each child issue must remain independently reviewable and keep the repository buildable.
- Do not create grandchildren or deeper sub-issue nesting for Milestone 0.
- The parent tracking issue is tracking-only and must not own code changes.
- Code, Terraform, workflow, and test changes must be linked to child issues.
- The parent tracking issue owns the milestone goal, child issue list, overall exit criteria, cross-cutting guardrails, and source document links.
- Pull requests should reference child issues rather than the parent tracking issue unless the PR only updates tracking metadata.

Parent issue "Do Not Implement Here" section:

- Do not make code changes under the parent tracking issue.
- Do not make Terraform implementation changes under the parent tracking issue.
- Do not make workflow YAML changes under the parent tracking issue.
- Do not scaffold production services under the parent tracking issue.
- Do not scaffold the React Product Dashboard under the parent tracking issue.
- Do not delete, move, or quarantine local-first projects under the parent tracking issue.
- Do not use the parent tracking issue for mixed PRs that span unrelated child issue outcomes.

Milestone 0 child issues:

| Issue | Child issue | Outcome |
| --- | --- | --- |
| [#18](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/18) | Production solution skeleton | New production project structure builds without relying on local-only AppHost |
| [#19](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/19) | Token Observability runtime skeletons | Product API, Product Ingestion Endpoint, Product Jobs, and React Product Dashboard placeholders exist with health-oriented smoke tests |
| [#20](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/20) | Local-first quarantine/removal | AppHost, Blazor Dashboard, direct file import, and Copilot JSONL tests are removed from the production path or explicitly quarantined |
| [#21](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/21) | Terraform stage skeleton | Required Terraform stage directories validate with backend disabled |
| [#22](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/22) | Workflow guardrail validator | Unsafe public-repository workflow fixtures fail validation |
| [#23](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/23) | Manual-only workflow templates | Deployment-capable workflow templates use `workflow_dispatch` and required pre-login gates |
| [#24](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/24) | Labels and milestones setup | Initial labels and GitHub milestones are created for the production backlog |

### 1. Tenant-Aware Product Foundation

Goal: establish product metadata, authorization, and tenant-scoped API foundations.

Parent tracking issue: [#32](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/32).

GitHub milestone: [1. Tenant-Aware Product Foundation](https://github.com/collaborationwithothers/ai-agent-token-observability/milestone/2).

Includes:

- Customer Organization model.
- Identity tenant, product user, and Product Role Mapping foundations.
- Product API route versioning and authorization middleware.
- Health and readiness endpoints.
- PostgreSQL production schema baseline.
- Governance Audit Event write path.
- Scoped Ingestion Credential metadata model.
- Tenant-scope rejection tests.

Exit criteria:

- Product API can resolve tenant and authorization context for protected routes.
- Health and readiness behavior is implemented.
- Product metadata migrations are tenant-aware.
- Governance Audit Events are first-class records.

Milestone 1 child issues:

| Issue | Child issue | Outcome |
| --- | --- | --- |
| [#39](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/39) | Customer organization and product metadata schema | Tenant-aware Customer Organization, Identity Tenant, and Product User metadata schema exists |
| [#40](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/40) | Product identity and role mapping foundation | Entra user and group claims map to tenant-scoped Product Role Mapping |
| [#41](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/41) | Product API versioning, health, and readiness | Versioned Product API route foundation and probe endpoints are implemented |
| [#42](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/42) | Tenant authorization context middleware | Protected routes resolve tenant context and fail closed on invalid scope |
| [#43](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/43) | Governance audit event write path | Tenant-scoped governance audit events can be written by administrative paths |
| [#44](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/44) | Scoped ingestion credential metadata model | Tenant-scoped ingestion credential metadata exists before Codex ingestion implementation |
| [#45](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/45) | Tenant-scope rejection tests | Tenant, role, and cross-tenant rejection paths are covered by tests |

### 2. Codex Ingestion Baseline

Goal: accept authenticated Codex telemetry into the product source of record.

Parent tracking issue: [#33](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/33).

GitHub milestone: [2. Codex Ingestion Baseline](https://github.com/collaborationwithothers/ai-agent-token-observability/milestone/3).

Includes:

- Codex setup profile and manual telemetry configuration docs.
- Scoped Ingestion Credential creation, validation, rotation, and revocation path.
- OTLP/HTTP endpoint for Codex Agent Telemetry Signals.
- Ingestion rejection records.
- Normalized telemetry envelope and session records.
- Null-versus-zero token metric semantics.
- Metrics export to Azure Monitor workspace or managed Prometheus.
- Trace, log, and event routing to Application Insights or Log Analytics.

Exit criteria:

- Valid Codex telemetry is accepted and normalized.
- Invalid credential, tenant, schema, or policy context is rejected with auditable metadata.
- Accepted telemetry creates tenant-aware session records and aggregate metrics.

Milestone 2 dependency rule:

Milestone 2 implementation issues must not start until their listed Milestone 1 dependencies are complete.

Milestone 2 child issues:

| Issue | Child issue | Outcome | Milestone 1 dependencies |
| --- | --- | --- | --- |
| [#46](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/46) | Codex setup profile and manual telemetry configuration docs | Codex manual telemetry setup profile is documented for first release | #39, #41, #44 |
| [#47](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/47) | Scoped ingestion credential lifecycle | Credential create, validate, rotate, disable, and revoke behavior exists | #40, #42, #43, #44, #45 |
| [#48](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/48) | Codex OTLP HTTP ingestion endpoint | Product Ingestion Endpoint accepts authenticated Codex telemetry | #39, #41, #42, #43, #44, #45 |
| [#49](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/49) | Ingestion rejection records and audit semantics | Rejected telemetry creates tenant-safe rejection records and audit metadata | #42, #43, #44, #45 |
| [#50](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/50) | Normalized Codex telemetry envelope and session records | Accepted telemetry creates normalized envelope and session records | #39, #42, #43, #44, #45 |
| [#51](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/51) | Codex token metric states and null semantics | Token metric quality and null-versus-zero semantics are preserved | #39, #42, #45 |
| [#52](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/52) | Aggregate metrics export from accepted Codex sessions | Accepted Codex sessions emit tenant-scoped aggregate metrics | #39, #42, #43 |
| [#53](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/53) | Trace log and event routing for Codex ingestion | Ingestion traces, logs, and events route safely to production observability | #41, #42, #43, #45 |
