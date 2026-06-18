#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STAGE_DIR="$ROOT_DIR/infrastructure/azure/stages/data_platform"
MODULES_DIR="$ROOT_DIR/infrastructure/azure/modules"
README="$STAGE_DIR/README.md"

python3 - "$STAGE_DIR" "$MODULES_DIR" "$README" <<'PY'
from __future__ import annotations

import pathlib
import re
import sys

stage_dir = pathlib.Path(sys.argv[1])
modules_dir = pathlib.Path(sys.argv[2])
readme_path = pathlib.Path(sys.argv[3])

stage_content = "\n".join(
    path.read_text(encoding="utf-8")
    for path in sorted(stage_dir.glob("*.tf"))
)
readme_content = readme_path.read_text(encoding="utf-8")

module_content = {
    name: "\n".join(
        path.read_text(encoding="utf-8")
        for path in sorted((modules_dir / name).glob("*.tf"))
    )
    for name in [
        "postgresql_flexible_server",
        "resource_group",
        "storage_account",
    ]
}

errors: list[str] = []

required_stage_patterns = {
    "workspace guard": r'resource\s+"terraform_data"\s+"workspace_guard"',
    "actual workspace validation": r'terraform\.workspace\s*==\s*local\.expected_workspace_name',
    "default workspace rejection": r'terraform\.workspace\s*!=\s*"default"',
    "data resource group wrapper": r'module\s+"data_resource_group"\s+\{[^}]*source\s*=\s*"\.\./\.\./modules/resource_group"',
    "local PostgreSQL wrapper": r'module\s+"product_metadata_store"\s+\{[^}]*source\s*=\s*"\.\./\.\./modules/postgresql_flexible_server"',
    "local Storage Account wrapper": r'module\s+"product_storage"\s+\{[^}]*source\s*=\s*"\.\./\.\./modules/storage_account"',
    "PostgreSQL delegated subnet contract": r'network_subnet_ids\["postgresql_delegated"\]',
    "Storage private endpoint subnet contract": r'network_subnet_ids\["private_endpoints"\]',
    "PostgreSQL private DNS contract": r'private_dns_zone_ids\["postgresql_private_access"\]',
    "Blob private DNS contract": r'private_dns_zone_ids\["blob"\]',
    "observability diagnostic contract": r'diagnostic_destinations\["data_platform"\]',
    "PostgreSQL resource ID output": r'output\s+"postgresql_server_resource_id"',
    "PostgreSQL restore output": r'output\s+"postgresql_restore_contract"',
    "storage account resource ID output": r'output\s+"storage_account_resource_id"',
    "captured content storage output": r'output\s+"captured_content_storage_contract"',
    "operational storage output": r'output\s+"operational_storage_contract"',
}

for name, pattern in required_stage_patterns.items():
    if re.search(pattern, stage_content, re.IGNORECASE | re.MULTILINE | re.DOTALL) is None:
        errors.append(f"missing {name}")

if re.search(r'module\s+"[^"]+"\s+\{[^}]*source\s*=\s*"Azure/', stage_content, re.IGNORECASE | re.MULTILINE | re.DOTALL):
    errors.append("stage must not call external AVM modules directly")

for module_name, content in module_content.items():
    if not content:
        errors.append(f"missing module: {module_name}")
        continue
    if re.search(r'(?m)^\s*provider\s+"', content):
        errors.append(f"{module_name} module must not define provider blocks")

