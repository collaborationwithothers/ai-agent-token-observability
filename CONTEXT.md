# AI Agent Token Observability Context

This context defines the project language for observing token usage across AI coding-agent sessions. It keeps the domain terms separate from implementation plans and architecture decisions.

## Language

**Coding-Agent Harness**:
The tool or runtime that mediates an AI coding-agent session, such as VS Code Copilot, Claude Code, or Codex.
_Avoid_: Agent vendor, AI tool

**Production MVP Harness**:
The Coding-Agent Harness implemented first for the Azure Production MVP. For this project, the Production MVP Harness is Codex.
_Avoid_: Primary local harness, secondary harness

**Target-State Harness**:
A Coding-Agent Harness supported in the Production Target State Spec. Codex, VS Code Copilot, and Claude Code are Target-State Harnesses.
_Avoid_: Stretch harness, optional vendor

**Codex Surface**:
The Codex runtime surface that emits or may emit Agent Telemetry Signals, including Codex CLI for the Azure Production MVP and Codex desktop app for the Production Target State Spec after telemetry parity validation.
_Avoid_: Codex CLI only, unvalidated desktop parity

**Token Burn**:
Token usage that should be investigated because it may be expensive, repeated, wasteful, or poorly attributed.
_Avoid_: Token cost, token usage

**Token Hotspot**:
A source of token burn tied to a session, turn, tool, file, folder, instruction, specification, model, harness, or repository.
_Avoid_: Finding, issue, alert

**Prompt Cache Breakage**:
A Token Hotspot where reusable prompt context was not read from cache, or cache effectiveness dropped, causing avoidable input token burn, latency, or cost.
_Avoid_: Generic cache miss, provider billing issue

**Cache Evidence Availability**:
The evidence state for Prompt Cache Breakage analysis, distinguishing observed provider or harness cache fields, correlated cache patterns, LLM-inferred cache explanations, and unavailable cache evidence.
_Avoid_: Assumed cache miss, hidden cache uncertainty

**Work Family**:
A rollup that groups Token Burn by the kind of work being performed across sessions, repositories, or teams. It supports portfolio-level analysis without replacing evidence-backed Token Hotspots.
_Avoid_: Primary hotspot, unsupported work label

**Hotspot Attribution Type**:
The evidence class for a Token Hotspot: direct when harness telemetry identifies the source, correlated when telemetry and Repo Context Enrichment align, and inferred when the engine proposes a likely cause without direct source evidence.
_Avoid_: Proven hotspot

**Unavailable Token Metric**:
A token metric that the Coding-Agent Harness did not emit or that the adapter cannot prove. It is represented as `NULL`, not zero.
_Avoid_: Zero token metric, missing-as-zero

**Metric Status**:
The availability state for a normalized metric, such as observed, estimated, unavailable, or not applicable.
_Avoid_: Boolean present flag

**Metric Confidence**:
The confidence level attached to an observed, estimated, or inferred metric so downstream views do not imply perfect attribution.
_Avoid_: Accuracy, certainty

**Metric Quality Marker**:
A quiet value-level display marker that shows Metric Status, Metric Confidence, or Token Total Type wherever an exact dashboard number depends on observed, estimated, mixed, or unavailable data.
_Avoid_: Headline-only caveat, hidden quality note

**Token Total Type**:
The classification for a token aggregate: observed when all included values came from provider or harness telemetry, estimated when values came from token estimation, and mixed when both are present.
_Avoid_: Total tokens

**Content Capture Mode**:
A production capability that can store policy-approved prompt snippets, tool results, command outputs, model excerpts, or file content only after the Pre-Storage Content Redaction Pipeline allows storage.
_Avoid_: Default content capture, silent capture

**Content Capture Policy**:
A Customer Organization policy that controls whether prompt snippets, selected tool inputs, selected tool outputs, model response excerpts, command summaries, or error summaries may be captured, retained, reviewed, and used for recommendations.
_Avoid_: Silent content logging, unrestricted prompt storage

