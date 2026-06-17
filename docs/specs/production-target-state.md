# Production Target State Spec

## Purpose

This spec defines the intended mature production platform for AI agent token observability. It supersedes the local-first product direction and anchors the future Azure Production MVP PRD, architecture documents, Terraform work, and GitHub issue roadmap.

The target state is a vendor-operated multi-tenant SaaS platform. It observes token burn across supported coding-agent harnesses, identifies evidence-backed hotspots, explains optimization opportunities, and helps engineering teams improve agentic coding workflows without creating a blame or surveillance product.

## Product Posture

The product target state is the Multi-Tenant SaaS Target State. Each product tenant is a Customer Organization with its own identity mappings, teams, repositories, harness setup profiles, retention policy, content capture policy, pricing basis, recommendation model policy, and billing boundary.

The first production release is a tenant-aware Single-Enterprise Release. It may run for one Customer Organization, but code, data, APIs, authorization, Terraform workspaces, and operational controls must be shaped so the system does not assume one tenant forever.

Local execution is developer convenience only. It is not a supported product mode.

## Harness Scope

Codex is the Production MVP Harness. The Azure Production MVP starts with Codex CLI telemetry.

The Production Target State supports Codex, VS Code Copilot, and Claude Code as Target-State Harnesses. Codex desktop app support is part of the target state after telemetry parity is validated.

Every harness is configured manually by each developer. The product may generate setup profiles, endpoint values, and validation guidance, but there is no silent enrollment or device-management-only rollout assumption.

## Telemetry Scope

The product ingests Agent Telemetry Signals: OpenTelemetry metrics, traces, and logs or events.

Metrics support Managed Grafana aggregate views. Traces and logs or events support session reconstruction, hotspot evidence, content capture decisions, cache diagnostics, and recommendations.

The canonical ingestion path is OTLP to the Product Ingestion Endpoint, optionally through a customer-side OpenTelemetry Collector. Direct-to-monitor-only ingestion is not the product system of record because it bypasses tenant validation, schema validation, content policy, credential identity, and normalization.

Each supported harness has a Production Ingestion Contract. The Azure Production MVP starts with the Codex CLI contract.

## Identity And Authorization

The target state uses Federated Customer Identity. Users authenticate with a Customer Organization identity provider, such as Microsoft Entra ID, while the product maps those external users and groups into product roles.

Runtime authorization uses Product Role Mapping. Raw Entra group names are input evidence for role resolution, not the authorization model itself.

Initial product roles:

- `PlatformAdmin`: manages tenant configuration, harness setup, retention, role mappings, pricing, and policy.
- `SecurityReviewer`: reviews sensitive captured content, redaction failures, and security-sensitive evidence where policy allows.
- `EngineeringLead`: views assigned team and repository analytics, sessions, hotspots, and recommendations.
- `Developer`: views their own sessions and coaching, plus scoped team or repository data where policy allows.
- `ReadOnlyViewer`: views approved aggregate dashboards without sensitive session content or individual coaching by default.

Scoped Ingestion Credentials authenticate telemetry uploads for a specific Customer Organization, developer, and harness setup profile. Credential-derived developer identity is authoritative for product authorization and self-view. Harness-emitted identity is retained as evidence but must not control access.

## Non-Punitive Optimization

The product must not become a developer ranking or blame system.

Aggregate dashboards emphasize team, repository, harness, model, workflow, and hotspot dimensions. Individual identity is minimized by default and shown only for self-view, scoped leadership workflows, authorized investigations, or security review.

Optimization Coaching explains what behavior, prompt, context choice, or workflow likely increased token burn and how the user could improve it. Coaching must be evidence-backed, role-scoped, and framed as workflow improvement rather than blame.

People ranking, developer leaderboards, individual waste rankings, and manager views sorted by personal wrongness are out of scope.

## Azure Architecture

Production app compute uses Azure Container Apps for HTTP services and Azure Container Apps Jobs for bounded background work.

HTTP services include:

- Product Dashboard.
- Product Ingestion Endpoint.
- Dashboard and product APIs.
- Admin and configuration APIs.

Jobs include:

- Telemetry normalization.
- Hotspot detection.
- Recommendation generation.
- Content redaction.
- Retention cleanup.
- Reprocessing.
- Tenant maintenance.
- Pricing refresh.

