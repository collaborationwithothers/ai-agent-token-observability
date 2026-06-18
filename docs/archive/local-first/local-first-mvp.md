# Local-First MVP PRD

Status: superseded by [Azure Production MVP PRD](../../prd/azure-production-mvp.md) and [ADR 0002](../../adr/0002-replace-local-first-with-azure-production-saas.md)

## Problem Statement

AI coding-agent usage creates token burn across prompts, tool calls, repository context, generated artifacts, specifications, and repeated agent behavior. Teams can often see that tokens were used, but they cannot reliably identify which files, folders, tools, specs, or harness behavior caused the burn, whether the evidence is direct or suspected, or what action would reduce it.

The Local-First MVP must prove that this project is an observability and optimization system, not a demo-only dashboard. It must ingest real coding-agent telemetry locally, preserve privacy by default, normalize token data into a durable store, enrich it with repository context, identify Token Hotspots, and produce evidence-backed Deterministic Recommendations.

## Solution

Build a local-first observability platform for VS Code Copilot telemetry. The platform imports Copilot OTel JSONL data through Direct File Import, normalizes session, turn, tool-call, repository, context-source, hotspot, and recommendation records into PostgreSQL, and exposes the results through a Blazor Local Dashboard running under .NET Aspire.

The MVP proves the Optimization Feedback Loop: normalized telemetry leads to Token Hotspots, attribution type, confidence, evidence, recommendations, and expected benefit. The primary product scenario is Spec Kit spec-bloat detection. The live Manual Spec Kit Spec-Bloat Demo Runbook exercises that product scenario after implementation exists, but Spec Kit is not a runtime dependency and the repo does not maintain a separate Spec Kit fixture corpus.

## Purpose

Build the Local-First MVP for AI coding-agent token observability. The MVP proves that VS Code Copilot telemetry can be imported locally, normalized into PostgreSQL, enriched with repo context, and turned into evidence-backed Token Hotspots and Deterministic Recommendations.

The dashboard is the inspection surface. The product proof is the Optimization Feedback Loop from telemetry to hotspot attribution, evidence, confidence, recommendation, and expected benefit.

## Primary User Story

As an AI platform architect, I want to observe token usage across AI coding-agent sessions so that I can identify waste, reduce cost, improve prompt-cache efficiency, and govern agent usage across engineering teams.

## User Stories

1. As an AI platform architect, I want to import VS Code Copilot telemetry locally, so that I can prove token observability without enterprise access or cloud deployment.
2. As an AI platform architect, I want token usage normalized across sessions, turns, models, and tool calls, so that I can compare coding-agent behavior consistently.
3. As an AI platform architect, I want unavailable token metrics stored as unknown instead of zero, so that dashboards do not understate token burn.
4. As an AI platform architect, I want observed, estimated, and mixed token totals labelled separately, so that I can distinguish measured usage from inferred usage.
5. As an AI platform architect, I want repository context enriched separately from telemetry ingestion, so that scanner findings do not get confused with harness-emitted facts.
6. As an AI platform architect, I want Token Hotspots tied to sessions, turns, tools, files, folders, specs, harnesses, and repositories where evidence allows, so that I can see what is causing burn.
7. As an AI platform architect, I want hotspot attribution labelled as direct, correlated, or inferred, so that suspected causes are not presented as proven facts.
8. As an AI platform architect, I want confidence and evidence references on each hotspot, so that I can judge whether the finding is actionable.
9. As an AI platform architect, I want deterministic recommendations with trigger conditions and expected benefit, so that remediation is explainable and repeatable.
10. As an AI platform architect, I want the Local Dashboard to show sessions, token splits, context sources, hotspots, and recommendations, so that I can inspect the Optimization Feedback Loop in one place.
11. As an AI platform architect, I want spec-driven development bloat detected as the primary MVP hotspot rule, so that the MVP proves the product can track and show concrete token hotspots.
12. As an AI platform architect, I want the Spec Kit Spec-Bloat Scenario separated from the demo runbook, so that the product scenario does not become a demo-only app.
13. As a presenter, I want the Manual Spec Kit Spec-Bloat Demo Runbook to execute end to end once implementation exists, so that I can demonstrate the product scenario reliably.
14. As a developer, I want parser behavior blocked until Copilot Field Mapping is documented, so that schema and parser decisions are based on observed fields instead of guesses.
15. As a developer, I want happy path and missing-metric fixtures, so that parser and persistence behavior can be tested without maintaining Spec Kit-only artifacts.
16. As a developer, I want ingestion, persistence, repo enrichment, hotspot detection, recommendation generation, and dashboard access separated into testable modules, so that complex behavior is isolated behind stable interfaces.
17. As a privacy reviewer, I want Content Capture Mode disabled by default, so that prompt text, code content, command output, and tool results are not persisted unless a future opt-in capability is implemented.
18. As an AI platform architect, I want Developer Display Labels, full repo names, and Repo Display Paths when explicitly supplied or clearly emitted by telemetry, so that the MVP remains usable while avoiding silent Git, OS, shell, or unrelated local-file scraping.
19. As an AI platform architect, I want the Local Dashboard to lead with range-scoped Token Burn, dense daily burn, 30-day Moving Burn Average, Token Hotspots, recommendations, and session evidence, so that it behaves like an observability dashboard instead of a raw log viewer.
20. As an AI platform architect, I want Estimated Token Cost and Unavailable Token Cost states, so that dollar estimates are useful without being presented as provider invoices.
21. As an AI platform architect, I want dashboard filters encoded in the URL query string, so that range, repository, harness, model, and Metric Status views survive refresh and can be shared.
22. As a future platform owner, I want the local data model to preserve a path toward OpenTelemetry Collector ingestion and secondary harnesses, so that the MVP does not require a rewrite when Claude Code, Codex, or Azure deployment are added later.

