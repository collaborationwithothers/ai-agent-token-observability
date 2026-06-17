#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FOUNDATION_DIR="$ROOT_DIR/infrastructure/azure/stages/foundation"
MODULES_DIR="$ROOT_DIR/infrastructure/azure/modules"
DESTROY_WORKFLOW="$ROOT_DIR/.github/workflows/terraform-destroy-plan.yml"

python3 - "$FOUNDATION_DIR" "$MODULES_DIR" "$DESTROY_WORKFLOW" <<'PY'
from __future__ import annotations

import pathlib
import re
import sys

foundation_dir = pathlib.Path(sys.argv[1])
modules_dir = pathlib.Path(sys.argv[2])
destroy_workflow = pathlib.Path(sys.argv[3])

tf_content = "\n".join(
    path.read_text(encoding="utf-8")
    for path in sorted(foundation_dir.glob("*.tf"))
)
key_vault_content = "\n".join(
    path.read_text(encoding="utf-8")
    for path in sorted((modules_dir / "key_vault").glob("*.tf"))
)
managed_identity_content = "\n".join(
    path.read_text(encoding="utf-8")
    for path in sorted((modules_dir / "managed_identity").glob("*.tf"))
)
destroy_content = destroy_workflow.read_text(encoding="utf-8")

errors: list[str] = []

required_tf_patterns = {
    "foundation resource group": r'resource\s+"azurerm_resource_group"\s+"[^"]+"',
    "foundation Azure Container Registry": r'resource\s+"azurerm_container_registry"\s+"[^"]+"',
    "ACR admin disabled": r"admin_enabled\s*=\s*false",
    "ACR SKU variable": r"sku\s*=\s*var\.container_registry_sku",
    "container_registry_login_server output": r'output\s+"container_registry_login_server"',
    "container_registry_id output": r'output\s+"container_registry_id"',
    "container_registry_name output": r'output\s+"container_registry_name"',
    "container_registry_resource_group_id output": r'output\s+"container_registry_resource_group_id"',
    "resource_group_ids contains foundation": r"resource_group_ids.*foundation|foundation.*azurerm_resource_group",
    "foundation Key Vault local wrapper": r'module\s+"key_vault"\s+\{[^}]*source\s*=\s*"\.\./\.\./modules/key_vault"',
    "foundation managed identity local wrapper": r'module\s+"deployment_identity"\s+\{[^}]*source\s*=\s*"\.\./\.\./modules/managed_identity"',
    "key_vault_id output": r'output\s+"key_vault_id"',
    "key_vault_name output": r'output\s+"key_vault_name"',
    "key_vault_uri output": r'output\s+"key_vault_uri"',
    "deployment_identities output": r'output\s+"deployment_identities"',
    "actual workspace validation": r'terraform\.workspace\s*==\s*local\.expected_workspace_name',
    "default workspace rejection": r'terraform\.workspace\s*!=\s*"default"',
    "deployment identity configured precondition": r'local\.deployment_identity_configured',
}

for name, pattern in required_tf_patterns.items():
    if re.search(pattern, tf_content, re.IGNORECASE | re.MULTILINE | re.DOTALL) is None:
        errors.append(f"missing {name}")

if re.search(r'module\s+"[^"]+"\s+\{[^}]*source\s*=\s*"Azure/', tf_content, re.IGNORECASE | re.MULTILINE | re.DOTALL) is not None:
    errors.append("foundation stage must not call external AVM modules directly")

module_patterns = {
    "Key Vault AVM wrapper source": (key_vault_content, r'source\s*=\s*"Azure/avm-res-keyvault-vault/azurerm"'),
    "Key Vault AVM version pin": (key_vault_content, r'version\s*=\s*"0\.10\.2"'),
    "Key Vault telemetry disabled": (key_vault_content, r'enable_telemetry\s*=\s*false'),
    "Key Vault RBAC role assignment passthrough": (key_vault_content, r'role_assignments\s*=\s*var\.role_assignments'),
    "Key Vault no secrets": (key_vault_content, r'secrets\s*=\s*\{\}'),
    "Managed identity AVM wrapper source": (managed_identity_content, r'source\s*=\s*"Azure/avm-res-managedidentity-userassignedidentity/azurerm"'),
    "Managed identity AVM version pin": (managed_identity_content, r'version\s*=\s*"0\.5\.0"'),
    "Managed identity telemetry disabled": (managed_identity_content, r'enable_telemetry\s*=\s*false'),
    "Managed identity client_id output": (managed_identity_content, r'output\s+"client_id"'),
    "Managed identity principal_id output": (managed_identity_content, r'output\s+"principal_id"'),
}

for name, (content, pattern) in module_patterns.items():
    if re.search(pattern, content, re.IGNORECASE | re.MULTILINE | re.DOTALL) is None:
        errors.append(f"missing {name}")

stage_options = set(re.findall(r"(?m)^\s*-\s*([a-z0-9_]+)\s*$", destroy_content))
if "foundation" in stage_options:
    errors.append("foundation must not be exposed as a terraform_stage destroy option")

disposable_orders = re.findall(r'DISPOSABLE_STAGE_ORDER\s*=\s*"([^"]*)"', destroy_content)
if not disposable_orders:
    errors.append("missing parsable DISPOSABLE_STAGE_ORDER assignment")
for order in disposable_orders:
    if "foundation" in set(order.split()):
        errors.append("foundation must not be included in DISPOSABLE_STAGE_ORDER")

if re.search(r'RETAINED_STAGE_DENY_LIST\s*=\s*"[^"]*\bfoundation\b', destroy_content) is None:
    errors.append("foundation must be included in RETAINED_STAGE_DENY_LIST")

if errors:
    print("Terraform foundation ACR validation failed:")
    for error in errors:
        print(f"  - {error}")
    sys.exit(1)

print("Terraform foundation ACR validation passed.")
PY
