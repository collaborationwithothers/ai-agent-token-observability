# Azure Production MVP PRD

## Purpose

Build the first production-hosted Azure release of the AI Agent Token Observability platform. The Azure Production MVP proves the production architecture with Codex CLI telemetry, tenant-aware ingestion, secure storage, aggregate Grafana visualization, session investigation, evidence-backed hotspots, and recommendation workflows.

This PRD implements a narrow slice of the [Production Target State Spec](../specs/production-target-state.md). It replaces the local-first MVP direction. Local execution is developer convenience only and is not a supported product mode.

## Product Goal

Help engineering teams reduce AI coding-agent token burn and improve agentic coding workflows without creating a blame or surveillance product.

The MVP must show:

- What token burn occurred.
- Which sessions, models, tools, workflows, or cache behavior likely drove it.
- What evidence supports each hotspot.
- What recommendation should improve efficiency.
- What cannot be determined because telemetry, cache evidence, pricing, or content permission is unavailable.

## Users

- `PlatformAdmin`: configures the Customer Organization, Codex setup profile, Product Role Mapping, Content Capture Policy, pricing, retention, and deployment settings.
- `SecurityReviewer`: reviews redaction failures and sensitive captured evidence where policy allows.
- `EngineeringLead`: views assigned team and repository metrics, sessions, hotspots, recommendations, and non-punitive coaching.
- `Developer`: configures Codex CLI manually, views their own sessions, and uses Optimization Coaching.
- `ReadOnlyViewer`: views approved aggregate dashboards without sensitive session content or individual coaching by default.

## MVP Scope

### In Scope

- Single Customer Organization, implemented as a tenant-aware Single-Enterprise Release.
- Codex CLI as the Production MVP Harness.
- Manual Harness Telemetry Setup for Codex CLI.
- Scoped Ingestion Credentials per developer and Codex setup profile.
- Product Ingestion Endpoint accepting Codex Agent Telemetry Signals over OTLP.
- Codex CLI telemetry following the versioned Production Ingestion Contract.
- Ingestion of OpenTelemetry metrics, traces, and logs or events where Codex emits them.
- Product metadata in Azure Database for PostgreSQL Flexible Server.
- Aggregate metrics routed to Azure Monitor workspace or managed Prometheus for Azure Managed Grafana.
- Trace, log, and event data routed to Application Insights or Log Analytics for investigation support.
- Content Capture Mode for Codex only, controlled by Content Capture Policy and disabled by default.
- Pre-Storage Content Redaction Pipeline before any Captured Content Blob is stored.
- Redaction Failure Gate that stores metadata only when confidence is insufficient.
- Azure Blob Storage as the Content Capture Store for policy-approved captured content.
- Session Investigation View in the Product Dashboard.
- Managed Grafana Surface for aggregate token burn, cost, model, harness, and high-level hotspot views.
- Product Dashboard for session drill-down, hotspot evidence, recommendations, content review workflows, and administration.
- Deterministic Recommendations.
- LLM-Assisted Recommendations using Azure OpenAI by default when policy allows.
- LLM-Inferred Candidate Hotspots, clearly labelled and not promoted without product validation.
- Prompt Cache Breakage as a supported hotspot when cache evidence is observed or responsibly correlated.
- Estimated Token Cost with Automated Pricing Seed, Pricing Update Review, customer overrides, and Unavailable Token Cost when no pricing match exists.
- Non-Punitive Budget Alerts scoped to Customer Organization, team, repository, workflow, harness, or model.
- Repository Discovery and Enrollment metadata model, with repository content scanning disabled unless explicitly enabled.
- Governance Audit Events for security, policy, content, recommendation, pricing, tenant administration, export, and deletion decisions.
- Data-Class Retention Policy foundations.
- Terraform Production Infrastructure using Azure Blob Storage remote state.
- Region Environment Workspace format: `{environment}_{region}_{customerOrganizationSlug}`.
- Initial workspaces using `internal` as the Customer Organization slug, such as `dv_eastus_internal` and `pd_eastus2_internal`.
- Public Repository Workflow Guardrail for deployment-capable GitHub Actions.
- Guarded Terraform Apply for manual deployments only.
- Azure Container Apps for services and Azure Container Apps Jobs for background work.
- Azure Front Door Premium WAF as the production edge.
- Azure Front Door managed certificates for explicit first-release product hostnames.
- Azure Front Door Private Link to Azure Container Apps origins so generated ACA FQDNs cannot bypass the edge in production.
- Private Data Plane for PostgreSQL, Blob Storage, Key Vault, and internal dependencies where feasible.
- Platform-Managed Encryption only. Customer Managed Keys are not offered.
- Day-1 Operable Baseline covering health probes, internal SLOs, Azure Monitor alerts, private action groups, restore validation, lifecycle validation, audit export, and incident runbooks.
- Production codebase transition that deletes, replaces, retains, or quarantines local-first implementation pieces instead of evolving local-only mode in place.

