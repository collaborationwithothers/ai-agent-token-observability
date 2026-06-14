#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

usage() {
  cat <<'USAGE'
Usage:
  scripts/validate-terraform-workflow-guardrails.sh [PATH ...]
  scripts/validate-terraform-workflow-guardrails.sh --self-test

When PATH is omitted, the validator scans .github/workflows.
USAGE
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

if [[ "${1:-}" == "--self-test" ]]; then
  "$0" "$ROOT_DIR/tests/workflow-guardrails/safe"

  failed=0
  while IFS= read -r -d '' fixture; do
    if "$0" "$fixture" >/tmp/workflow-guardrail-fixture.log 2>&1; then
      echo "unsafe fixture unexpectedly passed: $fixture" >&2
      failed=1
    else
      echo "unsafe fixture failed as expected: ${fixture#$ROOT_DIR/}"
    fi
  done < <(find "$ROOT_DIR/tests/workflow-guardrails/unsafe" -type f \( -name '*.yml' -o -name '*.yaml' \) -print0 | sort -z)

  exit "$failed"
fi

if [[ "$#" -eq 0 ]]; then
  set -- "$ROOT_DIR/.github/workflows"
fi

python3 - "$@" <<'PY'
from __future__ import annotations

import pathlib
import re
import sys

FORBIDDEN_TRIGGERS = (
    "push",
    "pull_request",
    "pull_request_target",
    "workflow_run",
    "schedule",
)

FORBIDDEN_COMMAND_PATTERNS = (
    r"terraform\s+apply\s+-auto-approve",
    r"terraform\s+destroy\s+-auto-approve",
    r"terraform\s+destroy(?:\s|$)",
    r"az\s+group\s+delete(?:\s|$)",
    r"az\s+resource\s+delete(?:\s|$)",
    r"az\s+deployment\s+sub\s+delete(?:\s|$)",
    r"resource_group_by_tag",
    r"all_tagged_resources",
)

DEPLOYMENT_CAPABLE_PATTERNS = (
    r"azure/login",
    r"\bterraform\s+(?:init|validate|plan|apply|destroy|workspace)\b",
    r"\baz\s+(?:login|deployment|group|resource)\b",
)

REQUIRED_PATTERNS = {
    "workflow_dispatch trigger": r"(?m)^\s*workflow_dispatch\s*:",
    "least privilege contents permission": r"(?m)^\s*contents\s*:\s*read\s*$",
    "OIDC id-token permission": r"(?m)^\s*id-token\s*:\s*write\s*$",
    "expected repository gate": r"GITHUB_REPOSITORY.*EXPECTED_REPOSITORY|EXPECTED_REPOSITORY.*GITHUB_REPOSITORY",
    "workflow_dispatch event gate": r"GITHUB_EVENT_NAME.*workflow_dispatch|workflow_dispatch.*GITHUB_EVENT_NAME",
    "expected actor gate": r"GITHUB_ACTOR|AUTHORIZED_ACTORS|TOKENOBS_ALLOWED_DEPLOYMENT_ACTORS",
    "branch gate": r"GITHUB_REF_NAME",
    "environment gate": r"dv\|qa\|pp\|pd|dv.*qa.*pp.*pd",
    "region gate": r"eastus\|eastus2\|westeurope|eastus.*eastus2.*westeurope",
    "customer organization slug gate": r"CUSTOMER_ORGANIZATION_SLUG|customer_organization_slug",
    "workspace derivation": r"TF_WORKSPACE=.*ENVIRONMENT.*AZURE_REGION.*CUSTOMER_ORGANIZATION_SLUG|terraform_workspace",
    "default workspace denial": r"TF_WORKSPACE.*default|default.*TF_WORKSPACE",
    "confirmation gate": r"CONFIRMATION|confirmation",
    "environment protection": r"(?m)^\s*environment\s*:",
}


def workflow_files(paths: list[str]) -> list[pathlib.Path]:
    files: list[pathlib.Path] = []
    for raw in paths:
        path = pathlib.Path(raw)
        if not path.exists():
            continue
        if path.is_dir():
            files.extend(sorted(path.rglob("*.yml")))
            files.extend(sorted(path.rglob("*.yaml")))
        elif path.suffix in {".yml", ".yaml"}:
            files.append(path)
    return sorted(dict.fromkeys(files))


def is_deployment_capable(content: str) -> bool:
    return any(re.search(pattern, content, re.IGNORECASE) for pattern in DEPLOYMENT_CAPABLE_PATTERNS)


def forbidden_trigger_errors(content: str) -> list[str]:
    errors: list[str] = []
    for trigger in FORBIDDEN_TRIGGERS:
        block_pattern = rf"(?m)^\s*{re.escape(trigger)}\s*:"
        list_pattern = rf"(?m)^\s*on\s*:\s*\[[^\]]*\b{re.escape(trigger)}\b"
        scalar_pattern = rf"(?m)^\s*on\s*:\s*{re.escape(trigger)}\s*$"
        if re.search(block_pattern, content) or re.search(list_pattern, content) or re.search(scalar_pattern, content):
            errors.append(f"forbidden trigger: {trigger}")
    return errors


def forbidden_command_errors(content: str) -> list[str]:
    errors: list[str] = []
    for pattern in FORBIDDEN_COMMAND_PATTERNS:
        if re.search(pattern, content, re.IGNORECASE):
            errors.append(f"forbidden command pattern: {pattern}")
    return errors


def first_line(content: str, needle: str) -> int | None:
    for index, line in enumerate(content.splitlines(), start=1):
        if needle in line:
            return index
    return None


def required_gate_errors(content: str) -> list[str]:
    errors = [
        f"missing {name}"
        for name, pattern in REQUIRED_PATTERNS.items()
        if re.search(pattern, content, re.IGNORECASE | re.MULTILINE | re.DOTALL) is None
    ]

    guard_line = first_line(content, "Validate guardrails before privileged steps")
    azure_login_line = first_line(content, "azure/login")
    terraform_line = first_line(content, "terraform ")

    if guard_line is None:
        errors.append("missing guardrail validation step")
    if azure_login_line is not None and guard_line is not None and guard_line > azure_login_line:
        errors.append("guardrail validation step must run before Azure login")
    if terraform_line is not None and guard_line is not None and guard_line > terraform_line:
        errors.append("guardrail validation step must run before Terraform execution")

    return errors


def validate_file(path: pathlib.Path) -> list[str]:
    content = path.read_text(encoding="utf-8")
    if not is_deployment_capable(content):
        return []

    errors: list[str] = []
    errors.extend(forbidden_trigger_errors(content))
    errors.extend(forbidden_command_errors(content))
    errors.extend(required_gate_errors(content))
    return errors


files = workflow_files(sys.argv[1:])
if not files:
    print("No workflow files found.")
    sys.exit(0)

all_errors: list[str] = []
for path in files:
    errors = validate_file(path)
    if errors:
        all_errors.append(str(path))
        all_errors.extend(f"  - {error}" for error in errors)

if all_errors:
    print("Workflow guardrail validation failed:")
    print("\n".join(all_errors))
    sys.exit(1)

print(f"Workflow guardrail validation passed for {len(files)} file(s).")
PY
