#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

if git diff --cached --name-only | grep -qx 'Comments.md'; then
  echo "Comments.md is staged; unstage it before proceeding." >&2
  exit 1
fi

dotnet restore AiAgentTokenObservability.slnx
dotnet build AiAgentTokenObservability.slnx --no-restore
dotnet test AiAgentTokenObservability.slnx --no-restore
dotnet format AiAgentTokenObservability.slnx --verify-no-changes --no-restore
npm --prefix web/token-observability-dashboard ci
npm --prefix web/token-observability-dashboard run build
scripts/validate-terraform-foundation-acr.sh
scripts/validate-terraform-network-private-data-plane.sh
scripts/validate-terraform-observability-foundation.sh
scripts/validate-terraform-data-platform.sh
scripts/validate-terraform-workflow-guardrails.sh
scripts/validate-edge-origin-workflow.sh
git diff --check

echo "PR validation passed."