### Out Of Scope

- Supported local-only product mode.
- Codex desktop app support before telemetry parity validation.
- VS Code Copilot and Claude Code adapters.
- Multi-customer SaaS onboarding.
- Dedicated tenant infrastructure tiers.
- Active-active multi-region deployment.
- Azure API Management as first-release ingress.
- Application Gateway as first-release ingress.
- Publicly reachable Azure Container Apps generated FQDNs in production.
- First-release BYOC wildcard certificate renewal.
- Customer Managed Keys.
- People ranking, developer leaderboards, individual waste rankings, and manager views sorted by personal wrongness.
- Silent content capture.
- Store-then-redact raw content.
- Direct-to-monitor-only ingestion as the product system of record.
- Repository source-code scanning unless explicitly enabled by policy.
- Full tenant offboarding and self-service deletion workflows.

## Required User Stories

1. As a PlatformAdmin, I want to configure a Customer Organization and product roles so that access is governed by product policy rather than raw group names.
2. As a PlatformAdmin, I want to issue a Codex CLI setup profile and Scoped Ingestion Credential so that a developer can manually configure telemetry export.
3. As a Developer, I want to verify that Codex CLI telemetry is reaching the product so that I know my setup is working.
4. As a Developer, I want to inspect my own sessions and Optimization Coaching so that I can improve my agentic coding workflow.
5. As an EngineeringLead, I want to view aggregate token burn by team, repository, model, workflow, and hotspot so that I can drive efficiency without ranking people.
6. As a SecurityReviewer, I want redaction failures blocked from storage and routed for review so that sensitive content is not silently stored.
7. As a PlatformAdmin, I want Content Capture Policy disabled by default and explicitly configurable so that content capture is governed.
8. As an EngineeringLead, I want Session Investigation View to show timeline, hotspots, cache diagnostics, evidence, and recommendations so that I can understand what drove token burn.
9. As a PlatformAdmin, I want Managed Grafana dashboards for aggregate metrics so that operational and cost trends are visible.
10. As a PlatformAdmin, I want pricing seeds to be reviewable before they affect cost estimates so that dashboards do not silently rewrite cost history.
11. As a PlatformAdmin, I want Terraform deployment workflows guarded against fork or PR execution so that this public repository cannot trigger unsafe Azure changes.

## Acceptance Criteria

