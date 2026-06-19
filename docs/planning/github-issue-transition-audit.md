# GitHub Issue Transition Audit

## Purpose

This document records the issue tracker transition from the superseded local-first backlog to the Azure Production MVP backlog.

It exists so production issue creation does not accidentally mix old local-only implementation work with the new production-only direction.

## Audit Inputs

Repository remote:

- `https://github.com/collaborationwithothers/ai-agent-token-observability`

Commands used:

- `git remote -v`
- `gh issue list --state all --limit 200 --json number,title,state,labels,url,milestone,updatedAt`
- `gh issue view <number> --json number,title,state,labels,url,body,updatedAt` for open issues 8 through 15.

## Initial Tracker State

The initial audit found 15 issues in the GitHub issue list.

- 7 closed issues: 1 through 7.
- 8 open issues: 8 through 15.
- No audited issue has a milestone assigned.

## Transition Update

The production transition created replacement issues 25 through 31 and closed old local-first issues 8 through 15 as `not planned` with superseded comments.

Replacement production issues:

| Issue | Milestone | Production concept |
| --- | --- | --- |
| [#25 Product Dashboard overview and investigation shell](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/25) | 6. Product Dashboard And Session Investigation | Product Dashboard overview, route shell, role-aware query state, and non-punitive UX |
| [#26 Aggregate token timeline and model operations metrics](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/26) | 4. Observability And Grafana | Aggregate token timeline, dense buckets, moving averages, and model operations metrics |
| [#27 Pricing basis and model cost visibility](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/27) | 5. Content, Hotspots, Recommendations, And Pricing | Pricing basis, automated provider price seeding, customer overrides, and cost state semantics |
| [#28 Token hotspots and prompt cache diagnostics](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/28) | 5. Content, Hotspots, Recommendations, And Pricing | Token Hotspots, Prompt Cache Breakage evidence states, and confirmed-versus-candidate boundaries |
| [#29 Recommendation engine evidence and review workflow](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/29) | 5. Content, Hotspots, Recommendations, And Pricing | Deterministic and LLM-assisted recommendation workflow with evidence and validation gates |
| [#30 Session evidence and investigation drill-down](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/30) | 6. Product Dashboard And Session Investigation | Session timeline, evidence summaries, hotspot links, cache diagnostics, and redaction-aware drill-down |
| [#31 Production dashboard verification and readiness reconciliation](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/31) | 7. Day-1 Operations And Production Readiness | Production dashboard verification, role paths, redaction states, readiness docs, and runbook reconciliation |

## Roadmap Implementation Expansion

The replacement production issues above carried forward the surviving concepts from the local-first backlog. Later roadmap grooming created additional production implementation child issues under the production parent milestones. These are not one-for-one replacements for local-first issues.

Milestone 5 expansion issues:

| Issue | Parent | Production concept |
| --- | --- | --- |
| [#67 Content capture policy and Codex candidate extraction](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/67) | [#36](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/36) | Content Capture Policy defaults, policy-scoped candidate extraction, and metadata-only denial states |
| [#68 Inline pre-storage redaction pipeline and failure gate](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/68) | [#36](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/36) | Inline deterministic, Azure AI, and product-specific redaction before storage |
| [#69 Captured content storage, retention, and review workflow](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/69) | [#36](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/36) | Redacted blob storage, retention, content references, and privileged review actions |
| [#70 Recommendation model policy and LLM validation gates](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/70) | [#36](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/36) | Recommendation Model Policy, evidence packets, structured outputs, and LLM validation gates |
| [#71 Automated pricing seed refresh and pricing update review](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/71) | [#36](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/36) | Candidate pricing refresh, approval, rejection, overrides, and historical pricing basis |
| [#72 Non-punitive budget alert policies and evaluation](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/72) | [#36](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/36) | Aggregate budget thresholds and alerts without individual ranking or blame workflows |

Milestone 6 expansion issues:

| Issue | Parent | Production concept |
| --- | --- | --- |
| [#73 Dashboard app shell, bootstrap, and route authorization](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/73) | [#37](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/37) | React/Vite app shell, `/api/v1/me` bootstrap, tenant context, and route authorization |
| [#74 Overview route aggregate summaries and Grafana filter entry](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/74) | [#37](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/37) | Authorized aggregate overview and safe Grafana-originated filter handling |
| [#75 Session search route and non-punitive filters](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/75) | [#37](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/37) | Role-scoped session search, safe filters, cursor pagination, and no people-ranking sort modes |
| [#76 Session investigation detail panels and evidence states](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/76) | [#37](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/37) | Session investigation panels, timeline, hotspots, cache diagnostics, content states, recommendations, and audit context |
| [#77 Content review queue and reviewer decision UI](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/77) | [#37](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/37) | Authorized content review queue and reviewer decisions without raw failed content |
| [#78 Recommendation review and regeneration UI](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/78) | [#37](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/37) | Recommendation queue, recommendation detail, evidence limitations, and asynchronous regeneration |
| [#79 Pricing and non-punitive budget administration UI](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/79) | [#37](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/37) | Pricing review and non-punitive budget policy administration |
| [#80 Identity, harness setup, and audit administration UI](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/80) | [#37](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/37) | Identity, role mapping, harness setup, credential lifecycle, and audit search administration |
| [#81 Role visibility and non-punitive dashboard guardrail tests](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/81) | [#37](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/37) | Cross-route authorization, Grafana-link safety, content boundary, and non-punitive UI tests |

Milestone 7 expansion issues:

| Issue | Parent | Production concept |
| --- | --- | --- |
| [#82 Container Apps health endpoints and probe configuration](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/82) | [#38](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/38) | Safe liveness and readiness endpoints plus Terraform-managed Container Apps probes |
| [#83 Day-1 internal SLO metrics and queries](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/83) | [#38](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/38) | Internal SLO metric and query contracts without customer SLA claims |
| [#84 Azure Monitor alerts and private action groups](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/84) | [#38](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/38) | Minimum Azure Monitor alert set and private notification action groups |
| [#85 PostgreSQL non-production restore drill](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/85) | [#38](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/38) | Non-production PostgreSQL restore drill and recovery evidence |
| [#86 Blob lifecycle validation and retention proof](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/86) | [#38](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/38) | Captured-content lifecycle validation and metadata retention proof |
| [#87 Audit export foundation and sanitized operational evidence](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/87) | [#38](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/38) | Bounded authorized audit export with sanitized operational evidence |
| [#88 First-release incident runbooks](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/88) | [#38](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/38) | First-release incident runbooks for expected operational failure modes |
| [#89 Production smoke tests and edge readiness gate](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/89) | [#38](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/38) | Public hostname, Front Door, generated ACA FQDN origin, deferred bypass-hardening status, and auth callback validation |
| [#90 Final production readiness checklist and release gate](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/90) | [#38](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/38) | Final readiness evidence mapping and release gate for first production use |

## Transition Rule

Do not treat the old local-first issues as the active production backlog.

The correct transition action is:

- Closed local-first issues remain closed as historical implementation records.
- Open local-first issues should receive a superseded comment and then be closed as `not planned` after the production parent issue exists.
- Superseded issue comments should link to the relevant production parent or child issue.
- Do not mark old local-first issues as duplicates unless a new issue has the same outcome and the old issue can truthfully be described as a duplicate.
- Concepts that remain valid must be re-expressed in new production issue bodies, not carried forward by leaving local-first issues open.

Every GitHub issue or comment posted by AI triage must include this disclaimer at the top:

```markdown
> *This was generated by AI during triage.*
```

## Issue Mapping

| Issue | State | Audit classification | Production transition |
| --- | --- | --- | --- |
| [#1 Document Copilot Field Mapping and MVP Data Model](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/1) | Closed | Historical local-first documentation work | Keep closed. Production docs supersede it. |
| [#2 Create Local App Skeleton With Aspire, PostgreSQL, API, and Blazor](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/2) | Closed | Historical local-first skeleton work | Keep closed. Production skeleton work belongs in Milestone 0 child issues. |
| [#3 Import Happy Path Copilot JSONL Into Normalized Sessions](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/3) | Closed | Historical direct-import work | Keep closed. Production ingestion starts with Codex OTLP/HTTP. |
| [#4 Handle Missing Token Metrics Without Treating Them as Zero](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/4) | Closed | Concept remains valid | Keep closed. Carry null-versus-zero semantics into production ingestion and API issues. |
| [#5 Enforce Metadata-Only Privacy Defaults](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/5) | Closed | Concept remains valid | Keep closed. Carry privacy defaults into content capture, ingestion, and dashboard issues. |
| [#6 Add Repo Context Enrichment for Workspace Repos](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/6) | Closed | Concept partially remains valid | Keep closed. Re-express as production repository discovery, allowlist, and policy-scope work where needed. |
| [#7 Detect Spec-Bloat Token Hotspots and Deterministic Recommendations](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/7) | Closed | Concept remains valid but implementation direction changed | Keep closed. Re-express under production hotspot and recommendation engine issues. |
| [#8 Make the Manual Spec Kit Demo Runbook Executable End to End](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/8) | Closed as `not planned` | Superseded local demo/runbook work | Superseded by [#31](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/31). |
| [#9 Add Dashboard Overview Shell With Query State](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/9) | Closed as `not planned` | Superseded Local Dashboard Blazor work | Superseded by [#25](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/25). |
| [#10 Render Dense Token Burn Timeline and 30-Day Moving Average](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/10) | Closed as `not planned` | Superseded local dashboard visualization work | Superseded by [#26](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/26). |
| [#11 Add Typed Burn Hero, Estimated Cost, and Model Cost Mix](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/11) | Closed as `not planned` | Superseded local dashboard cost view work | Superseded by [#27](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/27). |
| [#12 Add Hotspot Driver View and Recommendation Action Section](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/12) | Closed as `not planned` | Superseded local dashboard hotspot work | Superseded by [#28](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/28) and [#29](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/29). |
| [#13 Add Sessions Evidence Table and Fragment Drill-Down](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/13) | Closed as `not planned` | Superseded local dashboard evidence work | Superseded by [#30](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/30). |
| [#14 Apply Analytical Sepia Visual System and Responsive Layout](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/14) | Closed as `not planned` | Superseded local visual redesign work | Superseded by [#25](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/25). The old local Blazor visual system is not carried forward. |
| [#15 Verify Dashboard Redesign in Browser and Reconcile Docs](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/15) | Closed as `not planned` | Superseded local verification work | Superseded by [#31](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/31). |

## Recommended Sequence

1. Create the Milestone 0 parent tracking issue.
2. Create the Milestone 0 child issues.
3. Create the production issues that carry forward dashboard, hotspot, recommendation, pricing, and content concepts. Completed with issues 25 through 31.
4. Add superseded comments to open local-first issues 8 through 15. Completed.
5. Close issues 8 through 15 as `not planned` after posting superseded comments that link to the replacement production issues. Completed.
6. Do not reopen closed issues 1 through 7.
7. Keep [#24 Labels and milestones setup](https://github.com/collaborationwithothers/ai-agent-token-observability/issues/24) open until the documentation changes that record this tracker cleanup are committed or merged.

## Non-Negotiable Guardrails

- Do not leave local-first issues open as active production work.
- Do not rely on closed local-first issues as production specifications.
- Do not close open issues without linking to the production issue or milestone that replaces the surviving concept.
- Do not use issue tracker cleanup to erase historical decisions; keep closed issues as historical records.
