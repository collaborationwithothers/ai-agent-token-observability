#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STAGE_DIR="$ROOT_DIR/infrastructure/azure/stages/ai_services"
MODULES_DIR="$ROOT_DIR/infrastructure/azure/modules"
README="$STAGE_DIR/README.md"
DEPLOY_SCRIPT="$ROOT_DIR/scripts/terraform-stage-deploy.sh"

python3 - "$STAGE_DIR" "$MODULES_DIR" "$README" "$DEPLOY_SCRIPT" <<'PY'
from __future__ import annotations

import pathlib
import re
import sys

stage_dir = pathlib.Path(sys.argv[1])
modules_dir = pathlib.Path(sys.argv[2])
readme_path = pathlib.Path(sys.argv[3])
deploy_script_path = pathlib.Path(sys.argv[4])

stage_content = "\n".join(
    path.read_text(encoding="utf-8")
    for path in sorted(stage_dir.glob("*.tf"))
)
readme_content = readme_path.read_text(encoding="utf-8")
deploy_script_content = deploy_script_path.read_text(encoding="utf-8")

module_names = ["cognitive_services_account", "resource_group"]
module_content = {
    name: "\n".join(
        path.read_text(encoding="utf-8")
        for path in sorted((modules_dir / name).glob("*.tf"))
    )
    for name in module_names
}

errors: list[str] = []

required_stage_patterns = {
    "workspace guard": r'resource\s+"terraform_data"\s+"workspace_guard"',
    "actual workspace validation": r'terraform\.workspace\s*==\s*local\.expected_workspace_name',
    "default workspace rejection": r'terraform\.workspace\s*!=\s*"default"',
    "AI resource group wrapper": r'module\s+"ai_resource_group"\s+\{[^}]*source\s*=\s*"\.\./\.\./modules/resource_group"',
    "local Cognitive Services wrapper": r'module\s+"ai_services_account"\s+\{[^}]*source\s*=\s*"\.\./\.\./modules/cognitive_services_account"',
    "AIServices kind": r'kind\s*=\s*"AIServices"',
    "observability diagnostic contract": r'diagnostic_destinations\["ai_services"\]',
    "LLM primary alias guard": r'recommendation-writer-primary',
    "model deployment alias transform": r'recommendation_model_deployments',
    "runtime Azure AI role assignments": r'role_definition_id_or_name\s*=\s*"Azure AI User"',
    "AI services account endpoint output": r'output\s+"ai_services_account_endpoint"',
    "Language PII contract output": r'output\s+"language_pii_detection_contract"',
    "Content Safety contract output": r'output\s+"content_safety_contract"',
    "recommendation deployment contract output": r'output\s+"recommendation_model_deployment_contracts"',
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
    "Cognitive Services AVM wrapper": (
        module_content["cognitive_services_account"],
        r'source\s*=\s*"Azure/avm-res-cognitiveservices-account/azurerm"',
    ),
    "Cognitive Services AVM version pin": (
        module_content["cognitive_services_account"],
        r'version\s*=\s*"0\.11\.0"',
    ),
    "Cognitive Services AVM telemetry disabled": (
        module_content["cognitive_services_account"],
        r'enable_telemetry\s*=\s*false',
    ),
    "Cognitive Services local auth disabled": (
        module_content["cognitive_services_account"],
        r'local_auth_enabled\s*=\s*false',
    ),
    "Cognitive Services diagnostics passthrough": (
        module_content["cognitive_services_account"],
        r'diagnostic_settings\s*=\s*var\.diagnostic_settings',
    ),
    "Cognitive Services deployments passthrough": (
        module_content["cognitive_services_account"],
        r'cognitive_deployments\s*=\s*var\.cognitive_deployments',
    ),
    "Cognitive Services RAI passthrough": (
        module_content["cognitive_services_account"],
        r'rai_policies\s*=\s*var\.rai_policies',
    ),
    "Cognitive Services private endpoints passthrough": (
        module_content["cognitive_services_account"],
        r'private_endpoints\s*=\s*var\.private_endpoints',
    ),
}

for name, (content, pattern) in required_module_patterns.items():
    if re.search(pattern, content, re.IGNORECASE | re.MULTILINE | re.DOTALL) is None:
        errors.append(f"missing {name}")

for forbidden in [
    r'output\s+"[^"]*(connection_string|access_key|api_key|subscription_key|sas|credential|password|secret|token)[^"]*"',
    r'value\s*=.*\.(primary_access_key|secondary_access_key|resource_sensitive)',
    r'output\s+"[^"]*(raw_prompt|raw_terraform_state|raw_session|captured_content_payload|model_request|model_response|command_output|tool_output|tool_result|tenant_private)[^"]*"',
]:
    if re.search(forbidden, stage_content, re.IGNORECASE | re.MULTILINE | re.DOTALL):
        errors.append(f"forbidden sensitive stage output matched {forbidden}")
    if re.search(forbidden, module_content["cognitive_services_account"], re.IGNORECASE | re.MULTILINE | re.DOTALL):
        errors.append(f"forbidden sensitive module output matched {forbidden}")

for forbidden_output in [
    r'output\s+"resource"\s+\{',
    r'output\s+"resource_sensitive"\s+\{',
    r'output\s+"primary_access_key"\s+\{',
    r'output\s+"secondary_access_key"\s+\{',
]:
    if re.search(forbidden_output, module_content["cognitive_services_account"], re.IGNORECASE | re.MULTILINE | re.DOTALL):
        errors.append(f"cognitive_services_account module exposes forbidden AVM output: {forbidden_output}")

required_readme_terms = [
    "Resource Boundaries",
    "Language PII And Content Safety",
    "Model Alias Policy",
    "Identity And Access",
    "Public And Private Access",
    "Downstream Outputs",
    "Provider And AVM Choices",
    "Do not add .NET, xUnit, or C# tests for Terraform behavior.",
    "Product logic must use aliases such as `recommendation-writer-primary`, not hardcoded model names.",
]

for term in required_readme_terms:
    if term not in readme_content:
        errors.append(f"README missing required term: {term}")

required_deploy_patterns = {
    "AI services stage derives observability diagnostic destinations": r'(?ms)ai_services\).*?diagnostic_destinations="\$\(terraform_output_json observability_foundation "\$\{TF_WORKSPACE\}" diagnostic_destinations\)"',
    "AI services stage passes diagnostic destinations var": r'(?ms)ai_services\).*?var_args\+=\("-var=diagnostic_destinations=\$\{diagnostic_destinations\}"\)',
}

for name, pattern in required_deploy_patterns.items():
    if re.search(pattern, deploy_script_content) is None:
        errors.append(f"deploy script missing {name}")

code_blocks = re.findall(r"```(?:[a-zA-Z0-9_-]+)?\n(.*?)```", readme_content, re.DOTALL)
for index, block in enumerate(code_blocks, start=1):
    if re.search(
        r'(connection_string|access_key|api_key|subscription_key|sas|credential|password\s*=|secret\s*=|token\s*=|raw_prompt|captured_content_payload|model_request|model_response|command_output|tool_output|tool_result)',
        block,
        re.IGNORECASE,
    ):
        errors.append(f"README code block {index} contains a forbidden AI services example")

if errors:
    print("Terraform AI services validation failed:")
    for error in errors:
        print(f"  - {error}")
    sys.exit(1)

print("Terraform AI services validation passed.")
PY