required_module_patterns = {
    "PostgreSQL AVM wrapper": (
        module_content["postgresql_flexible_server"],
        r'source\s*=\s*"Azure/avm-res-dbforpostgresql-flexibleserver/azurerm"',
    ),
    "PostgreSQL AVM version pin": (
        module_content["postgresql_flexible_server"],
        r'version\s*=\s*"0\.2\.2"',
    ),
    "PostgreSQL AVM telemetry disabled": (
        module_content["postgresql_flexible_server"],
        r'enable_telemetry\s*=\s*false',
    ),
    "PostgreSQL public access disabled": (
        module_content["postgresql_flexible_server"],
        r'public_network_access_enabled\s*=\s*false',
    ),
    "PostgreSQL backup retention": (
        module_content["postgresql_flexible_server"],
        r'backup_retention_days\s*=\s*var\.backup_retention_days',
    ),
    "PostgreSQL diagnostics": (
        module_content["postgresql_flexible_server"],
        r'diagnostic_settings\s*=\s*var\.diagnostic_settings',
    ),
    "Storage AVM wrapper": (
        module_content["storage_account"],
        r'source\s*=\s*"Azure/avm-res-storage-storageaccount/azurerm"',
    ),
    "Storage AVM version pin": (
        module_content["storage_account"],
        r'version\s*=\s*"0\.7\.2"',
    ),
    "Storage AVM telemetry disabled": (
        module_content["storage_account"],
        r'enable_telemetry\s*=\s*false',
    ),
    "Storage public access disabled": (
        module_content["storage_account"],
        r'public_network_access_enabled\s*=\s*false',
    ),
    "Storage shared key disabled": (
        module_content["storage_account"],
        r'shared_access_key_enabled\s*=\s*false',
    ),
    "Storage lifecycle management": (
        module_content["storage_account"],
        r'storage_management_policy_rule\s*=\s*var\.storage_management_policy_rule',
    ),
    "Storage blob diagnostics": (
        module_content["storage_account"],
        r'diagnostic_settings_blob\s*=\s*var\.diagnostic_settings_blob',
    ),
}

for name, (content, pattern) in required_module_patterns.items():
    if re.search(pattern, content, re.IGNORECASE | re.MULTILINE | re.DOTALL) is None:
        errors.append(f"missing {name}")

for forbidden in [
    r'output\s+"[^"]*(connection_string|storage_key|access_key|sas|credential|password|secret|token)[^"]*"',
    r'value\s*=.*\.(primary_connection_string|secondary_connection_string|primary_access_key|secondary_access_key)',
    r'output\s+"[^"]*(raw_terraform_state|raw_session|captured_content_payload|prompt|command_output|tool_output|tool_result|tenant_private)[^"]*"',
]:
    if re.search(forbidden, stage_content, re.IGNORECASE | re.MULTILINE | re.DOTALL):
        errors.append(f"forbidden sensitive output matched {forbidden}")

for module_name in ["postgresql_flexible_server", "storage_account"]:
    if re.search(r'output\s+"resource"\s+\{', module_content[module_name], re.IGNORECASE | re.MULTILINE | re.DOTALL):
        errors.append(f"{module_name} module must not output the full AVM resource object")

required_readme_terms = [
    "Persistence Boundaries",
    "Backup And Restore Assumptions",
    "Lifecycle Assumptions",
    "Downstream Outputs",
    "Provider And AVM Choices",
    "Runtime managed identity principal IDs are supplied by downstream runtime issues.",
    "Do not add .NET, xUnit, or C# tests for Terraform behavior.",
]

for term in required_readme_terms:
    if term not in readme_content:
        errors.append(f"README missing required term: {term}")

code_blocks = re.findall(r"```(?:[a-zA-Z0-9_-]+)?\n(.*?)```", readme_content, re.DOTALL)
for index, block in enumerate(code_blocks, start=1):
    if re.search(
        r'(connection_string|storage_key|access_key|sas|credential|password\s*=|secret\s*=|token\s*=|raw_terraform_state|captured_content_payload|prompt\s*=|command_output|tool_output|tool_result)',
        block,
        re.IGNORECASE,
    ):
        errors.append(f"README code block {index} contains a forbidden data-platform example")

if errors:
    print("Terraform data platform validation failed:")
    for error in errors:
        print(f"  - {error}")
    sys.exit(1)

print("Terraform data platform validation passed.")
PY