**Content Capture Store**:
The product-owned encrypted storage boundary for policy-approved captured content and content references. It is separate from the observability backend used for aggregate metrics, traces, logs, and events.
_Avoid_: Log Analytics content store, unrestricted telemetry logs

**Pre-Storage Content Redaction Pipeline**:
The content safety boundary that detects, redacts, rejects, or routes captured content for review before any Captured Content Blob is written to the Content Capture Store.
_Avoid_: Store-then-redact, raw-content default

**Redaction Failure Gate**:
The rule that content must not be stored as a Captured Content Blob when it cannot be redacted confidently. The product stores metadata only and marks the item as redaction failed or review required until a privileged reviewer retries, discards, or approves a bounded excerpt.
_Avoid_: Best-effort raw storage, silent redaction failure

**Captured Content Blob**:
A policy-approved captured content artifact stored in Azure Blob Storage, referenced by the Product Metadata Store, and governed by Customer Organization retention, access, and isolation policy.
_Avoid_: Telemetry log payload, database text dump

**Observability Backend Split**:
The production storage boundary that keeps Managed Prometheus metrics, Application Insights or Log Analytics traces and logs or events, product metadata, and captured content in separate stores with separate query, retention, and access patterns.
_Avoid_: One telemetry database, all-in-logs storage

**Product Metadata Store**:
The tenant-aware transactional store for Customer Organizations, users, role mappings, teams, repositories, sessions, normalized telemetry summaries, Token Hotspots, recommendations, policies, content references, and audit metadata.
_Avoid_: Telemetry log store, captured-content store

**Deterministic Recommendation**:
A recommendation generated by a rule with known trigger conditions, evidence, confidence, and expected benefit.
_Avoid_: AI recommendation, generated advice

**Recommendation Rationale**:
A short dashboard-visible explanation of why a recommendation exists, including its trigger reason, expected benefit, confidence, and a link to the supporting Token Hotspot evidence.
_Avoid_: Unsupported action, evidence-only link

**LLM-Assisted Recommendation**:
A production-path recommendation explanation drafted by an LLM from existing hotspot evidence, deterministic recommendations, and policy constraints. It must not create new unsupported findings.
_Avoid_: LLM finding, autonomous recommendation

**Asynchronous Recommendation Generation**:
The production workflow that generates recommendation records after ingestion and hotspot detection, with authorized on-demand regeneration when evidence, policy, model, or prompt-template versions change.
_Avoid_: Click-time-only model call, unversioned recommendation

**LLM-Inferred Candidate Hotspot**:
A proposed Token Hotspot generated by an LLM from explicit telemetry, content, or policy evidence. It must remain labelled as a candidate until product validation promotes it or rejects it.
_Avoid_: Confirmed hotspot, unsupported LLM finding

**Recommendation Evidence Packet**:
The immutable, policy-filtered evidence bundle used to generate a deterministic or LLM-assisted recommendation version.
_Avoid_: Raw prompt bundle, mutable recommendation context

**Recommendation Validation Gate**:
The product-owned validation boundary that rejects unsupported, ungrounded, policy-violating, blame-oriented, or schema-invalid recommendation output before it becomes visible.
_Avoid_: LLM self-check, best-effort validation

**Candidate Hotspot Review**:
The workflow that allows authorized reviewers to promote, reject, expire, or supersede LLM-Inferred Candidate Hotspots based on product validation and evidence.
_Avoid_: automatic LLM promotion, unreviewed finding

**Optimization Feedback Loop**:
The path from normalized telemetry to Token Hotspots, evidence, confidence, and recommendations. It is what distinguishes the platform from a token-count dashboard.
_Avoid_: Dashboard, report

**Azure Production MVP**:
The first production-hosted Azure release of the platform, with local execution treated only as developer convenience and not as a product mode.
_Avoid_: Local-first MVP, production-only

**Production Target State Spec**:
The complete production-state product specification that describes the intended mature platform across ingestion, storage, visualization, security, recommendations, operations, and governance.
_Avoid_: MVP PRD, implementation plan

