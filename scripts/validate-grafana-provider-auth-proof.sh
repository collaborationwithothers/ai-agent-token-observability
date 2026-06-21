#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROOF_DIR="$ROOT_DIR/infrastructure/azure/proofs/grafana_provider_auth"
STAGE_DIR="$ROOT_DIR/infrastructure/azure/stages/managed_grafana"
MODULE_DIR="$ROOT_DIR/infrastructure/azure/modules/managed_grafana"
PROVE_SCRIPT="$ROOT_DIR/scripts/prove-grafana-provider-auth.sh"
FOCUSED_VALIDATOR="$ROOT_DIR/scripts/validate-focused.sh"
PR_VALIDATOR="$ROOT_DIR/scripts/validate-pr.sh"
DOC="$ROOT_DIR/docs/operations/grafana-provider-auth-proof.md"
MANAGED_GRAFANA_DOC="$ROOT_DIR/docs/architecture/managed-grafana-dashboards.md"
ROADMAP="$ROOT_DIR/docs/planning/production-implementation-roadmap/milestones-3-7.md"

python3 - "$ROOT_DIR" "$PROOF_DIR" "$STAGE_DIR" "$MODULE_DIR" "$PROVE_SCRIPT" "$FOCUSED_VALIDATOR" "$PR_VALIDATOR" "$DOC" "$MANAGED_GRAFANA_DOC" "$ROADMAP" <<'PY'
from __future__ import annotations

import pathlib
import re
import sys

(
    root_dir,
    proof_dir,
    stage_dir,
    module_dir,
    prove_script,
    focused_validator,
    pr_validator,
    doc_path,
    managed_grafana_doc,
    roadmap,
) = [pathlib.Path(arg) for arg in sys.argv[1:]]

errors: list[str] = []

def read(path: pathlib.Path) -> str:
    if not path.exists():
        errors.append(f"missing required file: {path.relative_to(root_dir)}")
        return ""
    return path.read_text(encoding="utf-8")

proof_files = sorted(proof_dir.glob("*.tf"))
if not proof_files:
    errors.append("missing Terraform proof fixture files")
proof_content = "\n".join(path.read_text(encoding="utf-8") for path in proof_files)
stage_content = "\n".join(path.read_text(encoding="utf-8") for path in sorted(stage_dir.glob("*.tf")))
module_content = "\n".join(path.read_text(encoding="utf-8") for path in sorted(module_dir.glob("*.tf")))
prove_content = read(prove_script)
focused_content = read(focused_validator)
pr_content = read(pr_validator)
doc_content = read(doc_path)
managed_grafana_doc_content = read(managed_grafana_doc)
roadmap_content = read(roadmap)

required_proof_patterns = {
    "Terraform 1.14 requirement": r'required_version\s*=\s*"~>\s*1\.14\.0"',
    "Grafana provider source": r'source\s*=\s*"grafana/grafana"',
    "Grafana provider exact pin": r'version\s*=\s*"4\.39\.0"',
    "empty Grafana provider block": r'provider\s+"grafana"\s*\{\s*\}',
    "read-only folders auth probe": r'data\s+"grafana_folders"\s+"auth_probe"\s*\{\s*\}',
}

for name, pattern in required_proof_patterns.items():
    if re.search(pattern, proof_content, re.IGNORECASE | re.MULTILINE | re.DOTALL) is None:
        errors.append(f"proof fixture missing {name}")

forbidden_proof_patterns = {
    "Grafana provider resources": r'resource\s+"grafana_',
    "Grafana service account token resource": r'grafana_service_account(_token|_rotating_token)?',
    "Grafana API key": r'api_key',
    "dashboard deployment": r'(grafana_dashboard|dashboard_json|dashboard_uid)',
    "folder resource": r'resource\s+"grafana_folder"',
    "Terraform outputs": r'(?m)^\s*output\s+"',
    "provider auth arguments": r'(?m)^\s*(auth|http_headers|url)\s*=',
}

for name, pattern in forbidden_proof_patterns.items():
    if re.search(pattern, proof_content, re.IGNORECASE | re.MULTILINE | re.DOTALL):
        errors.append(f"proof fixture contains forbidden {name}")

managed_grafana_combined = f"{stage_content}\n{module_content}"
for name, pattern in {
    "provider auth arguments": r'(?m)^\s*(auth|http_headers|url)\s*=',
    "unsupported Grafana provider resources": r'resource\s+"grafana_(?!folder|dashboard")',
    "service account tokens": r'(service_account_token|grafana_service_account|api_key\s*=|api_key_enabled\s*=\s*true)',
}.items():
    if re.search(pattern, managed_grafana_combined, re.IGNORECASE | re.MULTILINE | re.DOTALL):
        errors.append(f"managed_grafana stage or module contains forbidden {name}")

required_script_terms = [
    "GRAFANA_ENTRA_AUDIENCE=\"6f2d169c-08f3-4a4c-a982-bcaf2d038c45\"",
    "az account get-access-token",
    "GRAFANA_AUTH",
    "unset GRAFANA_HTTP_HEADERS",
    "GRAFANA_SERVICE_ACCOUNT_TOKEN",
    "-chdir=\"$PROOF_DIR\" init -backend=false",
    "-chdir=\"$PROOF_DIR\" plan",
    "Terraform output was suppressed",
    "clear_terraform_logging_env",
    "TF_LOG|TF_LOG_*|TF_ACC_LOG_PATH",
]

for term in required_script_terms:
    if term not in prove_content:
        errors.append(f"prove script missing required term: {term}")

for name, pattern in {
    "shell xtrace": r'(?m)^\s*set\s+-x\b',
    "token value echo": r'(?i)(echo|printf).*\$[{]?(GRAFANA_|TOKEN|AUTH|HTTP_HEADERS)',
}.items():
    if re.search(pattern, prove_content):
        errors.append(f"prove script contains forbidden {name}")

if prove_content.count("clear_terraform_logging_env") < 3:
    errors.append("prove script must define and call clear_terraform_logging_env in both auth modes")

for path_name, content in [
    ("validate-focused.sh", focused_content),
    ("validate-pr.sh", pr_content),
]:
    if "scripts/validate-grafana-provider-auth-proof.sh" not in content:
        errors.append(f"{path_name} must run validate-grafana-provider-auth-proof.sh")

required_doc_terms = [
    "Microsoft Entra bearer token",
    "6f2d169c-08f3-4a4c-a982-bcaf2d038c45",
    "scripts/prove-grafana-provider-auth.sh --mode entra",
    "scripts/prove-grafana-provider-auth.sh --mode service-account",
    "Key Vault",
    "service account token fallback",
    "Sanitized Evidence Template",
    "Do not paste bearer tokens",
]

for term in required_doc_terms:
    if term not in doc_content:
        errors.append(f"operation doc missing required term: {term}")

if "Provider Authentication Contract" not in managed_grafana_doc_content:
    errors.append("Managed Grafana architecture doc missing Provider Authentication Contract section")
if "grafana-provider-auth-proof.md" not in managed_grafana_doc_content:
    errors.append("Managed Grafana architecture doc must link the auth proof runbook")
if "#64 consumes the #63 authentication contract" not in roadmap_content:
    errors.append("roadmap must state that #64 consumes the #63 authentication contract")

if errors:
    print("Grafana provider authentication proof validation failed:")
    for error in errors:
        print(f"  - {error}")
    sys.exit(1)

print("Grafana provider authentication proof validation passed.")
PY
