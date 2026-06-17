---
name: review-worktree-issue-pr
description: Use when working a ready-for-agent GitHub issue in this repository, especially when the user says next issue, worktree, review, Code Reviewer, create a PR, or fix reviewer feedback. Produces an issue-start packet, keeps review scope narrow, and ships only after validation.
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

## Implementation Loop

1. Confirm the actual worktree or branch before editing.
2. Read only the source-of-truth docs relevant to the changed boundary.
3. Implement narrowly against the acceptance matrix.
4. Run focused validation before first review:

```bash
scripts/validate-focused.sh PROFILE
```

Use `ingestion`, `api`, `dashboard`, `terraform`, `docs`, or `all` for `PROFILE`.

5. Request Code Reviewer against the current branch diff.
6. Keep Code Reviewer output in `Comments.md` only.
7. If review returns `CHANGES_REQUESTED`, create a findings ledger:

```text
Finding:
Decision: accepted | rejected
Reason:
Fixed files:
Tests or validation:
```

8. Fix accepted findings, rerun validation, then ask for targeted rereview. Use full-diff rereview only for architecture, security, privacy, tenant-boundary, persistence, Terraform deployment, or product authorization changes.
9. After approval, run the PR gate:

```bash
scripts/validate-pr.sh
```

10. Commit, push, create the PR, and verify closing references:

```bash
gh pr view PR_NUMBER --json closingIssuesReferences
```

## Budget Controls

- Treat full validation, Terraform plans, and subagent reviews as expensive. Use them deliberately and keep their output narrow.
- Before the first edit, confirm `git worktree list --porcelain` and `git status --short --branch` for the target worktree. If edits land in the wrong checkout, stop and fix the workspace before continuing.
- During implementation, run the narrowest useful `scripts/validate-focused.sh PROFILE`. Reserve `scripts/validate-pr.sh` for after reviewer approval and before PR creation or PR update, unless a risky fix invalidates the prior full validation.
- For Terraform changes, avoid streaming full plan output by default. Prefer `terraform show -json` with `jq` assertions, targeted plan summaries, or narrow grep checks. Use full plan text only when the full plan is the artifact being reviewed.
- Cap noisy command output. If the first output is too broad, rerun with narrower filters instead of expanding the transcript.
- Do not use Code Reviewer, broad source searches, or full file reads to discover acceptance criteria. Use the Issue Start Packet and the smallest relevant source-of-truth document set.
- If reviewer output appears stale, verify the current branch diff locally before requesting another reviewer pass.

## Subagent Rules

- Use implementation subagents only when the user explicitly asks for them or the work is truly parallel.
- Use Code Reviewer as a review gate, not as an acceptance-criteria discovery tool.
- If a subagent wait or close attempt fails once, stop waiting and continue with local evidence.

## Done Means

- Acceptance matrix satisfied.
- Local validation passed for the changed surface.
- Code Reviewer reports `APPROVE`, or remaining findings are explicitly rejected with reasons.
- `Comments.md` is not committed.
- PR body closes only the intended issue or issues.