**Single-Enterprise Release**:
A production release used by one enterprise boundary, where teams, repositories, harnesses, and users are dimensions inside that enterprise rather than separate SaaS customers.
_Avoid_: Final target state, multi-customer SaaS

**Tenant-Aware First Release**:
The Single-Enterprise Release constraint that supports one Customer Organization on shared or single Azure resources while keeping code, data, authorization, and APIs explicitly scoped by Customer Organization.
_Avoid_: One-tenant-forever schema, tenantless MVP

**Multi-Tenant SaaS Target State**:
The final production model where the platform serves multiple customer tenants with tenant-aware identity, isolation, governance, operations, and billing boundaries.
_Avoid_: Single-enterprise deployment, internal-only platform

**Vendor-Operated SaaS**:
The target operating model where the product team operates the Azure platform, telemetry pipeline, stores, dashboards, recommendations, and upgrades for Customer Organizations, including optional dedicated resource tiers.
_Avoid_: Customer-operated deployment, unmanaged customer install

**Platform-Managed Encryption**:
The product encryption boundary where Azure platform-managed encryption is used for product stores and Customer Managed Keys are not offered as a product capability.
_Avoid_: Customer-managed key, tenant-owned encryption key

**Customer Organization**:
The product tenant in the Multi-Tenant SaaS Target State. A Customer Organization can connect one or more identity tenants and contains its teams, users, repositories, harness configurations, dashboards, retention policies, role mappings, and billing boundary.
_Avoid_: Entra tenant, team, repository

**Customer Data Residency Region**:
The primary Azure region where a Customer Organization's product metadata, captured content, detailed telemetry, and recommendation evidence are stored and processed unless its policy allows replication or cross-region processing.
_Avoid_: Regionless tenant, accidental cross-region processing

**Hybrid Tenant Isolation**:
The target-state tenancy model where most Customer Organizations use shared infrastructure with strict tenant-aware controls, while higher-risk or larger Customer Organizations can use dedicated data, observability, storage, or deployment resources.
_Avoid_: Shared-only tenancy, dedicated-only tenancy

**Federated Customer Identity**:
An identity model where users authenticate with a Customer Organization identity provider, such as Microsoft Entra ID, while the product maps those external identities and groups into Customer Organization roles.
_Avoid_: SaaS-owned employee accounts, local password store

**Product Role Mapping**:
A Customer Organization authorization rule that maps external identity users or groups into product-owned roles and scopes used for runtime authorization.
_Avoid_: Raw group-name authorization, hardcoded Entra groups

**Recommendation Model Policy**:
A Customer Organization policy that controls whether LLM-assisted recommendations are enabled and which model providers, deployments, prompt templates, and evidence classes may be used.
_Avoid_: Hardcoded model provider, ungoverned recommendation model

**Managed Grafana Surface**:
The Azure Managed Grafana visualization layer for aggregate observability views such as token burn trends, cost trends, harness and model breakdowns, and high-level hotspot panels.
_Avoid_: Whole product dashboard, recommendation workspace

**Product Dashboard**:
The role-protected product application for session drill-down, hotspot evidence, recommendation review, content capture workflows, governance, and administration.
_Avoid_: Grafana-only dashboard, metric-only dashboard

**React Product Dashboard**:
The production Product Dashboard implementation choice: a React, TypeScript, and Vite single-page application backed by Product API.
_Avoid_: Blazor carry-forward, browser-direct data store access

**Product API Contract**:
The versioned product application API boundary for Product Dashboard, administration, session investigation, content review, recommendations, pricing, budgets, and audit workflows.
_Avoid_: OTLP ingestion contract, browser-direct telemetry store access

**Session Investigation View**:
The Product Dashboard click-through view for a Coding-Agent Harness session, showing summary, timeline, Token Hotspots, cache diagnostics, policy-approved content evidence, recommendations, and audit context.
_Avoid_: Raw trace viewer, Grafana session page