The production ingress boundary uses public HTTPS for the Product Dashboard and Product Ingestion Endpoint through the Production Edge. These entry points are protected by authentication, tenant validation, rate limits, and WAF or front-door controls. Direct public origin bypass is not allowed in production.

The first-release production edge is Azure Front Door Premium WAF routing to Azure Container Apps through Private Link. Azure API Management is a future API gateway option for API lifecycle management, productized API access, policy centralization, partner access, and scale needs. APIM is not a first-release dependency.

The private data plane keeps PostgreSQL, Blob Storage, Key Vault, and other product stores or internal dependencies on private access paths where feasible. Public data-store endpoints should be disabled where feasible. App-to-store access should use managed identity and least privilege.

## Observability Backend Split

Production uses separate stores for separate access and retention patterns:

- Managed Prometheus or Azure Monitor workspace for time-series metrics used by Azure Managed Grafana.
- Application Insights or Log Analytics for traces, logs, events, application diagnostics, and investigation queries.
- Azure Database for PostgreSQL Flexible Server as the Product Metadata Store.
- Azure Blob Storage as the Content Capture Store.

Azure Managed Grafana is the Managed Grafana Surface for aggregate observability views such as token burn trends, cost trends, harness and model breakdowns, and high-level hotspot panels.

The Product Dashboard owns session drill-down, hotspot evidence, recommendation review, content capture workflows, governance, and administration.

## Product Metadata Store

The Product Metadata Store is Azure Database for PostgreSQL Flexible Server.

It stores tenant-aware transactional records, including:

- Customer Organizations.
- Identity mappings and product roles.
- Teams.
- Repository enrollment records.
- Harness setup profiles.
- Scoped Ingestion Credentials metadata.
- Sessions and normalized telemetry summaries.
- Token Hotspots.
- Recommendation records.
- Pricing basis records.
- Content references.
- Policies.
- Governance Audit Events.

Full captured content is not stored in PostgreSQL by default. PostgreSQL stores references, hashes, classifications, status, retention metadata, and audit context for captured content.

## Content Capture And Redaction

Content Capture Mode is supported in production but controlled by Customer Organization policy. It is not silent and is not unrestricted.

Content Capture Policy controls whether prompt snippets, selected tool inputs, selected tool outputs, model response excerpts, command summaries, error summaries, or file content may be captured, retained, reviewed, and used for recommendations.

The Pre-Storage Content Redaction Pipeline runs before any Captured Content Blob is written. The pipeline should combine deterministic secret scanning, Azure AI Language PII detection, Azure AI Content Safety checks, and product-specific redaction rules.

If content cannot be redacted confidently, the Redaction Failure Gate blocks storage. The product stores metadata only, marks the item as redaction failed or review required, and allows a privileged reviewer to retry, discard, or approve a bounded excerpt.

The Content Capture Store is Azure Blob Storage. It stores only policy-approved captured artifacts. PostgreSQL stores metadata and references.

The product uses Platform-Managed Encryption. Customer Managed Keys are not offered as a product capability.

## Repository Discovery And Evidence

Customer Organizations connect source providers so the product can discover repositories. Repositories are enrolled through policy rules, self-service requests, or review of unmatched telemetry candidates.

Repository discovery is metadata-first. Repository content scanning requires explicit Repository Content Scanning Policy at the repository or Customer Organization level.

Repository scanner findings are Correlatable Repository Evidence. They can support hotspot attribution only when correlated with Agent Telemetry Signals or other session evidence. Scanner findings must not be presented as harness-emitted session facts.

## Hotspots And Recommendations

Token Hotspots must preserve Hotspot Attribution Type: direct, correlated, or inferred.

LLM-Inferred Candidate Hotspots are allowed only when grounded in explicit telemetry, content, or policy evidence. They remain labelled as candidates until product validation promotes or rejects them.

LLMs must not be the sole authority for confirmed Token Hotspots or factual user-error claims.

Recommendations can be deterministic, LLM-assisted, or generated from validated candidate hotspots. LLM-assisted recommendations must be grounded in evidence packets, preserve evidence references, and store model, provider, prompt-template, policy, and generation metadata.

Asynchronous Recommendation Generation runs after ingestion and hotspot detection. Authorized users may request regeneration when evidence, policy, model, or prompt-template versions change.

