# Production Implementation Roadmap

## Purpose

This document defines the GitHub milestone and issue-shaping contract for the Azure Production MVP implementation backlog.

It turns the implementation-ready production docs into milestone-based vertical slices. It is not a replacement for the PRD, architecture docs, or issue bodies.

## Source Documents

- [Azure Production MVP PRD](../prd/azure-production-mvp.md)
- [Implementation Readiness Review](../architecture/implementation-readiness-review.md)
- [Production Codebase Transition](../architecture/production-codebase-transition.md)
- [Terraform Production Infrastructure](../architecture/terraform-production-infrastructure.md)
- [Codex Production Ingestion Contract](../architecture/codex-production-ingestion-contract.md)
- [Product API Contract](../architecture/product-api-contract.md)
- [Runtime Service Topology](../architecture/runtime-topology.md)
- [Product Dashboard UX Architecture](../architecture/product-dashboard-ux.md)
- [Managed Grafana Dashboards](../architecture/managed-grafana-dashboards.md)
- [Production Operations](../operations/production-operations.md)
- [GitHub Issue Transition Audit](github-issue-transition-audit.md)

## Decision

Create GitHub issues under milestones.

Milestones should represent implementation checkpoints. Issues should be vertical slices that produce a reviewable outcome, not component-only task buckets.

GitHub sub-issues may be used only when a parent issue is too large but still has one coherent outcome.

Parent tracking issues are allowed at milestone level when they hold milestone acceptance criteria and link one level of child issues. Do not create deep sub-issue nesting.