**Optimization Coaching**:
Evidence-backed guidance that explains what behavior, prompt, context choice, or workflow likely increased Token Burn and how the user could improve it.
_Avoid_: Blame, unsupported user-error claim

**Coaching Visibility Scope**:
The role and resource boundary that controls who can view Optimization Coaching for a session, keeping individual coaching separate from broad read-only dashboard access.
_Avoid_: Public user coaching, leaderboard feedback

**Non-Punitive Optimization**:
The product principle that metrics, Token Hotspots, and recommendations exist to improve agentic coding workflows, reduce cost, and increase efficiency without enabling blame, surveillance, or public developer ranking.
_Avoid_: Developer leaderboard, blame dashboard, individual waste ranking

**Identity-Minimized Dashboard**:
A dashboard default that emphasizes team, repository, harness, model, workflow, and hotspot dimensions while limiting individual identity display to self-view, authorized investigation, or scoped leadership contexts.
_Avoid_: Person-first dashboard, public individual analytics

**Agent Telemetry Signals**:
The OpenTelemetry metrics, traces, and logs or events emitted by Coding-Agent Harnesses. Metrics power aggregate observability, while traces and logs or events support session reconstruction, hotspot evidence, content capture, and recommendations.
_Avoid_: Metrics-only telemetry, raw logs only

**Product Ingestion Endpoint**:
The tenant-aware OTLP ingestion boundary that validates source identity, Customer Organization, schema version, and Content Capture Policy before normalizing telemetry and routing observability signals to downstream Azure backends.
_Avoid_: Direct-to-monitor-only ingestion, unvalidated harness upload

**Production Ingestion Contract**:
The versioned product contract that defines which harness telemetry signals are accepted, how they are authenticated, how tenant and developer identity are resolved, how content-bearing fields are governed, and how normalized records are routed.
_Avoid_: Informal log scrape, monitor-only contract, harness-specific guesswork

**Harness Telemetry Envelope**:
The product-normalized wrapper around an incoming harness telemetry record, containing tenant, harness, credential, schema, session, signal, evidence state, content policy, and routing metadata.
_Avoid_: Raw OTLP record as domain model, unscoped telemetry event

**Signal Routing Policy**:
The product rule set that decides whether normalized telemetry becomes aggregate metrics, traces or logs, product metadata, captured content, audit evidence, or rejection records.
_Avoid_: Store everything everywhere, direct raw fan-out

**Manual Harness Telemetry Setup**:
The onboarding model where each developer explicitly configures each Coding-Agent Harness to export Agent Telemetry Signals using a Customer Organization setup profile, rather than relying on centralized silent rollout.
_Avoid_: Silent harness enrollment, device-management-only rollout

**Scoped Ingestion Credential**:
A revocable credential issued for a specific Customer Organization, developer, and Coding-Agent Harness setup profile to authenticate telemetry uploads to the Product Ingestion Endpoint.
_Avoid_: Shared tenant upload key, anonymous telemetry upload

**Credential-Derived Developer Identity**:
The product identity attributed from the authenticated Scoped Ingestion Credential and used for authorization, self-view, and session ownership, with harness-emitted identity retained as evidence rather than access-control authority.
_Avoid_: Telemetry-authorized user, unchecked harness identity

**Repository Discovery and Enrollment**:
The product model where Customer Organizations connect source providers, discover repositories, and enroll repositories through policy rules, self-service requests, or review of unmatched telemetry candidates.
_Avoid_: Manual repo list only, arbitrary harness repo authority

**Repository Content Scanning Policy**:
A repository-level or Customer Organization policy that controls whether the product may inspect repository file content, store derived facts, or use repository content as recommendation evidence.
_Avoid_: Implicit source-code scanning, discovery-equals-content-access

**Correlatable Repository Evidence**:
Repository-derived evidence that can support Token Hotspot attribution only when correlated with Agent Telemetry Signals or other session evidence, and must not be presented as a harness-emitted session fact.
_Avoid_: Scanner-as-session-fact, unsupported repo attribution

