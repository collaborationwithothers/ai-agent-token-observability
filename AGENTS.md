# Repository Instructions

Truth and correctness come before fluency, completeness, or politeness.

If an instruction cannot be followed, say so explicitly and stop. Do not guess, infer, or partially comply.

For factual answers, perform a real web search unless there is nothing searchable. Include real, verifiable citations. For repository facts, cite local files or verified command output. If web access is not possible and the request needs web verification, say so and stop.

When uncertain, say "I don't know." Do not invent answers.

If the user is wrong, say so plainly and explain why.

Prefer stopping over giving a partial or non compliant answer.

The current message takes priority over past context unless explicitly stated otherwise.

If I am wrong, stand your ground. Do not appease me. The truth is of utmost importance.

If the user uploads a file, index it even if it has been indexed previously, because it might contain updated data.

Use standard ASCII punctuation only. Do not use em dashes or en dashes.

## Project Shape

This repository is being reworked into an Azure Production MVP for AI coding-agent token observability.

The previous local-first MVP is superseded. Do not reintroduce supported local-only mode. Do not treat the removed .NET Aspire AppHost, Blazor Local Dashboard, direct file import worker, local storage/import model, or Copilot JSONL tests as production architecture.

First production harness: Codex CLI.

Target-state harnesses: VS Code Copilot, Claude Code, and Codex. Do not implement VS Code Copilot or Claude Code adapters before Codex production ingestion is implemented.

## Context Loading

Keep the always-loaded context small.

- Use `docs/agent/source-map.md` to choose the smallest source-of-truth document set for the touched boundary.
- Do not read every source-of-truth document before knowing the change boundary.
- Treat superseded local-first, Aspire, Blazor, demo, and future-adapter documents as historical unless the user asks for that history.
- Prefer `rg -n` and focused line ranges over full file reads.
- Do not stream full Terraform plans, broad source searches, full worktree lists, full issue lists, or full validation logs when targeted evidence is enough.

## Build And Test

Prerequisites:

- .NET SDK 10.0.300 or newer in the .NET 10 SDK line.
- Docker-compatible container runtime for local PostgreSQL.

Use these commands for the production-shaped active tree:

- `dotnet restore AiAgentTokenObservability.slnx`
- `dotnet build AiAgentTokenObservability.slnx --no-restore`
- `dotnet test AiAgentTokenObservability.slnx --no-restore`
- `npm --prefix web/token-observability-dashboard ci`
- `npm --prefix web/token-observability-dashboard run build`

These commands are not production deployment commands.

## Ready-For-Agent Issue Work

When the user asks to work the next issue, implement a ready-for-agent issue, review a worktree, fix review feedback, or create a PR for issue work, use `.agents/skills/review-worktree-issue-pr/SKILL.md`.

For infrastructure-readiness issues, also use `.agents/skills/infrastructure-readiness-issue/SKILL.md`.

Before code changes or Code Reviewer, produce an Issue Start Packet:

- Current repository path.
- Issue number, title, labels, URL, and acceptance criteria from `gh issue view`.
- Active worktree and current branch/status.
- Diff scope from `git diff --stat HEAD`.
- Untracked implementation files from `git ls-files --others --exclude-standard`.
- Acceptance matrix: criterion, implementation file, test evidence, and docs or schema evidence.
- Focused validation commands for the changed surface.

Use `scripts/session-digest.sh ISSUE_NUMBER` or `scripts/issue-start.sh --compact ISSUE_NUMBER` when an issue number is known. Use `scripts/issue-start.sh --minimal ISSUE_NUMBER` for issue selection or resume checks when the full issue body is not needed yet. If full issue-start output is needed, summarize noisy sections instead of pasting the whole output.

## Subagents

Use subagents only for parallel review, independent research, or Code Reviewer.

Do not use planner or implementor subagents for normal ready-for-agent issue work. The issue planning, risk classification, implementation handoff, and narrow implementation loop live in the repo skills:

- `.agents/skills/review-worktree-issue-pr/SKILL.md`
- `.agents/skills/infrastructure-readiness-issue/SKILL.md`

Use the main agent to produce the planning handoff and implement from it. Escalate internally to the high-risk workflow in the relevant skill for security, privacy, tenant-boundary, authorization, persistence, migration, Terraform provider behavior, production architecture, token metric state changes, or Azure infrastructure behavior.

Do not spawn explorer agents unless a specific independent unknown remains after the skill-driven planning handoff.

If a subagent wait or close attempt fails once, stop waiting and continue with verified local evidence.

Only Code Reviewer should write `Comments.md`. The main agent remains responsible for final validation, review orchestration, PR creation, and closing-reference verification.

## Validation And Review

Prefer focused validation while iterating:

- `scripts/validate-focused.sh ingestion`
- `scripts/validate-focused.sh api`
- `scripts/validate-focused.sh dashboard`
- `scripts/validate-focused.sh terraform`
- `scripts/validate-focused.sh docs`
- `scripts/validate-focused.sh all`

For Terraform changes, prefer machine-checkable evidence: `terraform show -json` with `jq` assertions, targeted plan summaries, or narrow grep checks. Do not paste or rely on full plan text unless the full plan is the artifact under review.

Before Code Reviewer:

- Confirm target worktree and branch.
- Review `git diff --stat HEAD`.
- Include untracked implementation files in the review prompt.
- Confirm the acceptance matrix is covered.
- Run focused validation for the changed surface.

Run Code Reviewer once after focused validation. The reviewer must write findings to `Comments.md` only. `Comments.md` is transient review output and must not be committed.

If Code Reviewer reports `CHANGES_REQUESTED`, track each finding as accepted or rejected with reason, fix accepted findings, rerun focused validation, then request one targeted rereview. Use full-diff rereview only for architecture, security, privacy, tenant-boundary, persistence, Terraform deployment behavior, or product authorization changes.

## PR Gate

Before creating or updating a PR:

- `Comments.md` must be untracked or ignored and must not be staged.
- Run `scripts/validate-pr.sh` unless the change is documentation-only and acceptance criteria do not require build/test evidence.
- Code Reviewer must report `APPROVE`, or remaining findings must be explicitly rejected with defensible reasons.
- The PR body must close only intended issue or issues.
- After `gh pr create`, run `gh pr view PR_NUMBER --json closingIssuesReferences` and verify closing references.

## Implementation Rules

Preserve metadata-only capture by default. Content Capture Mode is disabled by default and must follow Content Capture Policy, pre-storage redaction, and the Redaction Failure Gate before any Captured Content Blob is stored.

Do not persist prompt text, code content, command output, or tool results unless Content Capture Mode is explicitly enabled and redaction succeeds.

Do not silently scrape Git config, OS users, shell environment, or unrelated local files for identity or path data.

Represent unavailable token metrics as null, not zero.

Keep observed, estimated, unavailable, not applicable, and mixed metric states distinct.

Keep Repo Context Enrichment separate from telemetry ingestion. Scanner findings are not harness-emitted facts.

Scoped Ingestion Credential identity is authoritative for production telemetry upload and session ownership. Harness-emitted identity is evidence, not authorization authority.

Prefer deterministic, evidence-backed behavior over LLM-generated findings. LLM-inferred hotspots must remain clearly labelled as candidates until product validation confirms them.

Keep changes narrowly scoped to the production boundary involved: Product API, Product Ingestion Endpoint, Product Jobs, React Product Dashboard, domain contracts, storage, Terraform, operations docs, or transition cleanup.
