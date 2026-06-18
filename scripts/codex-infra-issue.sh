#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

usage() {
  cat <<'USAGE'
Usage:
  scripts/codex-infra-issue.sh [ISSUE_NUMBER]

Starts a non-interactive Codex run using the infrastructure-readiness issue prompt.
If ISSUE_NUMBER is omitted, Codex is asked to choose the next infrastructure-readiness issue.
USAGE
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

if ! command -v codex >/dev/null 2>&1; then
  echo "codex command not found" >&2
  exit 127
fi

issue_arg="${1:-the next infrastructure-readiness ready-for-agent issue}"

cd "$ROOT_DIR"

printf 'Use $infrastructure-readiness-issue.\n\nWork %s. Use a compact issue packet, then Issue Planner, then one Issue Implementor High only after the planner handoff exists. Do not spawn explorer agents unless blocked. Keep Terraform, validation, issue-list, and worktree output narrow. Run focused validation before Code Reviewer, one Code Reviewer pass, one targeted rereview only if required, then the PR gate and create a PR closing only the intended issue.\n' "$issue_arg" | codex exec -