**Production App Compute**:
The Azure Container Apps compute boundary for product HTTP services and Azure Container Apps Jobs for bounded background processing such as normalization, recommendation generation, content redaction, retention cleanup, reprocessing, and tenant maintenance.
_Avoid_: VM-hosted app, local app platform

**Production Ingress Boundary**:
The production network boundary where Product Dashboard and Product Ingestion Endpoint use public HTTPS ingress through the Production Edge, while direct public origin bypass and public data stores are not allowed in production.
_Avoid_: Private-only developer ingestion, public data plane

**Private Data Plane**:
The production networking boundary where product stores and internal dependencies use private access patterns, public network access is disabled where feasible, and application access uses managed identity and least privilege.
_Avoid_: Public database endpoint, network-open storage

**Production Edge**:
The first-release public edge pattern where Azure Front Door Premium WAF serves public product hostnames, uses managed certificates, and reaches Azure Container Apps origins through Private Link, with Azure API Management reserved as a later API lifecycle and policy gateway option.
_Avoid_: Application Gateway first, APIM as first-release requirement, public ACA origin bypass

**Product DNS Zone**:
The delegated public DNS zone used for product-owned hostnames, certificate validation records, and production edge routing while the apex domain can remain with an external DNS provider.
_Avoid_: Apex takeover, unmanaged product DNS

**Product TLS Certificate Boundary**:
The public TLS boundary for product hostnames, where the first release uses Azure Front Door managed certificates for explicit product hostnames and defers shared Key Vault BYOC wildcard certificates to a later decision.
_Avoid_: App-local certificate, per-environment certificate sprawl, first-release BYOC requirement

**Future API Gateway Option**:
A target-state option to introduce Azure API Management as the external API entry point when API lifecycle management, policy centralization, partner access, or product scale requires it.
_Avoid_: First-release APIM dependency, direct-only forever

**Production Stack Boundary**:
The technology boundary that keeps .NET for backend services, ingestion, and jobs while treating the Product Dashboard frontend as a separate production architecture decision rather than inheriting the local-first Blazor choice.
_Avoid_: Local-first stack carryover, frontend stack by default

**Runtime Service Topology**:
The production runtime split between long-running product HTTP services and bounded background job commands.
_Avoid_: one-app-for-everything, accidental service split

**Terraform Production Infrastructure**:
The infrastructure-as-code boundary for Azure production resources, using Terraform with Azure Blob Storage remote state, environment-region-customer-scoped workspaces, and Azure Verified Modules where suitable modules exist.
_Avoid_: Bicep-first infrastructure, local-only provisioning

**Region Environment Workspace**:
A Terraform workspace scope that combines deployment environment, such as `dv`, `qa`, `pp`, or `pd`, Azure region, such as `eastus`, `eastus2`, or `westeurope`, and Customer Organization slug, such as `internal`, so state and deployments are separated by production stage, geography, and tenant boundary.
_Avoid_: Default workspace, unscoped state

**Regional Release Strategy**:
The deployment strategy where the Azure Production MVP runs active in a single region per environment while the Production Target State Spec documents multi-region placement, failover, and tenant data-residency options.
_Avoid_: Active-active MVP, regionless deployment

**Guarded Terraform Apply**:
A manual GitHub Actions deployment path that may run Terraform apply only after repository, actor, environment, region, workspace, branch, confirmation, OIDC, least-privilege, and environment-protection checks pass.
_Avoid_: Unguarded apply, PR-triggered deployment

**Public Repository Workflow Guardrail**:
The GitHub Actions safety boundary for this public repository, requiring deployment-capable workflows to be manually triggered, reject fork or PR execution paths, verify the expected repository and triggering actor, keep token permissions least-privileged, and be enforced by committed validation scripts and tests.
_Avoid_: Fork-triggered deployment, unguarded public workflow

