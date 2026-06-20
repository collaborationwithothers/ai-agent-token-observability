#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STAGE_DIR="$ROOT_DIR/infrastructure/azure/stages/managed_grafana"
MODULE_DIR="$ROOT_DIR/infrastructure/azure/modules/managed_grafana"
DASHBOARD_DIR="$ROOT_DIR/infrastructure/grafana/dashboards"
DEPLOY_HELPER="$ROOT_DIR/scripts/terraform-stage-deploy.sh"
FOCUSED_VALIDATOR="$ROOT_DIR/scripts/validate-focused.sh"
PR_VALIDATOR="$ROOT_DIR/scripts/validate-pr.sh"
README="$STAGE_DIR/README.md"

python3 - "$ROOT_DIR" "$STAGE_DIR" "$MODULE_DIR" "$DASHBOARD_DIR" "$DEPLOY_HELPER" "$FOCUSED_VALIDATOR" "$PR_VALIDATOR" "$README" <<'PY'
from __future__ import annotations

import json
import pathlib
import re
import sys

root_dir = pathlib.Path(sys.argv[1])
stage_dir = pathlib.Path(sys.argv[2])
module_dir = pathlib.Path(sys.argv[3])
dashboard_dir = pathlib.Path(sys.argv[4])
deploy_helper = pathlib.Path(sys.argv[5])
focused_validator = pathlib.Path(sys.argv[6])
pr_validator = pathlib.Path(sys.argv[7])
readme_path = pathlib.Path(sys.argv[8])

stage_content = "\n".join(path.read_text(encoding="utf-8") for path in sorted(stage_dir.glob("*.tf")))
module_content = "\n".join(path.read_text(encoding="utf-8") for path in sorted(module_dir.glob("*.tf")))
deploy_content = deploy_helper.read_text(encoding="utf-8")
focused_content = focused_validator.read_text(encoding="utf-8")
pr_content = pr_validator.read_text(encoding="utf-8")
readme_content = readme_path.read_text(encoding="utf-8")
dashboard_files = sorted(dashboard_dir.glob("*.json"))

errors: list[str] = []

dashboard_contract = {
    "executive-cost-overview.json": {
        "title": "Executive Cost Overview",
        "uid": "tokenobs-executive-cost-overview",
    },
    "harness-and-model-operations.json": {
        "title": "Harness And Model Operations",
        "uid": "tokenobs-harness-model-operations",
    },
    "cache-and-hotspot-trends.json": {
        "title": "Cache And Hotspot Trends",
        "uid": "tokenobs-cache-hotspot-trends",
    },
    "ingestion-and-platform-health.json": {
        "title": "Ingestion And Platform Health",
        "uid": "tokenobs-ingestion-platform-health",
    },
}

allowed_dashboard_variables = {"environment", "region", "harness", "model"}
forbidden_dashboard_variables = {
    "user",
    "user_id",
    "userId",
    "product_user_id",
    "productUserId",
    "developer",
    "developer_id",
    "developerId",
    "session",
    "session_id",
    "sessionId",
    "credential",
    "credential_id",
    "credentialId",
    "trace",
    "trace_id",
    "traceId",
    "span_id",
    "spanId",
    "file",
    "file_path",
    "filePath",
    "prompt",
    "prompt_text",
    "promptText",
    "repository_path",
    "repositoryPath",
    "raw_content",
    "rawContent",
}
forbidden_dashboard_terms = [
    "leaderboard",
    "raw session",
    "raw content",
    "review queue",
    "evidence packet",
    "credential",
    "credentialid",
    "trace_id",
    "traceid",
    "span_id",
    "spanid",
    "session_id",
    "sessionid",
    "user_id",
    "userid",
    "product_user_id",
    "productuserid",
    "developer_id",
    "developerid",
    "repository_path",
    "repositorypath",
    "file_path",
    "filepath",
    "prompt_text",
    "prompttext",
    "command_output",
    "commandoutput",
    "tool_result",
    "toolresult",
]

