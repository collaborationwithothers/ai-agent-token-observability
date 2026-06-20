#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

usage() {
  cat <<'USAGE'
Usage:
  scripts/validate-pr.sh
  scripts/validate-pr.sh --full
  scripts/validate-pr.sh --focused PROFILE
  scripts/validate-pr.sh --changed [BASE_REF]

Modes:
  --full              Run the full local PR gate. This is the default.
  --focused PROFILE   Run one validate-focused profile with timed output.
  --changed BASE_REF  Infer focused profiles from changed files. BASE_REF defaults to origin/main.

Profiles are delegated to scripts/validate-focused.sh.
USAGE
}

mode="full"
profile=""
base_ref="origin/main"

case "${1:-}" in
  "")
    mode="full"
    ;;
  --full)
    mode="full"
    shift
    ;;
  --focused)
    mode="focused"
    profile="${2:-}"
    if [[ -z "$profile" ]]; then
      echo "Missing focused validation profile." >&2
      usage >&2
      exit 2
    fi
    shift 2
    ;;
  --changed)
    mode="changed"
    base_ref="${2:-origin/main}"
    if [[ "$#" -gt 0 ]]; then
      shift
    fi
    if [[ "$#" -gt 0 ]]; then
      shift
    fi
    ;;
  -h|--help)
    usage
    exit 0
    ;;
  *)
    echo "Unknown validate-pr option: $1" >&2
    usage >&2
    exit 2
    ;;
esac

if [[ "$#" -ne 0 ]]; then
  echo "Unexpected validate-pr argument: $1" >&2
  usage >&2
  exit 2
fi

check_comments_not_staged() {
  if git diff --cached --name-only | grep -qx 'Comments.md'; then
    echo "Comments.md is staged; unstage it before proceeding." >&2
    return 1
  fi
}

run_step() {
  local name="$1"
  shift
  local started_at
  local finished_at
  local duration
  local status

  started_at="$(date +%s)"
  printf '==> %s\n' "$name"
  set +e
  "$@"
  status="$?"
  set -e
  finished_at="$(date +%s)"
  duration="$((finished_at - started_at))"

  if [[ "$status" -ne 0 ]]; then
    printf '<== %s failed after %ss with exit code %s\n' "$name" "$duration" "$status" >&2
    return "$status"
  fi

  printf '<== %s passed in %ss\n' "$name" "$duration"
}

run_focused_profile() {
  local selected_profile="$1"
  run_step "focused validation: ${selected_profile}" scripts/validate-focused.sh "$selected_profile"
}

changed_files() {
  local diff_base="$1"

  if git rev-parse --verify --quiet "$diff_base" >/dev/null; then
    git diff --name-only "${diff_base}...HEAD"
  else
    echo "Changed-file base ref not found: ${diff_base}" >&2
    echo "Falling back to unstaged and staged local changes." >&2
  fi

  git diff --name-only
  git diff --cached --name-only
  git ls-files --others --exclude-standard
}

infer_changed_profiles() {
  local diff_base="$1"
  local docs=false
  local terraform=false
  local dashboard=false
  local api=false
  local all=false
  local file
  local files

  files="$(changed_files "$diff_base" | sed '/^$/d' | sort -u)"
  if [[ -z "$files" ]]; then
    echo "No changed files detected against ${diff_base}." >&2
    return 1
  fi

  while IFS= read -r file; do
    case "$file" in
      AGENTS.md|README.md|docs/*|.agents/skills/*|*.md)
        docs=true
        ;;
      .github/workflows/terraform-*.yml|.github/workflows/terraform-*.yaml|.github/workflows/edge-origin-validation.yml|infrastructure/azure/*|scripts/terraform-*|scripts/validate-terraform-*|scripts/validate-edge-origin-workflow.sh|tests/workflow-guardrails/*)
        terraform=true
        ;;
      web/token-observability-dashboard/*)
        dashboard=true
        ;;
      src/TokenObservability.Api/*)
        api=true
        ;;
      scripts/issue-start.sh|scripts/session-digest.sh|scripts/worktree-current.sh|scripts/codex-infra-issue.sh|scripts/validate-focused.sh|scripts/validate-pr.sh|scripts/validate-markdown-links.sh|scripts/workflow-digest.sh)
        docs=true
        ;;
      src/*|tests/*|AiAgentTokenObservability.slnx|Directory.*|global.json|NuGet.config)
        all=true
        ;;
      package.json|package-lock.json|web/*)
        all=true
        ;;
      *)
        all=true
        ;;
    esac
  done <<< "$files"

  if [[ "$all" == "true" ]]; then
    printf '%s\n' all
    return 0
  fi
  if [[ "$docs" == "true" ]]; then
    printf '%s\n' docs
  fi
  if [[ "$terraform" == "true" ]]; then
    printf '%s\n' terraform
  fi
  if [[ "$dashboard" == "true" ]]; then
    printf '%s\n' dashboard
  fi
  if [[ "$api" == "true" ]]; then
    printf '%s\n' api
  fi
}

run_changed_profiles() {
  local diff_base="$1"
  local profiles
  local selected_profile

  profiles="$(infer_changed_profiles "$diff_base")"
  printf 'Changed-file validation profiles against %s:\n%s\n' "$diff_base" "$profiles"
  while IFS= read -r selected_profile; do
    [[ -z "$selected_profile" ]] && continue
    run_focused_profile "$selected_profile"
  done <<< "$profiles"
}

run_full_gate() {
  run_step "dotnet restore" dotnet restore AiAgentTokenObservability.slnx
  run_step "dotnet build" dotnet build AiAgentTokenObservability.slnx --no-restore
  run_step "dotnet test" dotnet test AiAgentTokenObservability.slnx --no-restore
  run_step "dotnet format verify" dotnet format AiAgentTokenObservability.slnx --verify-no-changes --no-restore
  run_step "npm ci dashboard" npm --prefix web/token-observability-dashboard ci
  run_step "npm build dashboard" npm --prefix web/token-observability-dashboard run build
  run_step "terraform foundation ACR validation" scripts/validate-terraform-foundation-acr.sh
  run_step "terraform network private data plane validation" scripts/validate-terraform-network-private-data-plane.sh
  run_step "terraform observability foundation validation" scripts/validate-terraform-observability-foundation.sh
  run_step "terraform data platform validation" scripts/validate-terraform-data-platform.sh
  run_step "terraform AI services validation" scripts/validate-terraform-ai-services.sh
  run_step "terraform app runtime validation" scripts/validate-terraform-app-runtime.sh
  run_step "terraform managed Grafana validation" scripts/validate-terraform-managed-grafana.sh
  run_step "terraform app runtime stage check" scripts/terraform-stage-check.sh app_runtime
  run_step "terraform AI services stage check" scripts/terraform-stage-check.sh ai_services
  run_step "terraform edge stage check" scripts/terraform-stage-check.sh edge
  run_step "terraform managed Grafana stage check" scripts/terraform-stage-check.sh managed_grafana
  run_step "terraform workflow guardrail self-test" scripts/validate-terraform-workflow-guardrails.sh --self-test
  run_step "terraform workflow guardrail validation" scripts/validate-terraform-workflow-guardrails.sh
  run_step "edge origin workflow validation" scripts/validate-edge-origin-workflow.sh
  run_step "git diff whitespace check" git diff --check
}

check_comments_not_staged

case "$mode" in
  full)
    run_full_gate
    ;;
  focused)
    run_focused_profile "$profile"
    ;;
  changed)
    run_changed_profiles "$base_ref"
    ;;
esac

echo "PR validation passed."