**Estimated Token Cost**:
A dollar-denominated estimate of Token Burn based on the configured pricing basis for the Coding-Agent Harness, model, token type, and billing route. It is dashboard guidance, not a provider invoice.
_Avoid_: Billed cost, exact invoice

**Unavailable Token Cost**:
A cost state used when token metrics exist but the dashboard cannot match the Coding-Agent Harness, model, token type, billing route, or Harness Pricing Basis well enough to produce an Estimated Token Cost.
_Avoid_: Fallback average cost, guessed cost

**Harness Pricing Basis**:
The pricing rule set used to turn token metrics into Estimated Token Cost for a specific Coding-Agent Harness, such as GitHub Copilot AI credit pricing, OpenAI API pricing, Anthropic API pricing, or a later enterprise-specific rate table.
_Avoid_: Universal API rate, hardcoded model cost

**Automated Pricing Seed**:
The product workflow that refreshes default provider pricing candidates for supported harnesses, providers, models, token classes, and billing routes before Customer Organization overrides are applied.
_Avoid_: Hardcoded pricing table, manual-only provider pricing

**Pricing Update Review**:
The PlatformAdmin workflow that reviews, accepts, rejects, or overrides automated pricing changes before they affect Estimated Token Cost, budgets, forecasts, or trend comparisons.
_Avoid_: Silent pricing update, unversioned cost recalculation

**Non-Punitive Budget Alert**:
A token or estimated-cost alert scoped to a team, repository, workflow, harness, model, or Customer Organization policy boundary, designed to prompt optimization without ranking or blaming individual developers.
_Avoid_: Individual waste alert, blame notification

**Governance Audit Event**:
A product audit record for security, policy, content, recommendation, pricing, tenant administration, data export, and deletion decisions that must remain traceable to an actor, scope, time, and evidence context.
_Avoid_: Best-effort admin log, hidden policy change

**Data-Class Retention Policy**:
A Customer Organization retention rule set that applies different retention periods and deletion behavior to aggregate metrics, normalized session metadata, traces, logs or events, captured content, audit events, pricing versions, and recommendation versions.
_Avoid_: Single global retention, content-retained-like-metrics

**Data Lifecycle Workflow**:
The export, deletion, retention cleanup, offboarding, and legal-hold workflow set for Customer Organization data across product metadata, captured content, telemetry stores, recommendations, audits, and backups.
_Avoid_: Delete-from-one-store only, retention without audit

**Infrastructure Deletion Workflow**:
A guarded Terraform workflow that destroys disposable Azure environment stages in an approved order while retaining shared foundation resources.
_Avoid_: Tag-based resource deletion, portal cleanup, unscoped destroy

**Retained Shared Resource**:
An Azure resource that must survive environment deletion because it supports shared identity, state, DNS, certificates, container images, audit, or recovery boundaries.
_Avoid_: Disposable environment resource, accidental shared cleanup

**Day-1 Operable Baseline**:
The first-release operations target that makes the production system runnable and recoverable with health checks, internal SLOs, private alerts, validation drills, audit visibility, and incident runbooks without claiming a mature external SLA.
_Avoid_: Full SRE program, no-ops MVP, public incident disclosure

**Production Codebase Transition**:
The migration boundary that treats local-first implementation code as delete, replace, retain, or quarantine material before building the Azure Production MVP runtime shape.
_Avoid_: Evolve local-only mode, hidden local dependency

**Model Cost Mix**:
A dashboard overview of Token Burn and Estimated Token Cost by model within the selected Dashboard Range.
_Avoid_: Harness comparison, provider invoice

**Pricing Refresh Workflow**:
A workflow that fetches provider pricing sources, compares them with the Harness Pricing Basis, shows a reviewable diff, and updates product pricing configuration only after explicit acceptance.
_Avoid_: Automatic live scraping, silent cost changes

**Dashboard Export Workflow**:
A later-phase workflow that exports a selected dashboard artifact, such as session rows, hotspot reports, cost reports, or evidence bundles, once the useful export shapes are known.
_Avoid_: First redesign action, automatic report generation