## In Scope

* VS Code Copilot as the Primary Harness.
* Direct File Import of Copilot OTel JSONL fixtures.
* Local PostgreSQL as the Local Store.
* .NET Aspire as the Local App Platform.
* Separate Local Pipeline Projects for ingestion worker, dashboard API, and Blazor Local Dashboard.
* Metadata-only default capture.
* Single MVP privacy mode that allows Developer Display Labels, full repo names, and Repo Display Paths from explicit import or enrichment input, or from telemetry only when the Coding-Agent Harness clearly emits them.
* Content Capture Mode disabled by default.
* Repo Context Enrichment separate from telemetry ingestion.
* Rule 2: Superseded spec bloat as the required MVP hotspot rule.
* Deterministic Recommendations only.
* Primary use case: tracking and showing spec-driven development bloat as a Token Hotspot.
* Dashboard Overview Contract served by `/dashboard/overview`.
* Dashboard Query State for Dashboard Range, repository, Coding-Agent Harness, model, and Metric Status filters.
* Analytical Sepia Palette, Static Dashboard Visuals, Token Burn Heatmap, 30-day Moving Burn Average, Hotspot Driver View, Recommendation Action Section, and same-page Dashboard Drill-Down.
* Estimated Token Cost using a local, versioned Harness Pricing Basis, with Unavailable Token Cost when pricing cannot be matched.

## Out of Scope

* Claude Code adapter.
* Codex adapter.
* OpenTelemetry Collector ingestion.
* Azure deployment.
* LLM-Assisted Recommendations.
* Content Capture Mode implementation.
* People ranking or developer leaderboard views.
* Silent Git config, OS user, shell environment, or unrelated local-file scraping for display identity.
* Spec Kit as a runtime dependency.
* Spec Kit-specific telemetry ingestion.
* Committed Spec Kit demo corpus fixtures.
* Expected hotspot fixtures for the Spec Kit demo.
* Dedicated harness comparison dashboard before a Secondary Harness adapter exists.
* Dedicated prompt-cache view unless needed for the primary use case.
* Dedicated repo-context bloat view unless needed for the primary use case.
* Work Family rollups.
* Rich Dashboard Interaction, including brushing, zooming, cross-filtering, and coordinated panels.
* Dashboard Export Workflow.
* Pricing Refresh Workflow and automatic live provider scraping.
* Chart library selection for the first visual redesign.

## Implementation Decisions

