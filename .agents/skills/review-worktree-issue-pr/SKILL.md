---
name: review-worktree-issue-pr
description: Use when working a ready-for-agent GitHub issue in this repository, especially when the user says next issue, worktree, review, Code Reviewer, create a PR, or fix reviewer feedback.
---

# Review Worktree Issue PR

## Goal

Ship one ready-for-agent issue with less repeated prompting and less review churn.

## Start Conditions

Use this skill when the user asks to:

- work the next issue,
- implement a ready-for-agent issue,
- inspect or continue work from a worktree,
- run Code Reviewer on issue work,
- fix reviewer feedback,
- create a PR for issue work.

Do not use this skill for human-only runbooks, broad backlog surveys, or tasks where the user explicitly says not to create a PR.

## Required Issue Start Packet

Before code changes or Code Reviewer, run:

```bash
scripts/issue-start.sh ISSUE_NUMBER
```

If there is no issue number, manually collect the same facts:

- Current repository path.
- Issue number, title, labels, URL, and acceptance criteria.
- Active worktree list.
- Current branch and worktree state.
- Diff stat.
- Untracked implementation files.
- Acceptance matrix: criterion, implementation file, test evidence, docs or schema evidence.
- Focused validation commands for the changed surface.

Stop and ask only if the issue target cannot be identified from local context or GitHub.

## Planning Handoff

Before editing, produce a concise planning handoff in the main thread. Do not spawn planner or implementor subagents for this sequential work.

Include:

- Issue summary and acceptance criteria.
- Source documents read.
- Current state evidence.
- Acceptance matrix with criterion, intended implementation file, required validation, and docs or schema evidence.
- Risk classification.
- Implementation steps small enough for one main-agent implementation pass.
- Do not touch list for out-of-scope files or neighboring issues.
- Focused validation plan.

Use the high-risk path when the issue touches security, privacy, tenant-boundary, authorization, persistence, migrations, Terraform provider behavior, production architecture, token metric state semantics, or Azure infrastructure behavior.

High-risk path:

- Re-check the handoff against the relevant source-of-truth docs before editing.
- Keep architecture boundaries explicit.
- For Terraform changes, use Terraform-native validation only. Do not add .NET tests to validate Terraform file structure or Terraform behavior.
- Preserve metadata-only capture by default.
- Do not persist prompt text, code content, command output, tool results, secrets, or unrelated local data.
- Represent unavailable token metrics as null, not zero.
- Keep observed, estimated, unavailable, not_applicable, and mixed states distinct.

## Implementation Loop

1. Confirm the actual worktree or branch before editing.
2. Complete the planning handoff.
3. Read only the source-of-truth docs relevant to the changed boundary.
4. Implement narrowly against the acceptance matrix and the do not touch list.
5. If local evidence proves the handoff is wrong, stop and update the handoff before broadening scope.
6. Run focused validation before first review:

```bash
scripts/validate-focused.sh PROFILE
```

Use `ingestion`, `api`, `dashboard`, `terraform`, `docs`, or `all` for `PROFILE`.

7. Request Code Reviewer against the current branch diff.
8. Keep Code Reviewer output in `Comments.md` only.
9. If review returns `CHANGES_REQUESTED`, create a findings ledger:

```text
Finding:
Decision: accepted | rejected
Reason:
Fixed files:
Tests or validation:
```

10. Fix accepted findings, rerun validation, then ask for targeted rereview. Use full-diff rereview only for architecture, security, privacy, tenant-boundary, persistence, Terraform deployment, or product authorization changes.
11. After approval, run the PR gate. Use the full gate for product runtime, authorization, persistence, migrations, deployed resource definitions, tenant/security boundaries, or broad cross-cutting changes. Use changed-file validation for narrow docs, process, validation-script, GitHub Actions guardrail, or Terraform workflow-script fixes:

```bash
scripts/validate-pr.sh
scripts/validate-pr.sh --changed origin/main
```

12. Commit, push, create the PR, and verify closing references:

```bash
gh pr view PR_NUMBER --json closingIssuesReferences
```

## Budget Controls

- Treat full validation, Terraform plans, and subagent reviews as expensive. Use them deliberately and keep their output narrow.
- Before the first edit, confirm `git worktree list --porcelain` and `git status --short --branch` for the target worktree. If edits land in the wrong checkout, stop and fix the workspace before continuing.
- During implementation, run the narrowest useful `scripts/validate-focused.sh PROFILE`. Reserve the PR gate for after reviewer approval and before PR creation or PR update, unless a risky fix invalidates the prior full validation.
- If validation is silent for more than 90 seconds in a sandboxed environment, stop waiting and diagnose the specific command. If the likely cause is sandboxed cache, package, or network access, rerun that specific command with the required permission.
- For Terraform changes, avoid streaming full plan output by default. Prefer `terraform show -json` with `jq` assertions, targeted plan summaries, or narrow grep checks. Use full plan text only when the full plan is the artifact being reviewed.
- Cap noisy command output. If the first output is too broad, rerun with narrower filters instead of expanding the transcript.
- Do not use Code Reviewer, broad source searches, or full file reads to discover acceptance criteria. Use the Issue Start Packet and the smallest relevant source-of-truth document set.
- If reviewer output appears stale, verify the current branch diff locally before requesting another reviewer pass.

## Subagent Rules

- Use subagents only for parallel review, independent research, or Code Reviewer.
- Do not use planner or implementor subagents for the sequential issue workflow.
- Use Code Reviewer as a review gate, not as an acceptance-criteria discovery tool.
- If a subagent wait or close attempt fails once, stop waiting and continue with local evidence.

## Done Means

- Acceptance matrix satisfied.
- Local validation passed for the changed surface.
- Code Reviewer reports `APPROVE`, or remaining findings are explicitly rejected with reasons.
- `Comments.md` is not committed.
- PR body closes only the intended issue or issues.
