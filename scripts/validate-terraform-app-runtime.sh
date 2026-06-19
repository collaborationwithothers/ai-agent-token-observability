#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STAGE_DIR="$ROOT_DIR/infrastructure/azure/stages/app_runtime"
HELPER="$ROOT_DIR/scripts/terraform-stage-deploy.sh"
README="$STAGE_DIR/README.md"

python3 - "$STAGE_DIR" "$HELPER" "$README" <<'PY'
from __future__ import annotations

import pathlib
import re
import sys

stage_dir = pathlib.Path(sys.argv[1])
helper_path = pathlib.Path(sys.argv[2])
readme_path = pathlib.Path(sys.argv[3])

stage_content = "\n".join(
    path.read_text(encoding="utf-8")
    for path in sorted(stage_dir.glob("*.tf"))
)
helper_content = helper_path.read_text(encoding="utf-8")
readme_content = readme_path.read_text(encoding="utf-8")

errors: list[str] = []

required_stage_patterns = {
    "container registry ID variable": r'variable\s+"container_registry_id"',
    "container registry ID validation": r"Microsoft\[.\]ContainerRegistry/registries",
    "container registry ID precondition": r"container_registry_server\s*==\s*null\s*\|\|\s*var\.container_registry_id\s*!=\s*null",
    "service user-assigned identities": r'resource\s+"azurerm_user_assigned_identity"\s+"services"',
    "job user-assigned identities": r'resource\s+"azurerm_user_assigned_identity"\s+"jobs"',
    "service AcrPull role assignment": r'resource\s+"azurerm_role_assignment"\s+"container_app_services_acr_pull"',
    "job AcrPull role assignment": r'resource\s+"azurerm_role_assignment"\s+"container_app_jobs_acr_pull"',
    "AcrPull role name": r'role_definition_name\s*=\s*"AcrPull"',
    "ACR role assignment scope": r"scope\s*=\s*var\.container_registry_id",
    "role assignment principal type": r'principal_type\s*=\s*"ServicePrincipal"',
    "new identity AAD check skip": r"skip_service_principal_aad_check\s*=\s*true",
    "services depend on AcrPull": r"depends_on\s*=\s*\[[^\]]*azurerm_role_assignment\.container_app_services_acr_pull",
    "jobs depend on AcrPull": r"depends_on\s*=\s*\[[^\]]*azurerm_role_assignment\.container_app_jobs_acr_pull",
    "job user-assigned identity attachment": r"user_assigned_resource_ids\s*=\s*\[\s*azurerm_user_assigned_identity\.jobs\[each\.key\]\.id\s*\]",
    "job registry uses user-assigned identity": r"identity\s*=\s*azurerm_user_assigned_identity\.jobs\[each\.key\]\.id",
    "job secret references use user-assigned identity": r"identity\s*=\s*azurerm_user_assigned_identity\.jobs\[job_key\]\.id",
    "job identity output principal ID": r"container_app_job_identities[\s\S]*principal_id\s*=\s*identity\.principal_id",
}

for name, pattern in required_stage_patterns.items():
    if re.search(pattern, stage_content, re.IGNORECASE | re.MULTILINE | re.DOTALL) is None:
        errors.append(f"missing {name}")

if re.search(r"container_registry_identity\s*=\s*\"System\"", stage_content):
    errors.append("app_runtime must not use System identity for ACR pulls")

if re.search(r"system_assigned\s*=\s*true", stage_content):
    errors.append("app_runtime jobs must not use system-assigned identity for ACR pulls")

required_helper_patterns = {
    "foundation ACR ID output lookup": r'terraform_output_raw\s+foundation\s+"\$\{TF_WORKSPACE\}"\s+container_registry_id',
    "app_runtime ACR ID var": r'-var=container_registry_id=\$\{container_registry_id\}',
    "app_runtime image var file": r'-var-file=\$\{APP_RUNTIME_IMAGES_TFVARS_PATH\}',
}

for name, pattern in required_helper_patterns.items():
    if re.search(pattern, helper_content, re.IGNORECASE | re.MULTILINE | re.DOTALL) is None:
        errors.append(f"terraform-stage-deploy missing {name}")

required_readme_terms = [
    "AcrPull",
    "container_registry_id",
    "user-assigned managed identities",
]

for term in required_readme_terms:
    if term not in readme_content:
        errors.append(f"README missing required term: {term}")

if errors:
    print("Terraform app runtime validation failed:")
    for error in errors:
        print(f"  - {error}")
    sys.exit(1)

print("Terraform app runtime validation passed.")
PY