* Build the Local App Platform with .NET Aspire, matching the ADR decision to use a production-shaped local distributed app.
* Keep the Local Pipeline Projects separate: an ingestion worker, a dashboard API, and a Blazor Local Dashboard.
* Use local PostgreSQL as the Local Store so local persistence stays close to the Azure Production Path.
* Start with VS Code Copilot as the Primary Harness. Claude Code and Codex remain Secondary Harnesses after the normalized schema works.
* Use Direct File Import for Copilot OTel JSONL before adding an OpenTelemetry Collector path.
* Treat Copilot Field Mapping as a required implementation gate before parser behavior is considered stable.
* Encapsulate Copilot telemetry parsing behind a stable adapter interface that produces normalized session, turn, model, token, and tool-call records.
* Encapsulate normalized persistence behind a storage module that owns schema writes, nullable token metrics, metric status, token total type, and idempotent imports.
* Encapsulate Repo Context Enrichment behind a module that scans repository files and metadata, classifies context sources, and never treats scanner findings as harness-emitted facts.
* Encapsulate the Hotspot Engine as a deep module that accepts normalized telemetry and repo context evidence, then emits Token Hotspots with attribution type, confidence, and evidence refs.
* Implement Rule 2: Superseded spec bloat as the required MVP hotspot rule.
* Encapsulate Deterministic Recommendations as a rule-based module that emits rule id, trigger condition, recommended action, expected benefit, confidence, and evidence refs.
* Add `/dashboard/overview` as the Dashboard Overview Contract endpoint for the redesigned Local Dashboard.
* Let the dashboard API own Dashboard Range filtering, dense daily Token Burn Timeline buckets, 30-day Moving Burn Average, Metric Quality Marker propagation, filter options, freshness summary, hotspot ranking, and Recommendation Rationale.
* Use the Dashboard Overview Contract as the main Local Dashboard data source. Keep `/sessions` and `/insights` as lower-level or diagnostic endpoints rather than stitching those responses together in Blazor.
* Include repository, Coding-Agent Harness, model, and Metric Status filter option lists in the Dashboard Overview Contract response so the Dashboard Top Rail options match the displayed data set.
* Return raw values plus semantic fields from the API. Blazor owns local timezone presentation, compact token labels, badges, and CSS states.
* Encode Dashboard Query State in the URL query string for Dashboard Range, repository, Coding-Agent Harness, model, and Metric Status.
* Keep the Local Dashboard as a single page route with Dashboard Component Boundaries for `DashboardTopRail`, `TokenBurnHero`, `TokenBurnHeatmap`, `HotspotDriverView`, `RecommendationActionSection`, `SessionsEvidenceTable`, and `GuidedEmptyState`.
* Use Dashboard Style Boundaries: shared theme tokens and base styles in `app.css`, section layout and visual detail in component-scoped `.razor.css` files.
* Use Static Dashboard Visuals rendered in Blazor HTML, CSS, or SVG. Defer chart library selection until Rich Dashboard Interaction or maintainability requires it.
* Keep the Local Dashboard focused on range-scoped Token Burn, Estimated Token Cost, Model Cost Mix, tool behavior summary, Token Hotspots, recommendations, sessions, evidence, and freshness.
* Store metadata only by default for content fields. Raw prompts, code content, command outputs, and full tool results are out of default persistence.
* Allow Developer Display Labels, full repo names, and Repo Display Paths in the single MVP privacy mode when they come from explicit import or enrichment input, or from clearly emitted telemetry fields. Do not silently scrape Git config, OS user accounts, shell environment, or unrelated local files.
* Keep Spec Kit outside runtime dependencies. The Spec Kit Spec-Bloat Scenario defines the product scenario, and the Manual Spec Kit Spec-Bloat Demo Runbook defines the presentation path.
* Preserve future extension points for OpenTelemetry Collector ingestion, Azure deployment, LLM-assisted recommendation text, Claude Code, and Codex without implementing them in the Local-First MVP.
* Split the first Local Dashboard visual redesign into Dashboard Implementation Slices: contract/API, Blazor data/load, visual redesign, then browser verification and documentation reconciliation.

## Testing Decisions

Good MVP tests verify externally observable behavior: imported records, normalized field values, missing-metric semantics, persisted privacy defaults, hotspot outputs, recommendation outputs, dashboard overview data, and dashboard semantics. Tests should not assert private parser internals or incidental implementation details.

Modules requiring MVP tests:

* Copilot parser and field mapper.
* Missing-metric normalization.
* Direct File Import command or worker entry point.
* Normalized PostgreSQL persistence.
* Repo Context Enrichment classification.
* Hotspot Engine, including Rule 2: Superseded spec bloat.
* Deterministic Recommendation generation.
* Privacy defaults for metadata-only capture.
* Dashboard Overview Contract and `/dashboard/overview` endpoint behavior.
* Dashboard Range filtering, dense daily buckets, 30-day Moving Burn Average, Metric Quality Marker propagation, filter options, and Recommendation Rationale.
* Dashboard Query State mapping to API request parameters.
* Local demo automation that starts the Aspire app and loads real parser fixtures.
* End-to-end runbook verification after implementation exists.

