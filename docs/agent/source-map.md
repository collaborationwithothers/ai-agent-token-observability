# Agent Source Map

Use this file to choose the smallest useful context set for a task. Do not load all files in a category by default.

## Always Needed

- `AGENTS.md`: durable repo rules.
- Current user request and current issue body.
- Current worktree status and diff stat.
- Relevant changed files.
- Focused validation evidence.

## Ready-For-Agent Issues

- `.agents/skills/review-worktree-issue-pr/SKILL.md`: issue workflow, review loop, PR gate.
- `.agents/skills/infrastructure-readiness-issue/SKILL.md`: infrastructure-readiness issue workflow and token-budget controls.
- `scripts/issue-start.sh`: issue packet generator.
- `scripts/session-digest.sh`: compact issue packet wrapper for new sessions.
- `scripts/worktree-current.sh`: compact current and issue worktree context.
- `scripts/validate-focused.sh`: scoped validation entry point.
- `scripts/validate-pr.sh`: final PR validation gate.
- `.github/codex/prompts/infrastructure-readiness.md`: GitHub Action prompt file for infrastructure-readiness work.
- `scripts/codex-infra-issue.sh`: local `codex exec` wrapper for infrastructure-readiness work.

## Production Scope

Read only when the touched boundary needs it:

- `CONTEXT.md`: domain language and glossary.
- `docs/prd/azure-production-mvp.md`: first-release product scope.
- `docs/specs/production-target-state.md`: target production direction.
- `docs/architecture/azure-production-architecture.md`: top-level production architecture.
- `docs/architecture/codex-production-ingestion-contract.md`: production ingestion behavior.
- `docs/architecture/data-model.md`: persistence and contracts.
- `docs/architecture/production-codebase-transition.md`: deleting, replacing, retaining, or quarantining local-first code.
- `docs/architecture/implementation-readiness-review.md`: issue readiness and implementation ordering.
- `docs/adr/0002-replace-local-first-with-azure-production-saas.md`: production pivot decision.
- `docs/adr/0003-use-react-spa-for-production-dashboard.md`: production dashboard stack decision.

## Infrastructure And Terraform

Read when changing Azure infrastructure, Terraform stages, modules, or deployment workflows:

- `docs/architecture/terraform-production-infrastructure.md`: index only.
- `docs/architecture/terraform-production-infrastructure/workspaces-stages-and-modules.md`
- `docs/architecture/terraform-production-infrastructure/workflow-guardrails-and-commands.md`
- `docs/architecture/terraform-production-infrastructure/acceptance-and-platform-facts.md`
- `docs/architecture/runtime-topology.md`
- `docs/operations/manual-terraform-remote-state.md`
- `docs/operations/production-operations.md`
- `infrastructure/azure/README.md`
- `infrastructure/azure/modules/**`
- `infrastructure/azure/stages/**`
- `.github/workflows/terraform-*.yml`
- `scripts/terraform-*.sh`
- `scripts/validate-terraform-*.sh`
- `scripts/terraform-stage-check.sh`

Prefer targeted searches and focused line ranges. Do not stream full Terraform plans. Use `terraform show -json` with `jq` assertions or narrow text filters.

## Product Boundaries

Read only when touching the matching boundary:

- Product API: API project files, API tests, API docs, API validation scripts.
- Product Ingestion Endpoint: ingestion contracts, ingestion project files, ingestion tests, Codex production ingestion contract.
- Product Jobs: job project files, job tests, scheduling and worker docs.
- React Product Dashboard: `web/token-observability-dashboard/**`, dashboard build/test scripts, dashboard ADR.
- Domain contracts and storage: data model, persistence docs, database migrations, contract tests.

## Reference-Only By Default

These files can be large or broad. Load focused sections only when needed:

- Full `CONTEXT.md`.
- Full production architecture docs.
- Full runtime topology docs.
- Full Terraform production infrastructure reference files.
- Full planning roadmaps.
- Full operations runbooks.

## Historical Or Future-Adapter Context

Do not use these to drive Azure Production MVP implementation unless the user explicitly asks for historical context or future-adapter evidence:

- `docs/archive/local-first/local-first-mvp.md`
- `docs/archive/local-first/0001-use-dotnet-aspire-and-blazor-for-local-first-mvp.md`
- `docs/archive/future-adapters/copilot-otel-field-mapping.md`
- `docs/archive/demos/**`
- `docs/archive/demos/spec-kit-spec-bloat.md`

## Harmful By Default

Avoid loading or pasting these into the main thread unless they are the artifact under review:

- Full Terraform plan text.
- Full `git worktree list --porcelain` output in repos with many stale worktrees.
- Full `gh issue list` output with large limits.
- Broad `rg docs infrastructure` output.
- Full validation logs when a pass/fail summary and targeted diagnostics are enough.
- Raw subagent notifications when a concise summary will do.

## Ignore Strategy

Use `.gitignore` for generated files, local secrets, local Terraform state, build outputs, transient review output, and large validation artifacts.

Use `.worktreeinclude` only for explicit non-secret ignored local files that Codex-managed worktrees need copied. The current file intentionally lists no paths.

Do not rely on undocumented Codex ignore files. If a future Codex release documents a repo-level context ignore file, add it deliberately and keep this source map as the human-readable routing policy.

Do not ignore tracked source-of-truth docs. Instead, classify them here as reference-only or historical so agents load them only on demand.
