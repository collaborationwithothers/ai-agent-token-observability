#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW_PATH="$ROOT_DIR/.github/workflows/edge-origin-validation.yml"

if [[ ! -f "$WORKFLOW_PATH" ]]; then
  echo "Missing edge origin validation workflow: ${WORKFLOW_PATH#$ROOT_DIR/}" >&2
  exit 1
fi

"$ROOT_DIR/scripts/validate-terraform-workflow-guardrails.sh" "$WORKFLOW_PATH"

python3 - "$WORKFLOW_PATH" <<'PY'
from __future__ import annotations

import pathlib
import sys

path = pathlib.Path(sys.argv[1])
content = path.read_text(encoding="utf-8")

required_fragments = {
    "app hostname": "app.tokenobs.consultwithcloud.com",
    "api hostname": "api.tokenobs.consultwithcloud.com",
    "ingest hostname": "ingest.tokenobs.consultwithcloud.com",
    "app runtime output": "direct_origin_validation_targets",
    "edge private link output": "front_door_private_link_origin_approval_requests",
    "edge hostname output": "front_door_custom_domain_hostnames",
    "auth callback output": "public_auth_callback_base_urls",
    "sanitized summary": "GITHUB_STEP_SUMMARY",
    "public endpoint probe": "validate_public_endpoint",
    "direct origin probe": "validate_direct_origin",
    "public runner direct origin job": "public-direct-origin-probe",
    "github hosted public runner": "runs-on: ubuntu-latest",
    "public job without Azure OIDC": "permissions:\n      contents: read",
    "pp pd bypass enforcement": 'case "${ENVIRONMENT}" in pp|pd)',
}

missing = [
    name
    for name, fragment in required_fragments.items()
    if fragment not in content
]

if missing:
    print("Edge origin validation workflow is missing required coverage:")
    for name in missing:
        print(f"  - {name}")
    sys.exit(1)

print("Edge origin workflow content validation passed.")
PY