Prior art in this repo includes xUnit tests and ASP.NET Core TestServer endpoint tests. The first dashboard redesign slice should prioritize API and contract tests, then add a small Blazor smoke test only if the repo has a practical pattern for it.

## Acceptance Summary

The Local-First MVP is done when Phase 0, Phase 1, and Phase 2 pass together. Phase 1.5 Collector ingestion is excluded from MVP acceptance and remains a post-MVP maturity path.

Required outcomes:

* Copilot Field Mapping is documented before parser behavior is treated as stable.
* MVP parser acceptance uses committed Copilot JSONL fixtures for happy path and missing metrics coverage.
* Direct JSONL import populates the normalized PostgreSQL schema.
* Aspire starts the Local Pipeline Projects and Local Store together.
* `/dashboard/overview` returns the Dashboard Overview Contract with top-level filter options, dense daily buckets, 30-day Moving Burn Average, Metric Quality Markers, Token Hotspots, Recommendation Rationale, sessions, evidence, freshness, and raw semantic values.
* The Local Dashboard renders the Dashboard Overview with Dashboard Top Rail, Typed Burn Total, Dashboard Metric Strip, Token Burn Heatmap, Moving Burn Average, Hotspot Driver View, Recommendation Action Section, SessionsEvidenceTable, Evidence Summary, and Guided Empty States.
* The Local Dashboard uses Dashboard Query State and same-page Dashboard Fragment Targets for drill-down.
* Repo Context Enrichment runs separately from telemetry ingestion.
* Rule 2: Superseded spec bloat produces a Token Hotspot.
* The primary spec-driven development bloat use case produces attribution type, confidence, evidence refs, recommendation, and expected benefit.
* Once implementation exists, the Manual Spec Kit Spec-Bloat Demo Runbook can be executed end to end.

## Fixture Contract

The MVP requires committed Copilot JSONL fixtures for parser and metric behavior:

* Happy path session: includes session, turn, model, input tokens, output tokens, timing, and tool-call metadata.
* Missing metrics session: omits at least one optional token metric and verifies the normalized value is `NULL` with metric status and confidence instead of zero.

The spec-bloat use case does not use a committed repo fixture. It uses the Spec Kit Spec-Bloat Scenario and the Manual Spec Kit Spec-Bloat Demo Runbook.

## Direct Import Contract

* A repeatable CLI or worker command imports one JSONL file or a fixture directory.
* The command accepts a harness value, starting with `copilot`.
* The command accepts workspace, repo, and developer display identity as explicit input when attribution is needed. A workspace repo requires an explicit repo root/path that can be salted and hashed; full repo name and Repo Display Path are allowed display metadata. The command must not derive repo or developer identity from Copilot OTel fields unless those fields are actually present.
* Re-importing the same fixture is idempotent or explicitly replaces the previous import.
* The command reports imported sessions, turns, tool calls, skipped records, warnings, and errors.
* Malformed records do not crash the whole import; they are counted and surfaced.
* The command exits non-zero only for fatal import failures.

## Persistence Contract

* Each fixture import creates persisted `agent_session` and `agent_turn` records. It creates `workspace_repo` records only when the import receives explicit repo root/path, full repo name, repo display metadata, or Repo Context Enrichment supplies repo evidence.
* Fixtures with tool calls create persisted `tool_call` records.
* Repo/context evidence creates persisted `context_source` records.
* The live Spec Kit demo import creates persisted `hotspot` and `recommendation` records when the captured telemetry and Repo Context Enrichment identify spec bloat.
* Unavailable token metrics are stored as `NULL`, never zero.
* Token metric status fields and `token_total_type` are populated.
* `user_hash` is present when developer identity is available, and `developer_display_label` may be present in the single MVP privacy mode.
* `repo_path_hash` is present when a workspace repo is explicitly provided or enriched, and `repo_display_path` or full repo name may be present when supplied explicitly or clearly emitted by telemetry.
* `context_source` records include `file_category` and `eligible_for_inferred_hotspot`.
* `hotspot` records include attribution type, confidence, and evidence refs.
* `recommendation` records include rule id, trigger condition, recommended action, expected benefit, confidence, and evidence refs.
* `estimated_cost_usd` is nullable and uses the local Harness Pricing Basis when matched. Unmatched values produce Unavailable Token Cost rather than fallback average cost.

## Dashboard Contract

Required MVP views:

