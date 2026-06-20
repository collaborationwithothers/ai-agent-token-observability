### 3. Azure Runtime And Edge Proof

Goal: deploy the full Azure Production MVP infrastructure stage tree with public Front Door ingress and deferred origin isolation hardening.

Parent tracking issue: [#34](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/34).

GitHub milestone: [3. Azure Runtime And Edge Proof](https://github.com/collaborationwithothers/ai-agent-token-observability/milestone/7).

Includes:

- Manual Terraform remote state foundation runbook.
- Foundation stage completion.
- Network private data plane stage.
- Observability foundation stage.
- Data platform stage.
- AI services stage.
- Container Apps environment and three long-running Container Apps.
- Shared jobs image and explicit job command wiring.
- Runtime container image definitions and guarded ACR publish workflow.
- App runtime image digest selection for Terraform deploy.
- Cross-stage Terraform output wiring and deploy contract.
- Guarded Terraform apply workflow from reviewed plan artifacts.
- Azure Front Door Premium WAF.
- Azure Front Door managed certificates for `app`, `api`, and `ingest`.
- Public Front Door routing to generated Azure Container Apps FQDN origins.
- Origin isolation hardening deferred to a later slice.
- Azure DNS records under `tokenobs.consultwithcloud.com`.
- Azure Managed Grafana workspace wiring.
- Edge Origin Validation workflow or proof procedure.
- Infrastructure deletion workflow for disposable stages.

Exit criteria:

- Manual Terraform remote state foundation runbook exists and defines safe backend setup evidence.
- Required Terraform stages are implemented for foundation, retained public DNS, network private data plane, observability foundation, data platform, AI services, app runtime, Managed Grafana, and edge.
- Front Door hostnames work.
- Generated ACA FQDN origin reachability is recorded as current public-origin evidence.
- Auth callbacks use public Front Door hostnames.
- Runtime images are built and published through a guarded workflow before Terraform app runtime deployment consumes digest-pinned image references.
- Normal Terraform apply uses reviewed saved plan artifacts and does not use `terraform apply -auto-approve`.
- Guarded deletion workflow cannot delete retained shared resources.

Milestone 3 dependency rule:

Milestone 3 implementation issues must not start until their listed Milestone 0, 1, and 2 dependencies are complete.

Milestone 3 child issues:

| Issue | Child issue | Outcome | Dependencies |
| --- | --- | --- | --- |
| [#138](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/138) | Manual Terraform remote state foundation runbook | Operators have the manual remote state setup and evidence runbook required before Azure-backed Terraform workflows run | #21, #22, #23 |
| [#139](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/139) | Foundation stage completion | Foundation stage owns shared foundation resources, ACR, Key Vault, identity or identity references, and downstream non-secret outputs | #21, #22, #23, #138 |
| [#140](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/140) | Network private data plane Terraform stage | VNet, subnets, and network boundaries are deployable for downstream stages; private network hardening is deferred | #21, #22, #23, #138, #139 |
| [#141](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/141) | Observability foundation Terraform stage | Log Analytics, Application Insights, Azure Monitor workspace or managed Prometheus foundation exists for app diagnostics, Grafana, SLOs, and alerts | #21, #22, #23, #138, #139, #140 |
| [#142](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/142) | Data platform Terraform stage | PostgreSQL, product Blob Storage, backup settings, lifecycle foundations, public allowlists, and safe outputs are deployable | #21, #22, #23, #39, #138, #139, #140, #141 |
| [#143](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/143) | AI services Terraform stage | Azure AI service dependencies, model deployment aliases, managed identity access, public allowlists where required, diagnostics, and safe outputs are deployable | #21, #22, #23, #138, #139, #140, #141 |
| [#54](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/54) | Container Apps runtime environment and long-running services | ACA environment and Product Dashboard, Product API, and Product Ingestion Endpoint apps are provisioned | #19, #21, #22, #23, #41, #48 |
| [#55](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/55) | Shared jobs image and explicit Container Apps job commands | ACA jobs runtime shape uses one shared jobs image with explicit job commands | #19, #21, #22, #23, #39, #43 |
| [#125](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/125) | Runtime container image definitions and ACR publish workflow | Runtime images have repository-owned build definitions and a guarded ACR publish workflow with immutable SHA tags and digest outputs | #19, #21, #22, #23, #54, #55 |
| [#144](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/144) | App runtime image digest selection for Terraform deploy | Operators can choose a successful ACR image publish run through a CLI helper, and Terraform deploy consumes the derived digest-pinned image artifact | #22, #23, #54, #55, #125, #126 |
| [#56](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/56) | Azure Front Door Premium WAF routes and rate limits | Front Door Premium WAF routes public traffic to product endpoints | #21, #22, #23, #41, #48, #54 |
| [#57](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/57) | Front Door managed certificates and product hostnames | Front Door managed certificates bind product hostnames for first release | #21, #23, #54, #56 |
| [#58](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/58) | Deferred ACA origin isolation hardening | Reintroduces an enforceable origin isolation design and direct-origin bypass proof | #21, #22, #23, #41, #54, #56 |
| [#59](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/59) | Azure DNS records for delegated tokenobs subdomain | Azure DNS records under `tokenobs.consultwithcloud.com` support app, api, and ingest hostnames | #21, #23, #56, #57 |
| [#60](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/60) | Edge origin validation workflow | Manual guarded workflow proves Front Door works and records generated ACA FQDN origin evidence | #22, #23, #41, #54, #56, #58, #59 |
| [#126](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/126) | Guarded Terraform apply workflow from reviewed saved plan artifacts | Manual guarded deploy workflow applies exact same-run saved plan artifacts for normal infrastructure changes after protected environment approval | #21, #22, #23, #54, #56, #57, #58, #59, #125 |
| [#61](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/61) | Guarded infrastructure deletion workflow for disposable stages | Manual guarded deletion workflow creates destroy plans and applies approved same-run destroy plan artifacts while retaining shared resources | #21, #22, #23, #54, #56, #59 |
| [#62](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/62) | Azure Managed Grafana workspace and data source wiring | Managed Grafana consumes observability foundation outputs and approved aggregate metrics data sources without exposing raw session or content data | #21, #22, #23, #26, #52, #54, #141 |
| [#145](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/145) | Cross-stage Terraform output wiring and deploy contract | Completed stages are wired through explicit remote-state outputs and deploy-script inputs so full-stage deployment does not require manual variable reconstruction | #139, #140, #141, #142, #143, #144, #62, #54, #56, #57, #58, #59 |

Image build and publish, normal Terraform deploy, Terraform deletion, retained public DNS, and edge origin validation are separate workflow paths. The normal Terraform deploy workflow produces reviewed saved plan artifacts and applies only those artifacts after protected environment approval; it does not publish images.

### 4. Observability And Grafana

Goal: make aggregate product and platform metrics visible without exposing session details or ranking individuals.

Parent tracking issue: [#35](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/35).

GitHub milestone: [4. Observability And Grafana](https://github.com/collaborationwithothers/ai-agent-token-observability/milestone/8).

Milestone 4 child issues:

Dependency rule: Milestone 4 implementation issues must not start until their listed metric, infrastructure, identity, and Product Dashboard dependencies are complete.

Infrastructure prerequisite: [#62](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/62) is tracked under Milestone 3 because it provisions the Managed Grafana workspace and data source wiring as part of the full Azure infrastructure stage tree.

| Issue | Workstream | Required outcome | Depends on |
| --- | --- | --- | --- |
| [#26](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/26) | Aggregate token timeline and model operations metrics | Product emits aggregate metrics needed by Grafana without session, developer, or raw content labels | #39, #48, #50, #51, #52 |
| [#63](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/63) | Grafana provider authentication proof gate | Terraform dashboard management proves Entra or workload identity first, with service-account-token fallback only if proof fails | #21, #23, #24, #62 |
| [#64](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/64) | Repo-versioned Grafana dashboard JSON deployment | First-release dashboard JSON artifacts deploy through Terraform from repo source and #64 consumes the #63 authentication contract | #26, #52, #62, #63 |
| [#65](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/65) | Grafana RBAC with environment-scoped Entra groups | Grafana built-in roles map to environment-scoped Entra groups and remain separate from Product API authorization | #40, #42, #62 |
| [#66](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/66) | Product Dashboard Grafana link allowlist and authorization | Grafana links pre-filter Product Dashboard views without bypassing Product API authorization or leaking forbidden identifiers | #25, #41, #42, #62, #64 |

Includes:

- Aggregate Metrics Contract implementation.
- Azure Monitor workspace or managed Prometheus integration.
- Azure Managed Grafana dashboards and data source use of the Milestone 3 workspace foundation.
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
| [#89](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/89) | Production smoke tests and edge readiness gate | Public hostnames, Front Door path, generated ACA FQDN origin health, deferred bypass-hardening status, and auth callbacks are validated safely | #56, #57, #58, #59, #60, #73, #82 |
| [#127](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/127) | Deployment sequencing runbook and release evidence handoff | Runbook orders remote state setup, image publish, Terraform plan/apply, image digest propagation, edge validation, smoke tests, and sanitized evidence handoff | #31, #60, #82, #89, #125, #126 |
| [#90](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/90) | Final production readiness checklist and release gate | Final release gate maps all MVP readiness criteria to evidence and blocks readiness on failed checks | #24, #31, #45, #60, #61, #81, #82, #83, #84, #85, #86, #87, #88, #89, #127 |

Includes:

- Container Apps liveness and readiness probes.
- Day-1 internal SLO queries or metrics.
- Azure Monitor alert rules.
- Private action groups.
- PostgreSQL non-production restore drill.
- Blob lifecycle validation.
- Required incident runbooks.
- Audit export foundation.
- Deployment sequencing runbook and sanitized release evidence handoff.
- Final production readiness checklist.

Exit criteria:

- Day-1 alerts are deployed and private.
- Restore and lifecycle validation pass in non-production.
- Required runbooks exist.
- Production incidents do not create public GitHub issues automatically.
