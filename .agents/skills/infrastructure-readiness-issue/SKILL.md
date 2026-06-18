---
name: infrastructure-readiness-issue
description: Use when working the next infrastructure-readiness ready-for-agent GitHub issue in this repo, especially Terraform, deployment, networking, Azure resource, validation, or PR-ready infrastructure work where token budget matters. Provides a compact issue workflow with bounded context, planner/implementor sequencing, focused Terraform validation, Code Reviewer gating, and PR creation.
---

# Infrastructure Readiness Issue

## Goal

Ship one infrastructure-readiness issue with minimal repeated context loading, bounded tool output, and a real PR.

This skill refines the repo ready-for-agent issue workflow for infrastructure work. Prefer the workflow below over broad backlog scans, full source-doc reads, full Terraform plan output, or exploratory subagent fanout.

## Workflow

1. Select one issue.
   - Use a narrow `gh issue list` query with `--limit 10` or less.
   - Prefer issues labelled `ready-for-agent` and obviously blocking infrastructure readiness.
   - After selecting an issue, view only that issue unless dependencies are unclear.

2. Create a compact issue-start packet.
   - Run `scripts/session-digest.sh ISSUE_NUMBER` or `scripts/issue-start.sh --compact ISSUE_NUMBER`.
   - If output is noisy, summarize only: issue metadata, acceptance criteria, current worktree, branch/status, diff stat, untracked files, acceptance matrix, and focused validation commands.
   - Do not paste full worktree lists when many stale worktrees exist. Report current worktree plus matching issue worktree only.

3. Use subagents only in this sequence when the user asks for planner/implementor agents.
   - Spawn `Issue Planner` first.
   - Wait for its `IMPLEMENTOR HANDOFF`.
   - Spawn exactly one implementor after the handoff.
   - Use `Issue Implementor High` for Terraform provider behavior, production architecture, networking, security, privacy, tenant-boundary, authorization, persistence, migration, or token metric state changes.
   - Do not spawn explorer agents unless a specific independent unknown remains after the planner handoff.

4. Keep main-thread context small while the implementor works.
   - Do not reread every file the implementor is reading.
   - Prepare only integration checks, validation commands, and PR gate steps.
   - When the implementor returns, inspect diff stat, changed files, and targeted snippets before deciding whether more reading is needed.

5. Implement or integrate narrowly.
   - Read only source-of-truth docs for the touched infrastructure boundary.
   - Prefer existing Terraform stage and module patterns over new structure.
   - Preserve state-sensitive Terraform addresses unless the issue explicitly requires replacement.
   - For Azure resource additions, prefer AVM through local wrapper modules when an appropriate AVM exists.

6. Validate with bounded output.
   - Run the narrowest relevant focused validation first, usually `scripts/validate-focused.sh terraform`.
   - For Terraform plans, prefer `plan -out`, then `terraform show -json` with `jq` assertions.
   - Use `scripts/terraform-stage-check.sh STAGE` for bounded stage init/validate, and `--plan` only when the required variables are known.
   - If text output is needed, pipe to a narrow filter for resource names, `Plan:`, diagnostics, or required assertions.
   - Do not stream full Terraform plans unless the full plan text is the artifact under review.

7. Review once.
   - Confirm target worktree and branch before review.
   - Confirm `Comments.md` is untracked or ignored.
   - Run Code Reviewer once after focused validation.
   - If review returns `CHANGES_REQUESTED`, create a concise findings ledger, fix accepted findings, rerun focused validation, then request one targeted rereview.

8. Finish with the PR gate.
   - Run `scripts/validate-pr.sh` once after reviewer approval unless the issue is docs-only and acceptance criteria do not require build/test evidence.
   - Commit only relevant files.
   - Create the PR.
   - Verify `closingIssuesReferences` closes only the intended issue.

## Output Rules

- Report command results as targeted evidence, not raw logs.
- Cap noisy command output and rerun with narrower filters instead of increasing output.
- Avoid broad `rg docs infrastructure` searches. Search specific docs or paths.
- Avoid full file reads when `rg -n` plus focused `sed -n` ranges are enough.
- Avoid repeated reads of `CONTEXT.md`, architecture docs, and Terraform docs across main and subagents.

## Context Routing

Always needed:
- This issue's body and acceptance criteria.
- Current branch/worktree status.
- Changed Terraform stage/module files.
- Focused validation evidence.

Task-only:
- Relevant Terraform stage docs.
- Current AVM/provider facts for resources being changed.
- Nearby stage/module examples.
- `.github/workflows` and scripts only when workflow behavior is touched.

Reference-only:
- Full `CONTEXT.md`.
- Full production architecture docs.
- Full runtime topology docs.
- Data model docs unless persistence/contracts are touched.

Historical:
- Local-first MVP docs.
- Superseded Aspire/Blazor docs.
- Future-adapter mapping docs.
- Demo or spec-bloat notes.

Harmful by default:
- Full Terraform plan text.
- Full ready issue lists.
- Full worktree lists.
- Raw subagent notifications copied into the main thread.
- Broad search output from the entire `docs` and `infrastructure` trees.

## Paste-Ready Invocation

Use this when starting the task:

```text
Use $infrastructure-readiness-issue.

Work the next infrastructure-readiness ready-for-agent issue. Use a compact issue packet, then Issue Planner, then one Issue Implementor High only after the planner handoff exists. Do not spawn explorer agents unless blocked. Keep Terraform, validation, issue-list, and worktree output narrow. Run focused validation before Code Reviewer, one Code Reviewer pass, one targeted rereview only if required, then the PR gate and create a PR closing only the intended issue.
```
