#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STAGE_DIR="$ROOT_DIR/infrastructure/azure/stages/network_private_data_plane"
MODULES_DIR="$ROOT_DIR/infrastructure/azure/modules"

python3 - "$STAGE_DIR" "$MODULES_DIR" <<'PY'
from __future__ import annotations

import pathlib
import re
import sys

stage_dir = pathlib.Path(sys.argv[1])
modules_dir = pathlib.Path(sys.argv[2])

stage_content = "\n".join(
    path.read_text(encoding="utf-8")
    for path in sorted(stage_dir.glob("*.tf"))
)

module_content = {
    name: "\n".join(
        path.read_text(encoding="utf-8")
        for path in sorted((modules_dir / name).glob("*.tf"))
    )
    for name in [
        "resource_group",
        "virtual_network",
        "network_security_group",
        "private_dns_zone",
    ]
}

errors: list[str] = []

required_stage_patterns = {
    "workspace guard": r'resource\s+"terraform_data"\s+"workspace_guard"',
    "actual workspace validation": r'terraform\.workspace\s*==\s*local\.expected_workspace_name',
    "default workspace rejection": r'terraform\.workspace\s*!=\s*"default"',
    "local resource group wrapper": r'module\s+"network_resource_group"\s+\{[^}]*source\s*=\s*"\.\./\.\./modules/resource_group"',
    "local virtual network wrapper": r'module\s+"virtual_network"\s+\{[^}]*source\s*=\s*"\.\./\.\./modules/virtual_network"',
    "local network security group wrapper": r'module\s+"network_security_groups"\s+\{[^}]*source\s*=\s*"\.\./\.\./modules/network_security_group"',
    "local private DNS zone wrapper": r'module\s+"private_dns_zones"\s+\{[^}]*source\s*=\s*"\.\./\.\./modules/private_dns_zone"',
    "private endpoint subnet output": r'output\s+"subnet_ids"',
    "private DNS zone IDs output": r'output\s+"private_dns_zone_ids"',
    "NSG IDs output": r'output\s+"network_security_group_ids"',
    "PostgreSQL delegated subnet": r'Microsoft\.DBforPostgreSQL/flexibleServers',
    "Container Apps delegated subnet": r'Microsoft\.App/environments',
    "Container Apps DNS zone": r'privatelink\.\$\{var\.azure_region\}\.azurecontainerapps\.io',
    "OpenAI DNS zone": r'privatelink\.openai\.azure\.com',
    "Storage blob DNS zone": r'privatelink\.blob\.core\.windows\.net',
}

for name, pattern in required_stage_patterns.items():
    if re.search(pattern, stage_content, re.IGNORECASE | re.MULTILINE | re.DOTALL) is None:
        errors.append(f"missing {name}")

if re.search(r'module\s+"[^"]+"\s+\{[^}]*source\s*=\s*"Azure/', stage_content, re.IGNORECASE | re.MULTILINE | re.DOTALL):
    errors.append("stage must not call external AVM modules directly")

for forbidden in [
    r'resource\s+"azurerm_postgresql_flexible_server"',
    r'resource\s+"azurerm_storage_account"',
    r'resource\s+"azurerm_cognitive_account"',
    r'resource\s+"azurerm_container_app_environment"',
    r'resource\s+"azurerm_container_app"',
    r'resource\s+"azurerm_dashboard_grafana"',
    r'resource\s+"azurerm_cdn_frontdoor_profile"',
]:
    if re.search(forbidden, stage_content, re.IGNORECASE | re.MULTILINE | re.DOTALL):
        errors.append(f"forbidden downstream resource matched {forbidden}")

required_module_patterns = {
    "resource group AVM wrapper": (
        module_content["resource_group"],
        r'source\s*=\s*"Azure/avm-res-resources-resourcegroup/azurerm"',
    ),
    "resource group AVM version pin": (
        module_content["resource_group"],
        r'version\s*=\s*"0\.4\.0"',
    ),
    "VNet AVM wrapper": (
        module_content["virtual_network"],
        r'source\s*=\s*"Azure/avm-res-network-virtualnetwork/azurerm"',
    ),
    "VNet AVM version pin": (
        module_content["virtual_network"],
        r'version\s*=\s*"0\.19\.0"',
    ),
    "NSG AVM wrapper": (
        module_content["network_security_group"],
        r'source\s*=\s*"Azure/avm-res-network-networksecuritygroup/azurerm"',
    ),
    "NSG AVM version pin": (
        module_content["network_security_group"],
        r'version\s*=\s*"0\.5\.1"',
    ),
    "Private DNS AVM wrapper": (
        module_content["private_dns_zone"],
        r'source\s*=\s*"Azure/avm-res-network-privatednszone/azurerm"',
    ),
    "Private DNS AVM version pin": (
        module_content["private_dns_zone"],
        r'version\s*=\s*"0\.5\.0"',
    ),
    "AVM telemetry disabled": (
        "\n".join(module_content.values()),
        r'enable_telemetry\s*=\s*false',
    ),
}

for name, (content, pattern) in required_module_patterns.items():
    if re.search(pattern, content, re.IGNORECASE | re.MULTILINE | re.DOTALL) is None:
        errors.append(f"missing {name}")

if errors:
    print("Terraform network private data plane validation failed:")
    for error in errors:
        print(f"  - {error}")
    sys.exit(1)

print("Terraform network private data plane validation passed.")
PY