* Dashboard Top Rail: shows Dashboard Range, repository, Coding-Agent Harness, model, Metric Status filters, Dashboard Timezone, and Dashboard Freshness Summary.
* Headline summary: shows one dominant Typed Burn Total with visible observed, estimated, mixed, and unavailable splits, plus a Dashboard Metric Strip for Estimated Token Cost, active sessions, top Token Hotspot, and Metric Status.
* Token Burn Heatmap: shows dense daily Token Burn Timeline buckets for the selected Dashboard Range, including zero-burn days, as the first major visual band after the headline.
* Moving Burn Average: shows a 30-day Moving Burn Average table or line computed server-side.
* Hotspot Driver View: shows Token Hotspots with compact visual bars and table semantics for exact values, Hotspot Attribution Type, Metric Confidence, and drill-down links.
* Recommendation Action Section: shows Deterministic Recommendations as a full-width action layer before sessions, with Recommendation Rationale, expected benefit, confidence, and evidence drill-down links.
* SessionsEvidenceTable: shows session and evidence detail after hotspots and recommendations, with Developer Display Labels, repo-relative paths in the main dashboard, and absolute Repo Display Paths in expanded evidence detail when available.
* Guided Empty State: points users to import telemetry, run Repo Context Enrichment, or review why no Token Hotspot was found. It must not render demo fixtures or silent sample data.

Required dashboard behavior:

* The main page order is headline summary, daily Token Burn visual, drivers and Token Hotspots, Recommendation Action Section, then sessions and evidence detail.
* Dashboard Query State is encoded in URL query parameters and maps to `/dashboard/overview` request parameters.
* Dashboard Drill-Down uses same-page Dashboard Fragment Targets, with expanded rows or sections for presentation state.
* Estimated Token Cost is shown as dashboard guidance, not provider invoice. When the Coding-Agent Harness, model, token type, billing route, or Harness Pricing Basis cannot be matched, the dashboard shows Unavailable Token Cost.
* Observed, estimated, mixed, and unavailable token totals remain visibly distinct.
* Metric Quality Markers travel with exact token and cost values wherever they appear.
* Unavailable metrics are not displayed as zero.
* The visual redesign uses an Analytical Sepia Palette and Static Dashboard Visuals without adding a chart library.
* The dashboard is desktop-led responsive: desktop and laptop scanning drive layout, while narrow screens stack sections and preserve readable controls and tables.

## Hotspot Rule Contract

Rule 2: Superseded spec bloat is the required MVP hotspot rule.

Acceptance requirements:

* The spec-bloat repo snapshot includes active, superseded, and old spec files.
* Repo Context Enrichment classifies those files as context sources.
* The primary hotspot is the stale spec artifact set, such as `specs/` or `specs/archive-candidate/`.
* The rule creates a hotspot with `source_type = spec` or a folder-level equivalent.
* The hotspot uses `attribution_type = correlated` or `attribution_type = inferred` unless harness telemetry directly references the spec.
* Confidence is populated.
* Evidence refs point to telemetry records, repo context records, or both.
* The recommendation says to create `active-specs.md`, move superseded specs under `specs/archive/`, and configure agent instructions to load only active specs by default.
* Expected benefit is populated.

## Privacy Contract

Default MVP imports are metadata-only for content capture. The MVP has one privacy mode, not separate public/private or trusted modes.

Acceptance requirements:

* Full prompt text is not persisted.
* Full code file content is not persisted.
* Full tool results are not persisted.
* Full command outputs are not persisted.
* Developer Display Labels, full repo names, and Repo Display Paths may be persisted and displayed when supplied explicitly, produced by Repo Context Enrichment, or clearly emitted by telemetry.
* `user_hash` is persisted when Developer Identity is available.
* `repo_path_hash` is persisted when repo path identity is available.
* `content_captured = false` for default fixture imports.
* Content Capture Mode is disabled by default.
* The MVP must not silently scrape Git config, OS user accounts, shell environment, or unrelated local files to populate display identity.
* The MVP should show developer labels in session rows, filters, and expanded evidence detail, but avoid top-level person rankings.
* The main dashboard should prefer repo-relative paths; expanded evidence detail may show absolute local Repo Display Paths when available.
* Redaction tests are post-MVP unless Content Capture Mode is implemented in the MVP.

## Verification Contract

Automated verification is required for MVP acceptance.

Required tests:

