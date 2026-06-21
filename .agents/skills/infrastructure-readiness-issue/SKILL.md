---
name: infrastructure-readiness-issue
description: Use when working the next infrastructure-readiness ready-for-agent GitHub issue in this repo, especially Terraform, deployment, networking, Azure resource, validation, or PR-ready infrastructure work where token budget matters.
---

# Infrastructure Readiness Issue

## Goal

Ship one infrastructure-readiness issue with minimal repeated context loading, bounded tool output, and a real PR.

This skill refines the repo ready-for-agent issue workflow for infrastructure work. Prefer the workflow below over broad backlog scans, full source-doc reads, full Terraform plan output, or exploratory subagent fanout. Use `.codex/agents/issue-executor.toml` only for coordinator-managed parallel batches with two or more independent ready-for-agent issues, never for a single issue.

## Workflow

1. Select one issue.
   - Use a narrow `gh issue list` query with `--limit 10` or less.
   - Prefer issues labelled `ready-for-agent` and obviously blocking infrastructure readiness.
   - After selecting an issue, view only that issue unless dependencies are unclear.

2. Create a compact issue-start packet.
   - Run `scripts/session-digest.sh ISSUE_NUMBER` or `scripts/issue-start.sh --compact ISSUE_NUMBER`.
   - Use `scripts/issue-start.sh --minimal ISSUE_NUMBER` when selecting or resuming work and the full issue body is not needed yet.
   - If output is noisy, summarize only: issue metadata, acceptance criteria, current worktree, branch/status, diff stat, untracked files, acceptance matrix, and focused validation commands.
   - Do not paste full worktree lists when many stale worktrees exist. Report current worktree plus matching issue worktree only.

3. Produce the planning handoff in the main thread.
   - Do not spawn planner or implementor subagents for the sequential infrastructure workflow.
   - For parallel batches, the coordinator may dispatch one Issue Executor per issue only after each issue has an isolated worktree and branch, compact issue packet, acceptance matrix, do-not-touch list, focused validation command, and risk classification.
   - Map each acceptance criterion to intended Terraform, workflow, docs, or script files.
   - List source documents read and source documents intentionally not loaded.
   - Identify risks for Terraform provider behavior, production architecture, networking, security, privacy, tenant-boundary, authorization, persistence, migration, token metric state changes, and Azure infrastructure behavior.
   - Include a do not touch list for neighboring stages, modules, or workflows.
   - Include focused Terraform validation commands and any required plan evidence.

4. Keep main-thread context small while implementing.
   - Do not reread every source-of-truth document.
   - Prepare integration checks, validation commands, and PR gate steps from the handoff.
   - Inspect diff stat, changed files, and targeted snippets before deciding whether more reading is needed.
   - For a single issue, keep implementation in the main thread.

5. Implement or integrate narrowly.
   - Read only source-of-truth docs for the touched infrastructure boundary.
   - Prefer existing Terraform stage and module patterns over new structure.
   - Preserve state-sensitive Terraform addresses unless the issue explicitly requires replacement.
   - For Azure resource additions, prefer AVM through local wrapper modules when an appropriate AVM exists.
   - If local evidence proves the handoff is wrong, stop and update the handoff before broadening scope.

6. Validate with bounded output.
   - Run the narrowest relevant focused validation first, usually `scripts/validate-focused.sh terraform`.
   - For Terraform plans, prefer `plan -out`, then `terraform show -json` with `jq` assertions.
   - Use `scripts/terraform-stage-check.sh STAGE` for bounded stage init/validate, and `--plan` only when the required variables are known.
   - If text output is needed, pipe to a narrow filter for resource names, `Plan:`, diagnostics, or required assertions.
   - Do not stream full Terraform plans unless the full plan text is the artifact under review.

7. Review once.
   - Confirm target worktree and branch before review.
   - Confirm `Comments.md` is untracked or ignored.
   - If Issue Executor was used, inspect its handoff, branch/status, diff stat, untracked files, acceptance matrix, and focused validation result before review.
   - Run Code Reviewer once after focused validation.
   - If review returns `CHANGES_REQUESTED`, create a concise findings ledger, fix accepted findings in the coordinator or redispatch the same Issue Executor with only the accepted findings and targeted validation command, rerun focused validation, then request one targeted rereview.

8. Finish with the PR gate.
   - Run `scripts/validate-pr.sh` once after reviewer approval for product runtime, authorization, persistence, migrations, deployed resource definitions, tenant/security boundaries, or broad cross-cutting changes.
   - Use `scripts/validate-pr.sh --changed origin/main` for narrow docs, process, validation-script, GitHub Actions guardrail, or Terraform workflow-script fixes where changed-file validation is sufficient.
   - If validation is silent for more than 90 seconds in a sandboxed environment, stop waiting and diagnose the specific command. If the likely cause is sandboxed cache, package, or network access, rerun that specific command with the required permission.
   - The coordinator commits only relevant files.
   - The coordinator creates the PR.
   - The coordinator verifies `closingIssuesReferences` closes only the intended issue.

## Output Rules

- Report command results as targeted evidence, not raw logs.
- Cap noisy command output and rerun with narrower filters instead of increasing output.
- Avoid broad `rg docs infrastructure` searches. Search specific docs or paths.
- Use `scripts/workflow-digest.sh WORKFLOW.yml` before reading large workflow YAML files end to end.
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
- Issue Executor for a single issue.
- Broad search output from the entire `docs` and `infrastructure` trees.

## Paste-Ready Invocation

Use this when starting the task:

```text
Use $infrastructure-readiness-issue.

Work the next infrastructure-readiness ready-for-agent issue. Use a compact issue packet, produce the planning handoff in the main thread, implement narrowly from that handoff, and do not spawn planner or implementor subagents. Do not spawn explorer agents unless blocked by a specific independent unknown. Keep Terraform, validation, issue-list, and worktree output narrow. Run focused validation before Code Reviewer, one Code Reviewer pass, one targeted rereview only if required, then the PR gate and create a PR closing only the intended issue.
```

For a parallel infrastructure batch, dispatch one Issue Executor per independent issue only after the coordinator has prepared a separate worktree, branch, issue packet, planning handoff, acceptance matrix, do-not-touch list, focused validation command, and risk classification. The coordinator still owns Code Reviewer, PR gate, PR creation, and closing-reference verification.