required_stage_patterns = {
    "workspace guard": r'resource\s+"terraform_data"\s+"workspace_guard"',
    "actual workspace validation": r'terraform\.workspace\s*==\s*local\.expected_workspace_name',
    "default workspace rejection": r'terraform\.workspace\s*!=\s*"default"',
    "upstream contract guard": r'resource\s+"terraform_data"\s+"upstream_contract_guard"',
    "managed Grafana wrapper call": r'module\s+"managed_grafana"\s+\{[^}]*source\s*=\s*"\.\./\.\./modules/managed_grafana"',
    "observability resource group name input": r'observability_resource_group_name',
    "observability resource group ID input": r'observability_resource_group_id',
    "metrics data source input": r'metrics_data_source_identifiers',
    "aggregate metrics local": r'aggregate_metrics_data_source\s*=\s*var\.metrics_data_source_identifiers\["aggregate_metrics"\]',
    "aggregate boundary precondition": r'boundary\s*==\s*"aggregate_metrics_only"',
    "aggregate consumer precondition": r'contains\(local\.aggregate_metrics_data_source\.consumer_stages,\s*local\.stage_name\)',
    "Grafana provider source": r'source\s*=\s*"grafana/grafana"',
    "Grafana provider exact pin": r'version\s*=\s*"4\.39\.0"',
    "empty Grafana provider block": r'provider\s+"grafana"\s*\{\s*\}',
    "Token Observability folder": r'resource\s+"grafana_folder"\s+"token_observability"\s+\{[^}]*title\s*=\s*"Token Observability"[^}]*uid\s*=\s*"tokenobs"',
    "first release dashboard resources": r'resource\s+"grafana_dashboard"\s+"first_release"',
    "dashboard artifact local": r'grafana_dashboard_artifacts\s*=\s*\{',
    "dashboard file config": r'config_json\s*=\s*file\(each\.value\.path\)',
}

required_module_patterns = {
    "AzureRM Grafana resource": r'resource\s+"azurerm_dashboard_grafana"\s+"this"',
    "system assigned identity": r'type\s*=\s*"SystemAssigned"',
    "API keys disabled": r'api_key_enabled\s*=\s*false',
    "native public network setting": r'public_network_access_enabled\s*=\s*var\.public_network_access_enabled',
    "Standard SKU": r'sku\s*=\s*"Standard"',
    "Azure Monitor workspace integration": r'azure_monitor_workspace_integrations\s+\{[^}]*resource_id\s*=\s*var\.aggregate_metrics_data_source\.resource_id',
    "workspace-scoped role assignment": r'resource\s+"azurerm_role_assignment"\s+"aggregate_metrics_data_reader"\s+\{[^}]*scope\s*=\s*var\.aggregate_metrics_data_source\.resource_id',
    "Monitoring Data Reader role": r'role_definition_name\s*=\s*"Monitoring Data Reader"',
    "managed identity principal": r'principal_id\s*=\s*azurerm_dashboard_grafana\.this\.identity\[0\]\.principal_id',
    "service principal type": r'principal_type\s*=\s*"ServicePrincipal"',
    "aggregate output": r'output\s+"aggregate_metrics_data_source"',
}

for name, pattern in required_stage_patterns.items():
    if re.search(pattern, stage_content, re.IGNORECASE | re.MULTILINE | re.DOTALL) is None:
        errors.append(f"managed_grafana stage missing {name}")

for name, pattern in required_module_patterns.items():
    if re.search(pattern, module_content, re.IGNORECASE | re.MULTILINE | re.DOTALL) is None:
        errors.append(f"managed_grafana module missing {name}")

if re.search(r'(?m)^\s*provider\s+"', module_content):
    errors.append("managed_grafana module must not define provider blocks")

for relative in [
    "infrastructure/azure/stages/observability_foundation",
    "infrastructure/azure/stages/data_platform",
    "infrastructure/azure/stages/ai_services",
    "infrastructure/azure/stages/app_runtime",
    "infrastructure/azure/stages/edge",
    "infrastructure/azure/modules/monitor_workspace",
    "infrastructure/azure/modules/application_insights",
    "infrastructure/azure/modules/log_analytics_workspace",
]:
    content = "\n".join(path.read_text(encoding="utf-8") for path in sorted((root_dir / relative).glob("*.tf")))
    if re.search(r'resource\s+"azurerm_dashboard_grafana"', content, re.IGNORECASE | re.MULTILINE | re.DOTALL):
        errors.append(f"Grafana workspace resource must not be implemented in {relative}")

forbidden_tf_patterns = {
    "direct Log Analytics ownership": r'resource\s+"azurerm_log_analytics_workspace"',
    "direct Application Insights ownership": r'resource\s+"azurerm_application_insights"',
    "direct Azure Monitor workspace ownership": r'resource\s+"azurerm_monitor_workspace"',
    "provider auth arguments": r'(?m)^\s*(auth|http_headers|url)\s*=',
    "imperative dashboard import": r'az grafana dashboard import',
    "dashboard JSON outputs": r'(?m)^\s*output\s+"[^"]*(dashboard_json|dashboard_uid)[^"]*"',
    "service account tokens": r'(service_account_token|grafana_service_account|api_key\s*=|api_key_enabled\s*=\s*true)',
    "custom hostname": r'(custom_hostname|custom_domain|vanity|grafana\.tokenobs)',
    "raw telemetry sources": r'(trace_log_data_source_identifiers|operational_traces_logs|raw_session|raw_content|raw_logs|developer_ranking)',
}

combined_tf = f"{stage_content}\n{module_content}"
for name, pattern in forbidden_tf_patterns.items():
    if re.search(pattern, combined_tf, re.IGNORECASE | re.MULTILINE | re.DOTALL):
        errors.append(f"managed_grafana Terraform contains forbidden {name}")

