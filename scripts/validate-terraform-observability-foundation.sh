#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STAGE_DIR="$ROOT_DIR/infrastructure/azure/stages/observability_foundation"
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
        "resource_group",
        "log_analytics_workspace",
        "application_insights",
        "monitor_workspace",
    ]
}

errors: list[str] = []

required_stage_patterns = {
    "workspace guard": r'resource\s+"terraform_data"\s+"workspace_guard"',
    "actual workspace validation": r'terraform\.workspace\s*==\s*local\.expected_workspace_name',
    "default workspace rejection": r'terraform\.workspace\s*!=\s*"default"',
    "local resource group wrapper": r'module\s+"observability_resource_group"\s+\{[^}]*source\s*=\s*"\.\./\.\./modules/resource_group"',
    "local Log Analytics wrapper": r'module\s+"log_analytics_workspace"\s+\{[^}]*source\s*=\s*"\.\./\.\./modules/log_analytics_workspace"',
    "local Application Insights wrapper": r'module\s+"application_insights"\s+\{[^}]*source\s*=\s*"\.\./\.\./modules/application_insights"',
    "local Azure Monitor workspace wrapper": r'module\s+"monitor_workspace"\s+\{[^}]*source\s*=\s*"\.\./\.\./modules/monitor_workspace"',
    "Log Analytics resource ID output": r'output\s+"log_analytics_workspace_resource_id"',
    "Log Analytics workspace ID output": r'output\s+"log_analytics_workspace_id"',
    "Application Insights resource ID output": r'output\s+"application_insights_resource_id"',
    "Application Insights App ID output": r'output\s+"application_insights_app_id"',
    "Azure Monitor workspace ID output": r'output\s+"monitor_workspace_resource_id"',
    "Azure Monitor query endpoint output": r'output\s+"monitor_workspace_query_endpoint"',
    "Azure Monitor default DCE output": r'output\s+"monitor_workspace_default_data_collection_endpoint_id"',
    "Azure Monitor default DCR output": r'output\s+"monitor_workspace_default_data_collection_rule_id"',
    "diagnostic destinations output": r'output\s+"diagnostic_destinations"',
    "metrics data source identifiers output": r'output\s+"metrics_data_source_identifiers"',
    "trace log data source identifiers output": r'output\s+"trace_log_data_source_identifiers"',
    "Log Analytics public ingestion default enabled": r'variable\s+"log_analytics_internet_ingestion_enabled"\s+\{.*?default\s*=\s*true',
    "Log Analytics public query default enabled": r'variable\s+"log_analytics_internet_query_enabled"\s+\{.*?default\s*=\s*true',
    "Application Insights public ingestion default enabled": r'variable\s+"application_insights_internet_ingestion_enabled"\s+\{.*?default\s*=\s*true',
    "Application Insights public query default enabled": r'variable\s+"application_insights_internet_query_enabled"\s+\{.*?default\s*=\s*true',
    "Azure Monitor workspace public network default enabled": r'variable\s+"monitor_workspace_public_network_access_enabled"\s+\{.*?default\s*=\s*true',
}

for name, pattern in required_stage_patterns.items():
    if re.search(pattern, stage_content, re.IGNORECASE | re.MULTILINE | re.DOTALL) is None:
        errors.append(f"missing {name}")

if re.search(r'module\s+"[^"]+"\s+\{[^}]*source\s*=\s*"Azure/', stage_content, re.IGNORECASE | re.MULTILINE | re.DOTALL):
    errors.append("stage must not call external AVM modules directly")

for module_name, content in module_content.items():
    if re.search(r'(?m)^\s*provider\s+"', content):
        errors.append(f"{module_name} module must not define provider blocks")

