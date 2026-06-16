# Repository Instructions

Truth and correctness come before fluency, completeness, or politeness.

If any instruction cannot be followed, say so explicitly and stop. Don't guess, infer, or partially comply.

Always perform a real web search before answering factual questions, except when there is nothing searchable.

Always include real, verifiable citations. Fabricated, placeholder, or inferred citations are unacceptable. For repository facts, cite local files or verified command output.

If web access is not possible and appropriate to the request, say so and stop.

When uncertain, say "I don't know." Don't invent answers.

If the user is wrong, say so plainly and explain why.

Be methodical, factual, and direct. Avoid embellishment.

Use standard ASCII punctuation only.

Don't use em dashes or en dashes.

Use normal contractions like "don't" unless emphasis requires otherwise.

Prefer stopping over giving a partial or non compliant answer.

The current message takes priority over past context unless explicitly stated otherwise.

If I am wrong, stand your ground. Do not appease me. The truth is of utmost importance.

If I upload a file, make sure you index it even if it has been indexed previously, because it might contain updated data.

## Project Shape

This repository is being reworked into an Azure Production MVP for AI coding-agent token observability.

The previous local-first MVP is superseded. The .NET Aspire AppHost, Blazor Local Dashboard, direct file import worker, local storage/import model, and Copilot JSONL tests have been removed from the active code path.

First production harness: Codex CLI.

Target-state harnesses: VS Code Copilot, Claude Code, and Codex.

Do not reintroduce supported local-only mode. Do not implement VS Code Copilot or Claude Code adapters before Codex production ingestion is implemented. Do not treat direct file import, Aspire AppHost, or Blazor Local Dashboard as production architecture.

## Source Of Truth

Read these before changing behavior:

- `CONTEXT.md` for domain language.
- `docs/prd/azure-production-mvp.md` for first-release scope.
- `docs/specs/production-target-state.md` for final production direction.
- `docs/architecture/azure-production-architecture.md` for top-level production architecture.
- `docs/architecture/codex-production-ingestion-contract.md` before changing production ingestion behavior.
- `docs/architecture/data-model.md` before changing persistence or contracts.
- `docs/architecture/production-codebase-transition.md` before deleting, replacing, retaining, or quarantining current local-first code.
- `docs/architecture/implementation-readiness-review.md` before creating GitHub implementation issues.
- `docs/adr/0002-replace-local-first-with-azure-production-saas.md` for the production pivot decision.
- `docs/adr/0003-use-react-spa-for-production-dashboard.md` for the production dashboard stack decision.

Superseded or historical references:

- `docs/prd/local-first-mvp.md`.
- `docs/adr/0001-use-dotnet-aspire-and-blazor-for-local-first-mvp.md`.
- `docs/architecture/copilot-otel-field-mapping.md`.

Use superseded references only as historical context or future-adapter evidence. They must not drive Azure Production MVP implementation.

## Build And Test

Prerequisites:

- .NET SDK 10.0.300 or newer in the .NET 10 SDK line.
- Docker-compatible container runtime for local PostgreSQL.

Use:

- `dotnet restore AiAgentTokenObservability.slnx`
- `dotnet build AiAgentTokenObservability.slnx --no-restore`
- `dotnet test AiAgentTokenObservability.slnx --no-restore`
- `npm --prefix web/token-observability-dashboard ci`
- `npm --prefix web/token-observability-dashboard run build`

These commands validate the production-shaped active tree. They are not production deployment commands.

## Review Before PR

Run the Codex `Code Reviewer` subagent only after an issue-level implementation is complete and the main agent has run focused validation.

Do not run Code Reviewer after every small edit or every individual finding fix.

The reviewer must write findings to `Comments.md` only. `Comments.md` is transient review output and must not be committed.

### Blocking Rules

Only `Must Fix` findings block merge readiness.

`Should Fix`, `Questions`, `Residual Risk`, and unverified areas do not block merge readiness unless the reviewer explicitly shows that they violate the current issue acceptance criteria or create a P0/P1 risk.

The main agent may reject a finding when it provides a concrete reason grounded in code, docs, tests, or accepted scope.

### First Review

Before the first Code Reviewer pass, the main agent must prepare a review packet containing:

- Branch and worktree status.
- Diff stat.
- List of changed and untracked implementation files.
- Issue acceptance matrix.
- Relevant docs read.
- Focused validation commands and results.
- Known intentional exclusions.

The first review prompt must say:

`Review mode: FIRST_PASS. Review only the current issue scope and changed surface. Report all P0, P1, and acceptance-blocking P2 findings in one pass. Do not report speculative or future-roadmap issues as blockers.`

### Rereview

When Code Reviewer reports `CHANGES_REQUESTED`, the main agent must:

- Build a findings ledger.
- Mark each finding as accepted, rejected with reason, or deferred.
- Fix all accepted `Must Fix` findings in one batch.
- Run focused validation once.
- Request a single rereview.

The rereview prompt must say:

`Review mode: REREVIEW. Verify only the prior Must Fix findings, the fix delta, and direct regressions in touched files. Do not restart a full review unless the fix changed architecture, security, privacy, tenant boundary, persistence, Terraform deployment behavior, or product authorization.`

### Review Pass Budget

Use at most two Code Reviewer passes per issue:

1. First pass.
2. One rereview pass.

If the second pass still reports `Must Fix`, stop and produce a human decision summary instead of continuing the agent loop.

### Review Efficiency

Before running Code Reviewer:

- Confirm the target worktree and branch with `git worktree list --porcelain` and `git status --short --branch`.
- Review `git diff --stat HEAD` and include untracked implementation files in the review prompt.
- Build an issue acceptance matrix covering each acceptance criterion, the implementation file, test evidence, and docs or schema evidence.
- Run focused validation for the changed surface before the first reviewer pass.

When Code Reviewer reports `CHANGES_REQUESTED`:

- Track each finding as accepted, rejected with reason, fixed files, added tests, and validation command.
- Rerun validation before requesting re-review.
- In the re-review prompt, include the prior findings ledger and ask the reviewer to verify the fixes plus regressions in touched files.
- Use a full-diff re-review only after all prior findings are closed or when the fix changes architecture, security, privacy, or tenant-boundary behavior.

## Implementation Rules

Preserve metadata-only capture by default. Content Capture Mode is disabled by default and must follow Content Capture Policy, pre-storage redaction, and the Redaction Failure Gate before any Captured Content Blob is stored. Do not persist prompt text, code content, command output, or tool results unless Content Capture Mode is explicitly enabled and redaction succeeds. Do not silently scrape Git config, OS users, shell environment, or unrelated local files for identity or path data.

Represent unavailable token metrics as null, not zero.

Keep observed, estimated, unavailable, not applicable, and mixed metric states distinct.

Keep Repo Context Enrichment separate from telemetry ingestion. Scanner findings are not harness-emitted facts.

Scoped Ingestion Credential identity is authoritative for production telemetry upload and session ownership. Harness-emitted identity is evidence, not authorization authority.

Prefer deterministic, evidence-backed behavior over LLM-generated findings.

LLM-inferred hotspots must remain clearly labelled as candidates until product validation confirms them.

Keep changes narrowly scoped to the production boundary involved: Product API, Product Ingestion Endpoint, Product Jobs, React Product Dashboard, domain contracts, storage, Terraform, operations docs, or transition cleanup.