* Parser unit tests for Copilot field mapping.
* Parser unit tests for unavailable metric handling.
* Integration test for importing the happy path fixture.
* Integration test for importing the missing metrics fixture.
* API and contract tests for `/dashboard/overview`.
* Range filtering tests.
* Dense daily bucket tests, including zero-burn days.
* 30-day Moving Burn Average tests.
* Metric Quality Marker propagation tests.
* Filter option tests for repository, Coding-Agent Harness, model, and Metric Status.
* Recommendation Rationale tests.
* Manual demo verification for the live Spec Kit spec-bloat path.
* Persistence assertions for required normalized records.
* Rule 2: Superseded spec bloat is verified during the live Spec Kit demo.
* Recommendation assertions for rule id, trigger condition, evidence refs, confidence, recommended action, and expected benefit.
* Privacy assertions for metadata-only default behavior.
* End-to-end runbook verification confirms the Manual Spec Kit Spec-Bloat Demo Runbook can be completed against the implemented app.

Required demo automation:

* Local demo script starts the Aspire app.
* Local demo script can load parser fixtures.
* Local demo script leaves the Local Dashboard ready to inspect the Dashboard Overview, token splits, hotspots, Recommendation Rationale, sessions, and evidence.
* Manual Spec Kit Spec-Bloat Demo Runbook can be completed end to end after the implementation exists.

Manual dashboard inspection and browser screenshots support the visual redesign, but they are not sufficient for MVP acceptance by themselves.

## Primary Use Case Contract

The primary MVP use case is tracking and showing spec-driven development bloat as a Token Hotspot. The Spec Kit Spec-Bloat Scenario defines the product scenario. The Manual Spec Kit Spec-Bloat Demo Runbook is one way to exercise this use case during presentation.

The use case creates or preserves:

* active current feature spec, plan, and tasks
* old specs
* superseded specs
* old plans
* completed task files
* stale design logs
* duplicate generated artifacts
* active specs
* large design logs

The product shows:

* token burn from outdated specs
* attribution type and confidence for the suspected hotspot
* evidence references from telemetry and Repo Context Enrichment
* the stale spec artifact set as the primary hotspot
* recommendation to create `active-specs.md`, move superseded specs under `specs/archive/`, and configure agent instructions to load only active specs by default
* expected benefit: reduce repeated context from stale specs and plans, preserve audit history by archiving instead of deleting, and make the current spec workflow explicit to the agent

Spec Kit Spec-Bloat Scenario:

* The presenter may run Spec Kit manually to create or evolve specs for an internal deployment approval tracker.
* The scenario can show manual approvals, Slack notifications, Azure environment gates, audit logs, RBAC, and emergency override specs accumulating over iterations.
* The presenter can then show how stale specs and plans remain visible to the agent after the current workflow changes.
* Telemetry capture runs while the presenter uses Copilot against the repo with stale Spec Kit artifacts visible.
* The captured telemetry is imported into the observability app.
* Repo Context Enrichment runs against the same repo.
* The dashboard shows the resulting spec-bloat Token Hotspot and recommendation.
* The scenario is documented in `docs/archive/demos/spec-kit-spec-bloat.md`.
* The live demo flow is documented in `docs/archive/demos/manual-spec-kit-spec-bloat.md`.

## Implementation Start Gate

Project skeleton work can start from this PRD.

Parser behavior and schema implementation are blocked until these documents exist:

* `docs/archive/future-adapters/copilot-otel-field-mapping.md`
* initial `data-model.md` or equivalent schema document

Parser behavior should not be considered stable until the Copilot Field Mapping is complete.

The Copilot Field Mapping document must include every fixture-observed Copilot OTel field with:

* source field name
* example value from fixture
* normalized target field
* field category: `documented`, `fixture-observed`, `optional_when_available`, or `content_capture_only`
* required versus nullable status
* metric status behavior when missing
* confidence level for inferred or estimated values
* whether the field is allowed in metadata-only mode
* privacy handling for content, paths, user identity, and repo identity

The initial data model document must include:

* table list and purpose
* columns, types, nullability, and defaults
* primary keys and foreign keys
* indexes needed for MVP dashboard queries
* enum values for metric status, token total type, attribution type, file category, and recommendation type
* privacy-sensitive columns and default storage behavior
* fixture-to-table expected row mapping
* migration strategy for local PostgreSQL
* fields that are MVP-required versus P2

## Further Notes

The PRD intentionally separates the product scenario from the presentation runbook. MVP acceptance can require the runbook to execute end to end after implementation exists, but the runbook must not become a maintained fixture corpus or the source of product behavior.

This PRD has not been published to an issue tracker yet. The current repo does not show issue-tracker setup or a `ready-for-agent` label vocabulary.