Before production issue creation, audit existing issues and separate historical local-first work from the production backlog. Existing local-first issues should not remain open as active production work. Use the [GitHub Issue Transition Audit](github-issue-transition-audit.md) as the tracker cleanup source.

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
- Manual-only workflow templates with repository, actor, branch, environment, region, workspace, OIDC, permissions, and confirmation gates.
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
| [#19](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/19) | Product runtime skeletons | Product API, Product Ingestion Endpoint, Product Jobs, and React Product Dashboard placeholders exist with health-oriented smoke tests |
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

### 3. Azure Runtime And Edge Proof

Goal: deploy the production runtime shape and prove public ingress cannot bypass the edge.

Parent tracking issue: [#34](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/34).

GitHub milestone: [3. Azure Runtime And Edge Proof](https://github.com/collaborationwithothers/ai-agent-token-observability/milestone/7).

Includes:

- Container Apps environment and three long-running Container Apps.
- Shared jobs image and explicit job command wiring.
- Azure Front Door Premium WAF.
- Azure Front Door managed certificates for `app`, `api`, and `ingest`.
- Azure Front Door Private Link origins to Azure Container Apps.
- Public network access disabled for ACA origins in production.
- Azure DNS records under `tokenobs.consultwithcloud.com`.
- Edge Origin Validation workflow or proof procedure.
- Infrastructure deletion workflow for disposable stages.

Exit criteria:

- Front Door hostnames work.
- Direct public generated ACA FQDN access does not reach the app in `pp` or `pd`.
- Auth callbacks use public Front Door hostnames.
- Guarded deletion workflow cannot delete retained shared resources.

Milestone 3 dependency rule:

Milestone 3 implementation issues must not start until their listed Milestone 0, 1, and 2 dependencies are complete.

Milestone 3 child issues:

| Issue | Child issue | Outcome | Dependencies |
| --- | --- | --- | --- |
| [#54](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/54) | Container Apps runtime environment and long-running services | ACA environment and Product Dashboard, Product API, and Product Ingestion Endpoint apps are provisioned | #19, #21, #22, #23, #41, #48 |
| [#55](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/55) | Shared jobs image and explicit Container Apps job commands | ACA jobs runtime shape uses one shared jobs image with explicit job commands | #19, #21, #22, #23, #39, #43 |
| [#56](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/56) | Azure Front Door Premium WAF routes and rate limits | Front Door Premium WAF routes public traffic to product endpoints | #21, #22, #23, #41, #48, #54 |
| [#57](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/57) | Front Door managed certificates and product hostnames | Front Door managed certificates bind product hostnames for first release | #21, #23, #54, #56 |
| [#58](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/58) | Front Door Private Link origins and ACA public access lock down | Front Door reaches ACA through Private Link and direct ACA public bypass fails | #21, #22, #23, #41, #54, #56 |
| [#59](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/59) | Azure DNS records for delegated tokenobs subdomain | Azure DNS records under `tokenobs.consultwithcloud.com` support app, api, and ingest hostnames | #21, #23, #56, #57 |
| [#60](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/60) | Edge origin validation workflow | Manual guarded workflow proves Front Door works and direct ACA origin bypass fails | #22, #23, #41, #54, #56, #58, #59 |
| [#61](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/61) | Guarded infrastructure deletion workflow for disposable stages | Manual guarded deletion workflow deletes disposable resources while retaining shared resources | #21, #22, #23, #54, #56, #59 |

### 4. Observability And Grafana

Goal: make aggregate product and platform metrics visible without exposing session details or ranking individuals.

Parent tracking issue: [#35](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/35).

GitHub milestone: [4. Observability And Grafana](https://github.com/collaborationwithothers/ai-agent-token-observability/milestone/8).

Milestone 4 child issues:

Dependency rule: Milestone 4 implementation issues must not start until their listed metric, infrastructure, identity, and Product Dashboard dependencies are complete.

| Issue | Workstream | Required outcome | Depends on |
| --- | --- | --- | --- |
| [#26](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/26) | Aggregate token timeline and model operations metrics | Product emits aggregate metrics needed by Grafana without session, developer, or raw content labels | #39, #48, #50, #51, #52 |
| [#62](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/62) | Azure Managed Grafana workspace and data source wiring | Managed Grafana is provisioned and wired to approved aggregate metrics data sources only | #21, #22, #23, #26, #52, #54 |
| [#63](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/63) | Grafana provider authentication proof gate | Terraform dashboard management proves Entra or workload identity first, with service-account-token fallback only if proof fails | #21, #23, #24, #62 |
| [#64](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/64) | Repo-versioned Grafana dashboard JSON deployment | First-release dashboard JSON artifacts deploy through Terraform from repo source | #26, #52, #62, #63 |
| [#65](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/65) | Grafana RBAC with environment-scoped Entra groups | Grafana built-in roles map to environment-scoped Entra groups and remain separate from Product API authorization | #40, #42, #62 |
| [#66](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/66) | Product Dashboard Grafana link allowlist and authorization | Grafana links pre-filter Product Dashboard views without bypassing Product API authorization or leaking forbidden identifiers | #25, #41, #42, #62, #64 |

Includes:

- Aggregate Metrics Contract implementation.
- Azure Monitor workspace or managed Prometheus integration.
- Azure Managed Grafana workspace.
- Grafana Provider Authentication Proof Gate.
- Service-account-token fallback only if Entra OIDC provider proof fails.
- Repo-versioned dashboard JSON artifacts.
- Executive Cost Overview.
- Harness And Model Operations.
- Cache And Hotspot Trends.
- Ingestion And Platform Health.
- Grafana RBAC with environment-scoped Entra group object IDs.
- Product Dashboard link allowlist for Grafana links.

Exit criteria:

- Dashboards deploy through Terraform.
- Production dashboard changes are not manual UI-only state.
- Grafana does not show raw session, content, or individual ranking data.
- Product Dashboard authorizes all linked drill-down views.

### 5. Content, Hotspots, Recommendations, And Pricing

Goal: produce policy-safe insight from accepted sessions.

Parent tracking issue: [#36](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/36).

GitHub milestone: [5. Content, Hotspots, Recommendations, And Pricing](https://github.com/collaborationwithothers/ai-agent-token-observability/milestone/6).

Milestone 5 child issues:

Dependency rule: Milestone 5 implementation issues must not start until their listed tenant, ingestion, identity, audit, metrics, infrastructure, and observability dependencies are complete.

| Issue | Workstream | Required outcome | Depends on |
| --- | --- | --- | --- |
| [#27](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/27) | Pricing basis and model cost visibility | Versioned pricing basis, customer overrides, and cost-state semantics exist without guessed cost | #39, #42, #43, #50, #51 |
| [#28](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/28) | Token hotspots and prompt cache diagnostics | Token Hotspots and Prompt Cache Breakage records preserve evidence state, attribution type, and confidence | #50, #51, #52, #53 |
| [#29](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/29) | Recommendation engine evidence and review workflow | Deterministic and LLM-assisted recommendations preserve evidence, policy, validation, and review state | #28, #43, #50, #51 |
| [#67](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/67) | Content capture policy and Codex candidate extraction | Content Capture Mode stays disabled by default and Codex candidates are extracted only when policy allows | #39, #40, #41, #42, #43, #46, #48, #50 |
| [#68](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/68) | Inline pre-storage redaction pipeline and failure gate | Inline redaction runs before storage and redaction_failed or review_required stores metadata only | #43, #48, #49, #50, #67 |
| [#69](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/69) | Captured content storage, retention, and review workflow | Redacted content blobs, metadata references, retention defaults, and privileged review actions follow policy | #21, #40, #42, #43, #54, #67, #68 |
| [#70](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/70) | Recommendation model policy and LLM validation gates | LLM recommendations use immutable evidence packets, versioned prompts, structured outputs, and rejection gates | #29, #40, #42, #43, #50, #51, #68, #69 |
| [#71](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/71) | Automated pricing seed refresh and pricing update review | Provider price seed creates candidates that require review before affecting cost, budgets, or trends | #27, #39, #42, #43, #50, #51, #52 |
| [#72](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/72) | Non-punitive budget alert policies and evaluation | Budget alerts use aggregate metrics and approved pricing without individual ranking or blame language | #27, #39, #40, #42, #43, #52, #62, #64, #71 |

Includes:

- Content Capture Policy disabled by default.
- Pre-storage redaction pipeline.
- Redaction Failure Gate.
- Captured Content Blob storage only after policy approval and redaction success.
- Token Hotspot detection.
- Prompt Cache Breakage evidence states.
- Deterministic recommendations.
- LLM-Assisted Recommendations with evidence packet, structured output, validation gates, and candidate review.
- Pricing basis, automated provider price seeding, Pricing Update Review, and customer overrides.
- Non-Punitive Budget Alerts.

Exit criteria:

- Raw content is never durably stored before redaction.
- Redaction failures store metadata only.
- LLM-inferred hotspots remain candidates until product validation.
- Recommendations and pricing preserve evidence, policy, and version metadata.
- Budget alerts do not rank or blame individual developers.

### 6. Product Dashboard And Session Investigation

Goal: give authorized users the production investigation and coaching surface.

Parent tracking issue: [#37](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/37).

GitHub milestone: [6. Product Dashboard And Session Investigation](https://github.com/collaborationwithothers/ai-agent-token-observability/milestone/4).

Milestone 6 child issues:

Dependency rule: Milestone 6 implementation issues must not start until their listed dashboard shell, Product API, tenant authorization, ingestion, content, recommendation, pricing, budget, audit, and Grafana-link dependencies are complete.

| Issue | Workstream | Required outcome | Depends on |
| --- | --- | --- | --- |
| [#25](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/25) | Product Dashboard overview and investigation shell | Production React dashboard route foundation and overview shell exists without local-first UI carry-forward | #19, #40, #41, #42 |
| [#30](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/30) | Session evidence and investigation drill-down | Authorized users can open session investigation with summary, evidence, hotspot, cache, and recommendation entry points | #28, #29, #42, #50, #51 |
| [#73](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/73) | Dashboard app shell, bootstrap, and route authorization | React/Vite dashboard bootstraps from `/api/v1/me`, resolves tenant context, and gates routes by product role and scope | #19, #25, #40, #41, #42, #54 |
| [#74](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/74) | Overview route aggregate summaries and Grafana filter entry | `/overview` displays authorized aggregate summaries and treats Grafana filters as untrusted navigation input | #25, #26, #41, #42, #52, #64, #66, #73 |
| [#75](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/75) | Session search route and non-punitive filters | `/sessions` supports role-scoped search, safe filters, cursor pagination, and no people-ranking sort modes | #25, #30, #41, #42, #50, #51, #66, #73, #74 |
| [#76](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/76) | Session investigation detail panels and evidence states | `/sessions/:sessionId` displays timeline, metrics, hotspots, cache diagnostics, content states, recommendations, and audit context | #28, #29, #30, #41, #42, #50, #51, #67, #68, #69, #70, #73, #75 |
| [#77](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/77) | Content review queue and reviewer decision UI | `/content-review` supports authorized review metadata, retry, discard, and bounded excerpt decisions without raw failed content | #40, #42, #43, #67, #68, #69, #73 |
| [#78](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/78) | Recommendation review and regeneration UI | `/recommendations` shows visible recommendation state and asynchronous regeneration without browser-side LLM generation | #28, #29, #42, #43, #69, #70, #73, #76 |
| [#79](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/79) | Pricing and non-punitive budget administration UI | Pricing review and budget policy screens enforce approval, audit, tenant scope, and non-punitive budget behavior | #27, #42, #43, #71, #72, #73 |
| [#80](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/80) | Identity, harness setup, and audit administration UI | Admin routes manage identity, role mappings, setup profiles, credentials, and audit search with one-time secret display | #40, #41, #42, #43, #44, #46, #47, #73 |
| [#81](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/81) | Role visibility and non-punitive dashboard guardrail tests | Cross-route tests prove role visibility, authorization boundaries, Grafana link safety, and no blame-oriented UI | #25, #30, #66, #73, #74, #75, #76, #77, #78, #79, #80 |

Includes:

- React Product Dashboard.
- Product route map.
- `GET /api/v1/me` bootstrap.
- Overview, sessions, session investigation, recommendations, content review, pricing, budget, audit, and administration views.
- Role-based visibility.
- Session timeline, hotspots, cache diagnostics, evidence states, and recommendation review.
- Identity-minimized defaults and non-punitive UX constraints.

Exit criteria:

- Authorized users can investigate sessions through Product Dashboard.
- Unauthorized users cannot access session, content, recommendation, pricing, or audit details.
- Dashboard does not rank individual developers by burn, waste, wrongness, or cost.

### 7. Day-1 Operations And Production Readiness

Goal: prove the first release can be operated safely.

Parent tracking issue: [#38](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/38).

GitHub milestone: [7. Day-1 Operations And Production Readiness](https://github.com/collaborationwithothers/ai-agent-token-observability/milestone/5).

Milestone 7 child issues:

Dependency rule: Milestone 7 implementation issues must not start until their listed runtime, edge, observability, data protection, dashboard, authorization, workflow, and documentation dependencies are complete.

| Issue | Workstream | Required outcome | Depends on |
| --- | --- | --- | --- |
| [#31](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/31) | Production dashboard verification and readiness reconciliation | Product Dashboard and readiness docs are verified against a running production-shaped environment | #73, #74, #75, #76, #77, #78, #79, #80, #81 |
| [#82](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/82) | Container Apps health endpoints and probe configuration | Long-running Container Apps expose safe liveness and readiness behavior and Terraform-managed probes | #41, #54, #55, #58 |
| [#83](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/83) | Day-1 internal SLO metrics and queries | Internal SLOs have measurable SLI queries or metric contracts without customer SLA claims | #26, #52, #54, #62, #82 |
| [#84](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/84) | Azure Monitor alerts and private action groups | Minimum alert set and private action groups are Terraform-managed and sanitized | #21, #23, #52, #54, #56, #58, #60, #62, #82, #83 |
| [#85](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/85) | PostgreSQL non-production restore drill | Non-production PostgreSQL restore drill proves backup recovery without production overwrite | #21, #39, #54, #61, #82 |
| [#86](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/86) | Blob lifecycle validation and retention proof | Captured-content Blob lifecycle behavior is validated in non-production and metadata remains queryable where required | #21, #54, #61, #69, #82 |
| [#87](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/87) | Audit export foundation and sanitized operational evidence | Bounded authorized audit export produces sanitized operational evidence and records export audit events | #40, #42, #43, #80 |
| [#88](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/88) | First-release incident runbooks | Required incident runbooks exist, are linked, and avoid public secrets or blame workflows | #31, #60, #68, #69, #70, #71, #82, #84, #85 |
| [#89](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/89) | Production smoke tests and edge readiness gate | Public hostnames, Front Door path, Private Link origin, ACA bypass failure, and auth callbacks are validated safely | #56, #57, #58, #59, #60, #73, #82 |
| [#90](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/90) | Final production readiness checklist and release gate | Final release gate maps all MVP readiness criteria to evidence and blocks readiness on failed checks | #24, #31, #45, #60, #61, #81, #82, #83, #84, #85, #86, #87, #88, #89 |

Includes:

- Container Apps liveness and readiness probes.
- Day-1 internal SLO queries or metrics.
- Azure Monitor alert rules.
- Private action groups.
- PostgreSQL non-production restore drill.
- Blob lifecycle validation.
- Required incident runbooks.
- Audit export foundation.
- Final production readiness checklist.

Exit criteria:

- Day-1 alerts are deployed and private.
- Restore and lifecycle validation pass in non-production.
- Required runbooks exist.
- Production incidents do not create public GitHub issues automatically.

## Issue Shape

Each implementation issue must include:

- User outcome.
- Product surface.
- Data model entities touched.
- Authorization scope.
- Telemetry, content, or recommendation evidence involved.
- Azure resources involved.
- Tests and verification.
- Non-punitive and privacy guardrails.
- Source documents.
- Acceptance criteria.

Avoid:

- Component-only issues with no user or operational outcome.
- Issues that silently revive local-only mode.
- Issues that mix unrelated milestones.
- Issues that require production secrets or private operational details in public GitHub comments.

## Label Model

Recommended labels:

| Label | Purpose |
| --- | --- |
| `type:feature` | Product behavior |
| `type:infra` | Terraform, Azure, workflow, or deployment behavior |
| `type:docs` | Documentation only |
| `type:test` | Test or validation-only work |
| `area:api` | Product API |
| `area:ingestion` | Product Ingestion Endpoint |
| `area:jobs` | ACA Jobs and background work |
| `area:dashboard` | React Product Dashboard |
| `area:grafana` | Managed Grafana dashboards and provisioning |
| `area:terraform` | Terraform stages, modules, and workflow validation |
| `area:ops` | Day-1 operations, alerts, runbooks, validation drills |
| `area:data` | Product metadata, PostgreSQL, Blob Storage |
| `area:identity` | Entra, Product Role Mapping, Scoped Ingestion Credential |
| `guardrail:privacy` | Content, redaction, retention, and sensitive data constraints |
| `guardrail:non-punitive` | No ranking, no blame, coaching-oriented UX |
| `guardrail:public-repo` | Public repository workflow and issue safety |
| `guardrail:tenant` | Customer Organization and tenant isolation constraints |
| `status:blocked` | Blocked by external dependency or decision |
| `status:proof-needed` | Requires a documented implementation proof |

## Verified Platform Facts

- GitHub Issues can be associated with milestones, issue types, projects, assignees, and labels: https://docs.github.com/en/issues/tracking-your-work-with-issues/using-issues/creating-an-issue
- GitHub milestones track progress on groups of issues or pull requests in a repository: https://docs.github.com/issues/using-labels-and-milestones-to-track-work/about-milestones
- GitHub labels categorize issues and pull requests in a repository: https://docs.github.com/en/issues/using-labels-and-milestones-to-track-work/managing-labels
- GitHub sub-issues can break larger pieces of work into related tasks: https://docs.github.com/en/issues/tracking-your-work-with-issues/using-issues/adding-sub-issues
