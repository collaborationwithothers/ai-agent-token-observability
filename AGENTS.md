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

After any implementation change and before opening, updating, or declaring a PR ready, run the Codex `Code Reviewer` subagent against the current branch diff.

The reviewer must write findings to `Comments.md` only. `Comments.md` is transient review output and must not be committed.

If the reviewer reports `CHANGES_REQUESTED`, either fix the findings and rerun validation, or explicitly document why a finding is not accepted before proceeding.

### Ready-For-Agent Issue Workflow

When the user asks to work the next issue, implement a ready-for-agent issue, review a worktree, or create a PR for issue work, use the repo-local skill `.agents/skills/review-worktree-issue-pr/SKILL.md`.

Before changing code, produce an Issue Start Packet and use it as the working checklist:

- Current repository path.
- Issue number, title, labels, URL, and acceptance criteria from `gh issue view`.
- Active worktree from `git worktree list --porcelain`.
- Branch and worktree state from `git status --short --branch`.
- Diff scope from `git diff --stat HEAD`.
- Untracked implementation files from `git ls-files --others --exclude-standard`.
- Acceptance matrix: criterion, implementation file, test evidence, and docs or schema evidence.
- Focused validation commands for the changed surface.

Use `scripts/issue-start.sh ISSUE_NUMBER` to generate the packet when an issue number is known. Do not spawn Code Reviewer until the Issue Start Packet exists.

### Subagent Budget

Use subagents only for parallel review or independent research. Do not use implementation subagents for sequential issue work unless the user explicitly asks for them.

If a subagent wait or close attempt fails once, stop waiting on that subagent and continue with verified local evidence.

For Code Reviewer, request one full review after focused validation. If it reports `CHANGES_REQUESTED`, fix the findings, rerun validation, and request one targeted rereview using the prior findings ledger.

### Token And Time Budget

Treat validation output, Terraform plans, and subagent reviews as budgeted resources. Do not stream large command output into the conversation when targeted evidence is enough.

- Confirm the actual worktree before the first edit. If the wrong checkout was edited, stop and correct the workspace before doing more implementation work.
- Prefer focused validation during iteration. Run `scripts/validate-pr.sh` once after reviewer approval and before PR creation or PR update, unless a risky fix invalidates the prior full validation.
- For Terraform changes, prefer machine-checkable evidence such as `terraform show -json` with `jq` assertions, targeted `terraform plan` summaries, or narrow grep checks. Do not paste or rely on full plan text unless the full plan is the artifact under review.
- Cap noisy command output with tool output limits. Rerun a command with narrower filters instead of increasing output when only a few facts are needed.
- Do not run broad source searches, full file reads, full validation, or reviewer passes to discover acceptance criteria. Use the Issue Start Packet and relevant source-of-truth docs first.
- If Code Reviewer output appears stale, verify the current branch diff locally before spawning another reviewer.
- Do not spawn explorer or implementation subagents for sequential issue work unless the user explicitly asks for them or the work is truly parallel and independent.

### Planning And Implementation Agents

Project-scoped custom agents live under `.codex/agents/`.

For ready-for-agent GitHub issue work, use `Issue Planner` when the user asks for a planning subagent or when a detailed implementation handoff is explicitly requested. `Issue Planner` is read-only and must produce the Issue Start Packet, acceptance matrix, validation plan, risk checklist, and `IMPLEMENTOR HANDOFF` before implementation begins.

Use implementation agents only when the user explicitly asks for implementation subagents:

- Use `Issue Implementor` for narrow, low-risk, well-scoped, or mostly mechanical changes after a planner handoff exists.
- Use `Issue Implementor High` for security, privacy, tenant-boundary, authorization, persistence, migration, Terraform provider behavior, production architecture, or token metric state changes after a planner handoff exists.

Implementation agents must not run Code Reviewer, write `Comments.md`, commit, push, or create PRs unless explicitly instructed. The main agent remains responsible for final validation, review orchestration, PR creation, and closing-reference verification.

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
- Within the single allowed rereview pass, use a full-diff rereview only when the fix changes architecture, security, privacy, tenant-boundary behavior, persistence, Terraform deployment behavior, or product authorization. Otherwise restrict rereview to prior `Must Fix` findings, the fix delta, and direct regressions in touched files.

### PR Gate

Before creating or updating a PR for implementation work:

- `Comments.md` must be untracked or ignored, and must not be staged.
- Run `scripts/validate-focused.sh PROFILE` for the changed surface before the first review.
- Run `scripts/validate-pr.sh` before PR creation unless the change is documentation-only and the issue acceptance criteria do not require build/test evidence.
- Code Reviewer must report `APPROVE`, or any remaining finding must be explicitly rejected with a defensible reason.
- The PR body must close only the intended issue or issues.
- After `gh pr create`, run `gh pr view PR_NUMBER --json closingIssuesReferences` and verify the closing references.

## Implementation Rules

Preserve metadata-only capture by default. Content Capture Mode is disabled by default and must follow Content Capture Policy, pre-storage redaction, and the Redaction Failure Gate before any Captured Content Blob is stored. Do not persist prompt text, code content, command output, or tool results unless Content Capture Mode is explicitly enabled and redaction succeeds. Do not silently scrape Git config, OS users, shell environment, or unrelated local files for identity or path data.

Represent unavailable token metrics as null, not zero.

Keep observed, estimated, unavailable, not applicable, and mixed metric states distinct.

Keep Repo Context Enrichment separate from telemetry ingestion. Scanner findings are not harness-emitted facts.

Scoped Ingestion Credential identity is authoritative for production telemetry upload and session ownership. Harness-emitted identity is evidence, not authorization authority.

Prefer deterministic, evidence-backed behavior over LLM-generated findings.

LLM-inferred hotspots must remain clearly labelled as candidates until product validation confirms them.

Keep changes narrowly scoped to the production boundary involved: Product API, Product Ingestion Endpoint, Product Jobs, React Product Dashboard, domain contracts, storage, Terraform, operations docs, or transition cleanup.
