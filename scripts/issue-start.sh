#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

usage() {
  cat <<'USAGE'
Usage:
  scripts/issue-start.sh [--compact] ISSUE_NUMBER
  scripts/issue-start.sh [--compact] --no-issue

Prints the required ready-for-agent Issue Start Packet.
USAGE
}

compact=false

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

if [[ "${1:-}" == "--compact" ]]; then
  compact=true
  shift
fi

issue_arg="${1:-}"
if [[ -z "$issue_arg" ]]; then
  usage >&2
  exit 2
fi

cd "$ROOT_DIR"

print_worktrees() {
  if [[ "$compact" == true ]]; then
    local current_worktree
    current_worktree="$(git rev-parse --show-toplevel)"
    local issue_pattern=""
    if [[ "$issue_arg" != "--no-issue" ]]; then
      issue_pattern="issue-${issue_arg}"
    fi

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
  else
    git worktree list --porcelain
  fi
}

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
print_worktrees
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
