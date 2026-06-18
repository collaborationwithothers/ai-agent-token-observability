---
description: Work one infrastructure-readiness issue with compact context and bounded validation.
---

Use $infrastructure-readiness-issue.

Work the next infrastructure-readiness ready-for-agent issue.

Constraints:

- Use a narrow issue list query with `--limit 10` or less.
- Use a compact issue packet.
- Run Issue Planner first and wait for `IMPLEMENTOR HANDOFF`.
- Run exactly one Issue Implementor High after the planner handoff.
- Do not spawn explorer agents unless blocked by a specific independent unknown.
- Keep Terraform, validation, issue-list, and worktree output narrow.
- Use `terraform show -json` with `jq` assertions or narrow text filters instead of full plan text.
- Run focused validation before Code Reviewer.
- Run one Code Reviewer pass and one targeted rereview only if required.
- Run the PR gate and create a PR that closes only the intended issue.