Recommendation Model Policy controls whether LLM-assisted recommendations are enabled and which model providers, deployments, prompt templates, and evidence classes may be used. Azure OpenAI is the default first-release provider. Anthropic or other providers are target-state options when Customer Organization policy allows them.

## Prompt Cache Diagnostics

Prompt Cache Breakage is a first-class target-state Token Hotspot.

Cache Evidence Availability distinguishes:

- Observed provider or harness cache fields.
- Correlated cache patterns.
- LLM-inferred cache explanations.
- Unavailable cache evidence.

The product may explain why cache effectiveness likely dropped only when evidence supports the claim. If provider or harness cache evidence is missing and content capture policy does not allow enough context, the product must say the reason is unavailable.

## Session Investigation View

The Product Dashboard click-through experience for a session is the Session Investigation View.

It shows:

- Session summary.
- Timeline of turns, tool calls, model invocations, approvals, errors, retries, and high-burn points.
- Confirmed Token Hotspots.
- LLM-inferred candidate hotspots.
- Cache diagnostics.
- Policy-approved content evidence.
- Recommendations.
- Optimization Coaching.
- Audit context showing evidence used, evidence hidden by policy, model and prompt versions, generation time, and reviewer state.

Grafana may deep-link into this view, but Grafana does not own the session investigation workflow.

## Pricing, Budgets, And Alerts

Estimated Token Cost is dashboard guidance, not provider invoice cost.

The product maintains a Harness Pricing Basis per Customer Organization, harness, provider, model, billing route, token class, and effective date.

Automated Pricing Seed refreshes default provider pricing candidates. Pricing Update Review requires PlatformAdmin acceptance, rejection, or override before pricing changes affect estimated cost, budgets, forecasts, or trend comparisons.

Customer Organizations can configure pricing overrides for enterprise agreements.

If pricing cannot be matched, the product shows Unavailable Token Cost rather than guessed cost.

Non-Punitive Budget Alerts are scoped to Customer Organization, team, repository, workflow, harness, or model. They must not rank or blame individual developers.

## Retention, Audit, And Lifecycle

Data-Class Retention Policy applies different retention periods and deletion behavior to:

- Aggregate metrics.
- Normalized session metadata.
- Traces, logs, and events.
- Captured content.
- Governance Audit Events.
- Pricing versions.
- Recommendation versions.

Governance Audit Events are required for security, policy, content, recommendation, pricing, tenant administration, data export, and deletion decisions.

Data Lifecycle Workflows cover export, deletion, retention cleanup, offboarding, and legal hold across product metadata, captured content, telemetry stores, recommendations, audits, and backups.

## Infrastructure And Deployment

Infrastructure is Terraform-first. Terraform uses Azure Blob Storage remote state.

Terraform code must use Azure Verified Modules where suitable modules exist. If no suitable AVM exists, use AzureRM resources. Use AzAPI only for provider gaps.

Terraform workspaces use the Region Environment Workspace pattern: `{environment}_{region}_{customerOrganizationSlug}`. The first Single-Enterprise Release may use `internal` as the Customer Organization slug, such as `dv_eastus_internal` or `pd_eastus2_internal`.

The Azure Production MVP is single-region active per environment. The target state documents multi-region placement, failover, and tenant data-residency options.

Deployment-capable GitHub Actions workflows must follow the Public Repository Workflow Guardrail:

- `workflow_dispatch` only.
- No fork or PR execution path for Azure-changing jobs.
- Expected repository validation.
- Expected actor validation.
- Explicit environment and region inputs.
- Derived workspace validation.
- Least-privilege permissions.
- Azure OIDC with least privilege.
- Environment protection for higher environments.
- Guardrail validation script and tests.

Guarded Terraform Apply may run from GitHub Actions only after repository, actor, environment, region, workspace, branch, confirmation, OIDC, least-privilege, and environment-protection checks pass. The retained public DNS apply workflow is the only confirmation-input exception, and only because it is fixed to `public_dns` in `pd_eastus2_internal`, uses protected environment approval, applies a same-run saved plan artifact, emits Cloudflare delegation output only, and verifies public NS delegation.

## MVP Boundary To Be Defined Separately

The Azure Production MVP PRD must choose the first releasable slice from this target state. It should not reintroduce local-only mode, people ranking, silent content capture, direct-to-monitor-only ingestion, unguarded deployment workflows, or tenantless data models.
