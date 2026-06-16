#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

usage() {
  cat <<'USAGE'
Usage:
  scripts/validate-focused.sh PROFILE

Profiles:
  ingestion   Build solution and run ingestion/runtime tests.
  api         Build solution and run runtime API authorization tests.
  dashboard   Build the React dashboard.
  terraform   Run Terraform workflow guardrail validation.
  docs        Run lightweight repository checks for docs/process changes.
  all         Run build, test, dashboard build, format, and diff checks.
USAGE
}

profile="${1:-}"
if [[ -z "$profile" || "$profile" == "-h" || "$profile" == "--help" ]]; then
  usage
  if [[ -z "$profile" ]]; then
    exit 2
  fi
  exit 0
fi

cd "$ROOT_DIR"

check_comments_not_staged() {
  if git diff --cached --name-only | grep -qx 'Comments.md'; then
    echo "Comments.md is staged; unstage it before proceeding." >&2
    return 1
  fi
}

run_docs_checks() {
  check_comments_not_staged
  git diff --check
  bash -n scripts/issue-start.sh
  bash -n scripts/validate-focused.sh
  bash -n scripts/validate-pr.sh
  if [[ -f .agents/skills/review-worktree-issue-pr/SKILL.md ]]; then
    grep -q '^name: review-worktree-issue-pr$' .agents/skills/review-worktree-issue-pr/SKILL.md
    grep -q '^description: ' .agents/skills/review-worktree-issue-pr/SKILL.md
  fi
}

run_dotnet_build() {
  dotnet build AiAgentTokenObservability.slnx --no-restore
}

run_runtime_tests() {
  dotnet test tests/TokenObservability.Runtime.Tests/TokenObservability.Runtime.Tests.csproj --no-restore
}

case "$profile" in
  ingestion)
    check_comments_not_staged
    run_dotnet_build
    run_runtime_tests
    git diff --check
    ;;
  api)
    check_comments_not_staged
    run_dotnet_build
    dotnet test tests/TokenObservability.Runtime.Tests/TokenObservability.Runtime.Tests.csproj --no-restore --filter "FullyQualifiedName~ProductApiAuthorizationContextTests"
    git diff --check
    ;;
  dashboard)
    check_comments_not_staged
    npm --prefix web/token-observability-dashboard run build
    git diff --check
    ;;
  terraform)
    check_comments_not_staged
    scripts/validate-terraform-workflow-guardrails.sh
    git diff --check
    ;;
  docs)
    run_docs_checks
    ;;
  all)
    check_comments_not_staged
    dotnet build AiAgentTokenObservability.slnx --no-restore
    dotnet test AiAgentTokenObservability.slnx --no-restore
    dotnet format AiAgentTokenObservability.slnx --verify-no-changes --no-restore
    npm --prefix web/token-observability-dashboard run build
    git diff --check
    ;;
  *)
    echo "Unknown validation profile: $profile" >&2
    usage >&2
    exit 2
    ;;
esac