**Scale Equivalent**:
A later-phase illustrative estimate that translates Token Burn into a familiar comparison. It is not evidence for a Token Hotspot or recommendation.
_Avoid_: MVP proof, hotspot evidence

**Copilot Field Mapping**:
The Phase 0 document that maps VS Code Copilot OTel fields into the normalized model and classifies each field as documented, fixture-observed, optional when available, or content-capture-only.
_Avoid_: Assumed schema, parser guesses

**Workspace Repo**:
A repository root associated with an agent session workspace. A single session can have multiple Workspace Repo records.
_Avoid_: Session repo, primary repo

**Repo Path Hash**:
A stable pseudonymous identifier for a repository path, derived with a configured salt and stored alongside display-oriented repository labels and paths.
_Avoid_: Raw path, absolute path

**Repo Display Path**:
A dashboard-oriented repository path label that may include an absolute local path or repo-relative context-source path when available. It improves MVP usability and remains distinct from prompt text, code content, command output, and tool result capture.
_Avoid_: Content capture, pseudonymous-only repo label

**Developer Identity**:
A real-world person or service account associated with an agent session, such as a full name or email address when available.
_Avoid_: User profile, author

**User Hash**:
A stable pseudonymous identifier for a Developer Identity, derived with a configured salt and stored alongside display-oriented developer labels when available.
_Avoid_: Username, email

**Developer Display Label**:
A dashboard-oriented label for Developer Identity, such as a real email address, full name, service account name, or explicitly supplied display alias.
_Avoid_: Pseudonymous-only user label, hidden user

**Display Identity Source**:
The allowed source for Developer Display Labels, full repo names, and Repo Display Paths. Display identity comes from explicit import or enrichment input first, or from telemetry only when the field is clearly emitted by the Coding-Agent Harness.
_Avoid_: Silent Git scraping, OS user inference

**Collector Ingestion Path**:
The later ingestion path where telemetry flows through an OpenTelemetry Collector before normalization and storage.
_Avoid_: Optional future rewrite, unrelated production feature

**Repo Context Enrichment**:
The separate enrichment step that scans repository files and metadata to add context to normalized telemetry without treating scanner findings as harness-emitted facts.
_Avoid_: Repo ingestion, scanner truth

**Spec Kit Spec-Bloat Scenario**:
A product scenario where GitHub Spec Kit creates specification-driven development artifacts, older artifacts remain visible to an agent, and the platform tracks and shows the resulting spec-bloat Token Hotspot. It is not a runtime platform dependency or maintained fixture corpus.
_Avoid_: Spec Kit integration, Spec Kit fixture, demo app

**Manual Spec Kit Spec-Bloat Demo Runbook**:
The live presentation path that manually runs Spec Kit, captures telemetry, imports it, runs Repo Context Enrichment, and shows the resulting Token Hotspot and recommendation.
_Avoid_: Product scenario, acceptance fixture

**Spec Artifact Status**:
The scenario classification of a specification-driven development artifact: active when it belongs to the current feature workflow, bloat when it is superseded, stale, duplicate, completed, or archived but still visible to the agent, and neutral when it is not currently referenced but may contribute to repository context size.
_Avoid_: Spec file type, document state

**File Context Category**:
The repo-context classification for a file or path, such as source, generated, lockfile, vendor, binary, or build artifact.
_Avoid_: File type, extension

**Generated Artifact Attribution Policy**:
The rule that generated files, lockfiles, vendor files, binaries, and build artifacts can explain token burn but are excluded from correlated or inferred Token Hotspot attribution unless harness telemetry directly references them.
_Avoid_: Ignore generated files, hide lockfiles

**Tool-Associated Token Burn**:
Token Burn associated with tool behavior through direct evidence or correlation across turns and model invocations. It must be labelled with evidence quality and shown as unavailable when the association cannot be proven or responsibly inferred.
_Avoid_: Tool cost guess, unlabelled tool attribution
