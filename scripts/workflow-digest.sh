#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

usage() {
  cat <<'USAGE'
Usage:
  scripts/workflow-digest.sh WORKFLOW.yml [WORKFLOW.yml ...]

Prints a compact digest of GitHub workflow triggers, permissions, jobs, environments, dependencies, and risky command lines.
USAGE
}

if [[ $# -eq 0 || "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  if [[ $# -eq 0 ]]; then
    exit 2
  fi
  exit 0
fi

cd "$ROOT_DIR"

for workflow in "$@"; do
  if [[ ! -f "$workflow" ]]; then
    echo "Workflow not found: $workflow" >&2
    exit 2
  fi

  echo "# Workflow Digest: $workflow"
  echo

  awk '
    /^name:/ {
      print
      next
    }
    /^on:/ {
      section = "on"
      print
      next
    }
    /^permissions:/ {
      section = "permissions"
      print
      next
    }
    /^jobs:/ {
      section = "jobs"
      print
      next
    }
    /^[^[:space:]]/ {
      if ($0 !~ /^(on:|permissions:|jobs:|name:)/) {
        section = ""
      }
    }
    section == "on" {
      if ($0 ~ /^[[:space:]]{2}workflow_dispatch:/ ||
          $0 ~ /^[[:space:]]{6}[A-Za-z0-9_-]+:/ ||
          $0 ~ /^[[:space:]]{2}(push|pull_request|pull_request_target|workflow_run|schedule):/) {
        print
      }
      next
    }
    section == "permissions" {
      if ($0 ~ /^[[:space:]]{2}[A-Za-z0-9_-]+:/) {
        print
      }
      next
    }
    section == "jobs" {
      if ($0 ~ /^  [A-Za-z0-9_-]+:/ ||
          $0 ~ /^[[:space:]]+(needs|if|environment):/ ||
          $0 ~ /^[[:space:]]+name: terraform-apply/ ||
          $0 ~ /^[[:space:]]+- uses: (azure\/login|hashicorp\/setup-terraform)/ ||
          $0 ~ /TERRAFORM_STAGE=.*terraform-stage-deploy/) {
        print
      }
    }
  ' "$workflow"

  echo
done
