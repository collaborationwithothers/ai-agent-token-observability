#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

usage() {
  cat <<'USAGE'
Usage:
  scripts/issue-start.sh ISSUE_NUMBER
  scripts/issue-start.sh --no-issue

Prints the required ready-for-agent Issue Start Packet.
USAGE
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

issue_arg="${1:-}"
if [[ -z "$issue_arg" ]]; then
  usage >&2
  exit 2
fi

cd "$ROOT_DIR"

echo "# Issue Start Packet"
echo
echo "## Repository"
echo
printf -- "- Path: \`%s\`\n" "$ROOT_DIR"
printf -- "- Branch: \`%s\`\n" "$(git branch --show-current)"
printf -- "- Head: \`%s\`\n" "$(git rev-parse --short HEAD)"
echo

if [[ "$issue_arg" != "--no-issue" ]]; then
  if ! command -v gh >/dev/null 2>&1; then
    echo "## GitHub Issue"
    echo
    echo "- `gh` is not available; issue details are unverified."
    echo
  else
    echo "## GitHub Issue"
    echo
    gh issue view "$issue_arg" \
      --json number,title,state,labels,url,body \
      --template '{{printf "- Number: #%v\n" .number}}{{printf "- Title: %s\n" .title}}{{printf "- State: %s\n" .state}}{{printf "- URL: %s\n" .url}}- Labels:{{range .labels}} {{.name}}{{end}}{{printf "\n\n### Body\n\n%s\n" .body}}'
    echo
  fi
else
  echo "## GitHub Issue"
  echo
  echo "- No issue number supplied. Fill issue details manually before coding."
  echo
fi

echo "## Worktrees"
echo
git worktree list --porcelain
echo

echo "## Status"
echo
git status --short --branch
echo

echo "## Diff Stat"
echo
if ! git diff --stat HEAD; then
  echo "- Unable to compute diff stat."
fi
echo

echo "## Untracked Files"
echo
untracked="$(git ls-files --others --exclude-standard)"
if [[ -z "$untracked" ]]; then
  echo "- None"
else
  printf '%s\n' "$untracked" | sed 's/^/- /'
fi
echo

cat <<'MATRIX'
## Acceptance Matrix

Fill this before implementation or review:

| Criterion | Implementation Files | Test Evidence | Docs or Schema Evidence |
| --- | --- | --- | --- |
| TODO | TODO | TODO | TODO |

## Focused Validation Plan

Choose the smallest profile that covers the changed surface:

- `scripts/validate-focused.sh ingestion`
- `scripts/validate-focused.sh api`
- `scripts/validate-focused.sh dashboard`
- `scripts/validate-focused.sh terraform`
- `scripts/validate-focused.sh docs`
- `scripts/validate-focused.sh all`
MATRIX