- ADR 0001 is marked superseded and ADR 0002 is accepted.
- The stale local-first PRD is marked superseded by this PRD.
- Codex CLI setup documentation provides manual configuration steps for the Product Ingestion Endpoint.
- A developer-scoped Codex setup profile can be created, revoked, and validated.
- Codex telemetry reaches the Product Ingestion Endpoint through authenticated OTLP.
- Product ingestion rejects telemetry without valid tenant, credential, schema, or policy context.
- Product ingestion normalizes Codex metrics, traces, and logs or events into tenant-aware records.
- PostgreSQL stores Customer Organization, role mapping, setup profile, credential metadata, sessions, hotspots, recommendations, pricing basis, content references, and audit records.
- Managed Grafana shows aggregate token burn and estimated cost trends from the Azure observability metric backend.
- Managed Grafana dashboard provisioning uses Entra OIDC if the provider proof succeeds, with service account token fallback only when documented provider incompatibility requires it.
- Product Dashboard shows Session Investigation View for authorized users.
- Content Capture Mode is disabled by default.
- When enabled, captured content passes through the Pre-Storage Content Redaction Pipeline before Blob Storage.
- Redaction failures do not write Captured Content Blobs.
- Captured content is stored in Azure Blob Storage only after policy approval and redaction success.
- Recommendations preserve evidence refs, model or rule source, confidence, and generation metadata.
- LLM-inferred candidate hotspots remain labelled as candidates until validated.
- Prompt Cache Breakage is shown only with observed, correlated, LLM-inferred, or unavailable cache evidence states.
- No dashboard ranks individual developers by token burn, cost, waste, or wrongness.
- Terraform uses Azure Blob Storage remote state.
- Terraform workspace names follow `{environment}_{region}_{customerOrganizationSlug}`.
- Terraform modules use Azure Verified Modules where suitable modules exist.
- Deployment-capable workflows are manual-only and guarded by repository, actor, branch, environment, region, workspace, OIDC, permissions, and confirmation checks.
- A committed workflow guardrail validator and tests detect unsafe workflow triggers or missing deployment guards.
- Public product hostnames serve through Azure Front Door managed certificates.
- Front Door origin health succeeds through Private Link to Azure Container Apps.
- Direct public access to generated Azure Container Apps FQDNs fails in production.
- Health and readiness endpoints are implemented for each long-running production service.
- Azure Container Apps liveness and readiness probes are configured.
- Day-1 internal SLOs and minimum Azure Monitor alerts are implemented.
- A non-production PostgreSQL restore drill and Blob lifecycle validation pass before production readiness.
- Required first-release incident runbooks exist and avoid public disclosure of sensitive operational details.
- The production solution no longer depends on the Aspire AppHost.
- The Blazor Local Dashboard is replaced by the React Product Dashboard path.
- Direct file import is absent from the production ingestion path.
- Copilot JSONL tests are removed from active production CI or quarantined as future-adapter evidence.

## Documentation Deliverables

- `docs/specs/production-target-state.md`
- `docs/prd/azure-production-mvp.md`
- Supersession note in `docs/prd/local-first-mvp.md`
- `docs/architecture/azure-production-architecture.md`
- `docs/architecture/content-capture-and-redaction.md`
- `docs/architecture/recommendation-engine.md`
- `docs/architecture/identity-and-authorization.md`
- `docs/architecture/terraform-production-infrastructure.md`
- `docs/architecture/public-dns-and-certificates.md`
- `docs/architecture/production-codebase-transition.md`
- `docs/operations/edge-origin-validation.md`
- `docs/operations/production-operations.md`
- `docs/operations/certificate-renewal.md`
- `docs/operations/infrastructure-deletion.md`
- `docs/planning/production-implementation-roadmap.md`

## Issue Roadmap Shape

The implementation roadmap is defined in [Production Implementation Roadmap](../planning/production-implementation-roadmap.md).

The backlog uses milestone-based vertical slices.

First milestone:

- Production Skeleton And Guardrails.

Required later milestones:

- Tenant-Aware Product Foundation.
- Codex Ingestion Baseline.
- Azure Runtime And Edge Proof.
- Observability And Grafana.
- Content, Hotspots, Recommendations, And Pricing.
- Product Dashboard And Session Investigation.
- Day-1 Operations And Production Readiness.

Existing local-first issues should be superseded where they no longer fit, not silently rewritten into unrelated production work.
