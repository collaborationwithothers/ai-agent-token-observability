#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

usage() {
  cat <<'USAGE'
Usage:
  scripts/worktree-current.sh [ISSUE_NUMBER]

Prints only the current worktree, current branch/status, and any matching issue worktree.
USAGE
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

issue_arg="${1:-}"

cd "$ROOT_DIR"

current_worktree="$(git rev-parse --show-toplevel)"
issue_pattern=""
if [[ -n "$issue_arg" ]]; then
  issue_pattern="issue-${issue_arg}"
fi

echo "# Worktree Context"
echo
printf -- "- Current path: \`%s\`\n" "$current_worktree"
printf -- "- Branch: \`%s\`\n" "$(git branch --show-current)"
printf -- "- Head: \`%s\`\n" "$(git rev-parse --short HEAD)"
echo
echo "## Matching Worktrees"
echo

git worktree list --porcelain | awk -v current="$current_worktree" -v issue="$issue_pattern" '
  BEGIN { block = ""; include = 0 }
  /^worktree / {
    if (block != "" && include) {
      printf "%s\n", block
    }
    block = $0 "\n"
    include = (index($0, current) > 0 || (issue != "" && index($0, issue) > 0))
    next
  }
  {
    block = block $0 "\n"
  }
  END {
    if (block != "" && include) {
      printf "%s", block
    }
  }
'

echo
echo "## Status"
echo
git status --short --branch