grafana_resource_matches = set(re.findall(r'resource\s+"(grafana_[^"]+)"', combined_tf))
unexpected_grafana_resources = grafana_resource_matches - {"grafana_folder", "grafana_dashboard"}
if unexpected_grafana_resources:
    errors.append(f"managed_grafana Terraform contains unsupported Grafana resources: {sorted(unexpected_grafana_resources)}")

if not dashboard_dir.exists():
    errors.append("missing infrastructure/grafana/dashboards directory")
elif {path.name for path in dashboard_files} != set(dashboard_contract):
    errors.append("dashboard directory must contain exactly the first-release dashboard JSON artifacts")

for file_name, contract in dashboard_contract.items():
    path = dashboard_dir / file_name
    if not path.exists():
        errors.append(f"missing dashboard artifact: infrastructure/grafana/dashboards/{file_name}")
        continue

    raw = path.read_text(encoding="utf-8")
    try:
        dashboard = json.loads(raw)
    except json.JSONDecodeError as exc:
        errors.append(f"{file_name} is not valid JSON: {exc}")
        continue

    if dashboard.get("uid") != contract["uid"]:
        errors.append(f"{file_name} UID must be {contract['uid']}")
    if dashboard.get("title") != contract["title"]:
        errors.append(f"{file_name} title must be {contract['title']}")

    variables = {
        item.get("name")
        for item in dashboard.get("templating", {}).get("list", [])
        if isinstance(item, dict)
    }
    if variables != allowed_dashboard_variables:
        errors.append(f"{file_name} dashboard variables must be {sorted(allowed_dashboard_variables)}")
    forbidden_variables = variables & forbidden_dashboard_variables
    if forbidden_variables:
        errors.append(f"{file_name} contains forbidden dashboard variables: {sorted(forbidden_variables)}")

    panels = dashboard.get("panels", [])
    if not isinstance(panels, list) or not panels:
        errors.append(f"{file_name} must include at least one dashboard panel")

    lowered = raw.lower()
    for term in forbidden_dashboard_terms:
        if term in lowered:
            errors.append(f"{file_name} contains forbidden dashboard term: {term}")

for forbidden_output in [
    r'output\s+"[^"]*(connection_string|instrumentation_key|secret|token|key)[^"]*"',
    r'output\s+"[^"]*(session|product_user|developer|credential|trace|span|prompt|command|tool|content|ranking)[^"]*"',
]:
    if re.search(forbidden_output, stage_content, re.IGNORECASE | re.MULTILINE | re.DOTALL):
        errors.append(f"managed_grafana stage contains forbidden output matching {forbidden_output}")

required_helper_patterns = {
    "managed_grafana branch": r'\bmanaged_grafana\)',
    "observability resource group output": r'terraform_output_raw observability_foundation "\$\{TF_WORKSPACE\}" observability_resource_group_name',
    "resource group IDs output": r'terraform_output_json observability_foundation "\$\{TF_WORKSPACE\}" resource_group_ids',
    "metrics data source output": r'terraform_output_json observability_foundation "\$\{TF_WORKSPACE\}" metrics_data_source_identifiers',
    "aggregate-only jq guard": r'\.aggregate_metrics\.boundary == "aggregate_metrics_only"',
    "managed_grafana consumer jq guard": r'index\("managed_grafana"\)',
    "observability resource group var": r'-var=observability_resource_group_name=',
    "metrics data source var": r'-var=metrics_data_source_identifiers=',
}

for name, pattern in required_helper_patterns.items():
    if re.search(pattern, deploy_content, re.IGNORECASE | re.MULTILINE | re.DOTALL) is None:
        errors.append(f"terraform-stage-deploy.sh missing {name}")

for path_name, content in [
    ("validate-focused.sh", focused_content),
    ("validate-pr.sh", pr_content),
]:
    if "scripts/validate-terraform-managed-grafana.sh" not in content:
        errors.append(f"{path_name} must run validate-terraform-managed-grafana.sh")
    if "scripts/terraform-stage-check.sh managed_grafana" not in content:
        errors.append(f"{path_name} must run terraform-stage-check.sh managed_grafana")

required_readme_terms = [
    "Aggregate Metrics Boundary",
    "Azure Monitor workspace integration",
    "Monitoring Data Reader",
    "Repo-Versioned Dashboard Deployment",
    "Provider And AVM Choice",
    "No AzAPI workaround is required",
    "Do not add .NET, xUnit, or C# tests for Terraform behavior.",
]

for term in required_readme_terms:
    if term not in readme_content:
        errors.append(f"managed_grafana README missing required term: {term}")

if errors:
    print("Terraform managed Grafana validation failed:")
    for error in errors:
        print(f"  - {error}")
    sys.exit(1)

print("Terraform managed Grafana validation passed.")
PY