required_module_patterns = {
    "resource group AVM wrapper": (
        module_content["resource_group"],
        r'source\s*=\s*"Azure/avm-res-resources-resourcegroup/azurerm"',
    ),
    "Log Analytics AVM wrapper": (
        module_content["log_analytics_workspace"],
        r'source\s*=\s*"Azure/avm-res-operationalinsights-workspace/azurerm"',
    ),
    "Log Analytics AVM version pin": (
        module_content["log_analytics_workspace"],
        r'version\s*=\s*"0\.5\.1"',
    ),
    "Log Analytics AVM telemetry disabled": (
        module_content["log_analytics_workspace"],
        r'enable_telemetry\s*=\s*false',
    ),
    "Application Insights AVM wrapper": (
        module_content["application_insights"],
        r'source\s*=\s*"Azure/avm-res-insights-component/azurerm"',
    ),
    "Application Insights AVM version pin": (
        module_content["application_insights"],
        r'version\s*=\s*"0\.4\.0"',
    ),
    "Application Insights AVM telemetry disabled": (
        module_content["application_insights"],
        r'enable_telemetry\s*=\s*false',
    ),
    "Azure Monitor workspace AzureRM resource": (
        module_content["monitor_workspace"],
        r'resource\s+"azurerm_monitor_workspace"\s+"this"',
    ),
    "Azure Monitor workspace public network variable": (
        module_content["monitor_workspace"],
        r'public_network_access_enabled\s*=\s*var\.public_network_access_enabled',
    ),
}

for name, (content, pattern) in required_module_patterns.items():
    if re.search(pattern, content, re.IGNORECASE | re.MULTILINE | re.DOTALL) is None:
        errors.append(f"missing {name}")

for forbidden in [
    r'resource\s+"azurerm_dashboard_grafana"',
    r'resource\s+"grafana_',
    r'resource\s+"azurerm_monitor_metric_alert"',
    r'resource\s+"azurerm_monitor_scheduled_query_rules_',
    r'resource\s+"azurerm_monitor_action_group"',
]:
    if re.search(forbidden, stage_content, re.IGNORECASE | re.MULTILINE | re.DOTALL):
        errors.append(f"forbidden downstream Grafana or alert resource matched {forbidden}")

for forbidden in [
    r'output\s+"[^"]*(connection_string|instrumentation_key|secret|token|key)[^"]*"',
    r'value\s*=.*\.(connection_string|instrumentation_key)',
    r'output\s+"[^"]*(raw_session|captured_content|prompt|command_output|tool_output|tool_result|tenant_private_payload)[^"]*"',
]:
    if re.search(forbidden, stage_content, re.IGNORECASE | re.MULTILINE | re.DOTALL):
        errors.append(f"forbidden sensitive output matched {forbidden}")

for module_name in ["log_analytics_workspace", "application_insights"]:
    if re.search(r'output\s+"resource"\s+\{', module_content[module_name], re.IGNORECASE | re.MULTILINE | re.DOTALL):
        errors.append(f"{module_name} module must not output the full AVM resource object")

required_readme_terms = [
    "Metric And Log Boundaries",
    "Downstream Consumers",
    "Exclusions",
    "Non-Punitive Aggregate Expectations",
    "Provider And AVM Choices",
    "Azure Monitor workspace uses AzureRM",
    "Do not add .NET, xUnit, or C# tests for Terraform behavior.",
]

for term in required_readme_terms:
    if term not in readme_content:
        errors.append(f"README missing required term: {term}")

code_blocks = re.findall(r"```(?:[a-zA-Z0-9_-]+)?\n(.*?)```", readme_content, re.DOTALL)
for index, block in enumerate(code_blocks, start=1):
    if re.search(r'(connection_string|instrumentation_key|secret\s*=|token\s*=|prompt\s*=|command_output|tool_output|tool_result|captured_content)', block, re.IGNORECASE):
        errors.append(f"README code block {index} contains a forbidden observability example")

if errors:
    print("Terraform observability foundation validation failed:")
    for error in errors:
        print(f"  - {error}")
    sys.exit(1)

print("Terraform observability foundation validation passed.")
PY
