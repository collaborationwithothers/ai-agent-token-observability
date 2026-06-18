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
    r"\baz\s+acr\s+login\b",
    r"docker/build-push-action",
    r"\bdocker\s+push\b",
)

REQUIRED_PATTERNS = {
    "workflow_dispatch trigger": r"(?m)^\s*workflow_dispatch\s*:",
    "least privilege contents permission": r"(?m)^\s*contents\s*:\s*read\s*$",
    "OIDC id-token permission": r"(?m)^\s*id-token\s*:\s*write\s*$",
    "managed runner group": r"(?m)^\s*group\s*:\s*consultwithcloud-azure\s*$",
    "managed runner label": r"(?m)^\s*labels\s*:\s*\[\s*gh-linux\s*\]\s*$",
    "job-level workflow_dispatch gate": r"github\.event_name\s*==\s*'workflow_dispatch'",
    "job-level repository gate": r"github\.repository\s*==\s*'collaborationwithothers/ai-agent-token-observability'",
    "job-level actor gate": r"github\.actor\s*==\s*'haripraghash'",
    "job-level main branch gate": r"github\.ref\s*==\s*'refs/heads/main'",
    "expected repository gate": r"GITHUB_REPOSITORY.*REQUIRED_REPOSITORY|REQUIRED_REPOSITORY.*GITHUB_REPOSITORY",
    "workflow_dispatch event gate": r"GITHUB_EVENT_NAME.*workflow_dispatch|workflow_dispatch.*GITHUB_EVENT_NAME",
    "expected actor gate": r"GITHUB_ACTOR.*haripraghash|haripraghash.*GITHUB_ACTOR",
    "full ref gate": r"GITHUB_REF.*refs/heads/main|refs/heads/main.*GITHUB_REF",
    "branch gate": r"GITHUB_REF_NAME",
    "environment gate": r"dv\|qa\|pp\|pd|dv.*qa.*pp.*pd",
    "region gate": r"eastus\|eastus2\|westeurope|eastus.*eastus2.*westeurope",
    "customer organization slug gate": r"CUSTOMER_ORGANIZATION_SLUG|customer_organization_slug",
    "customer organization slug default": r"customer_organization_slug\s*:.*?default\s*:\s*internal",
    "workspace derivation": r"TF_WORKSPACE=.*ENVIRONMENT.*AZURE_REGION.*CUSTOMER_ORGANIZATION_SLUG",
    "default workspace denial": r"TF_WORKSPACE.*default|default.*TF_WORKSPACE",
    "environment protection": r"(?m)^\s*environment\s*:",
}

IMAGE_PUBLISH_PATTERNS = (
    r"\baz\s+acr\s+login\b",
    r"docker/build-push-action",
    r"\bdocker\s+push\b",
    r"ACR_LOGIN_SERVER|acr_login_server|container_registry_login_server",
)

IMAGE_PUBLISH_REQUIRED_PATTERNS = {
    "ACR login server guard": r"ACR_LOGIN_SERVER.*azurecr\.io|azurecr\.io.*ACR_LOGIN_SERVER",
    "foundation Terraform state initialization": r"infrastructure/azure/stages/foundation",
    "foundation container registry login server output": r"terraform\s+-chdir=.*foundation.*output\s+-raw\s+container_registry_login_server|terraform\s+output\s+-raw\s+container_registry_login_server",
    "Product Dashboard image": r"product-dashboard",
    "Product API image": r"product-api",
    "Product Ingestion image": r"product-ingestion",
    "Product Jobs image": r"product-jobs",
    "image digest artifact": r"app-runtime-images\.auto\.tfvars\.json|image-digests",
}

DESTROY_REQUIRED_PATTERNS = {
    "deletion target gate": r"(?m)^\s*(DELETION_TARGET|deletion_target)\s*:",
    "derived destroy scope": r"DESTROY_SCOPE",
    "disposable stage allow-list": r"DISPOSABLE_STAGE_ORDER",
    "retained shared stage deny-list": r"RETAINED_STAGE_DENY_LIST",
    "environment and stage destroy scopes": r"environment\|stage|stage\|environment",
    "saved destroy plan file": r"DESTROY_PLAN_FILE",
    "destroy plan preview": r"terraform\s+-chdir=.*plan\s+-destroy.*-out=.*DESTROY_PLAN_FILE",
    "approved destroy plan apply": r"terraform\s+-chdir=.*apply\s+-input=false\s+.*DESTROY_PLAN_FILE",
    "same-run plan dependency": r"needs\s*:\s*destroy-plan",
    "same-run artifact download": r"actions/download-artifact",
    "deletion approval environment": r"(?m)^\s*name\s*:\s*terraform-deletion-approval\s*$",
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


def is_destroy_capable(content: str) -> bool:
    return re.search(r"plan\s+-destroy|destroy_scope|DESTROY_SCOPE", content, re.IGNORECASE) is not None


def is_image_publish_capable(content: str) -> bool:
    return any(re.search(pattern, content, re.IGNORECASE) for pattern in IMAGE_PUBLISH_PATTERNS)


def forbidden_trigger_errors(content: str) -> list[str]:
    errors: list[str] = []
    for trigger in FORBIDDEN_TRIGGERS:
        list_pattern = rf"(?m)^\s*on\s*:\s*\[[^\]]*\b{re.escape(trigger)}\b"
        scalar_pattern = rf"(?m)^\s*on\s*:\s*{re.escape(trigger)}\s*$"
        nested_block = False
        in_on_block = False
        for line in content.splitlines():
            if re.match(r"^\S", line):
                in_on_block = line.startswith("on:")
                continue
            if in_on_block and re.match(rf"^[ \t]+{re.escape(trigger)}\s*:", line):
                nested_block = True
                break
        if nested_block or re.search(list_pattern, content) or re.search(scalar_pattern, content):
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


def destroy_required_errors(content: str) -> list[str]:
    errors = [
        f"missing {name}"
        for name, pattern in DESTROY_REQUIRED_PATTERNS.items()
        if re.search(pattern, content, re.IGNORECASE | re.MULTILINE | re.DOTALL) is None
    ]

    forbidden_dispatch_inputs = (
        "expected_repository",
        "terraform_workspace",
        "edge_log_analytics_workspace_id",
        "apply_destroy",
        "reviewed_destroy_plan_run_id",
        "reason",
    )
    dispatch_inputs = dispatch_input_names(content)
    for input_name in forbidden_dispatch_inputs:
        if input_name in dispatch_inputs:
            errors.append(f"deletion workflow must not expose {input_name} as a dispatch input")
    forbidden_legacy_references = (
        "REQUESTED_TERRAFORM_WORKSPACE",
        "EDGE_LOG_ANALYTICS_WORKSPACE_ID",
        "APPLY_DESTROY",
        "REVIEWED_DESTROY_PLAN_RUN_ID",
        "inputs.terraform_workspace",
        "inputs.edge_log_analytics_workspace_id",
        "inputs.apply_destroy",
        "inputs.reviewed_destroy_plan_run_id",
        "inputs.reason",
    )
    for reference in forbidden_legacy_references:
        if reference in content:
            errors.append(f"deletion workflow must not reference {reference}")

    stage_options = re.findall(r"(?m)^\s*-\s*([a-z0-9_]+)\s*$", content)
    retained_options = sorted({"foundation", "public_dns"} & set(stage_options))
    if retained_options:
        errors.append(f"retained shared stage exposed as workflow input option: {', '.join(retained_options)}")

    disposable_stage_orders = re.findall(r"DISPOSABLE_STAGE_ORDER\s*=\s*\"([^\"]*)\"", content)
    if not disposable_stage_orders:
        errors.append("missing parsable DISPOSABLE_STAGE_ORDER assignment")
    for order in disposable_stage_orders:
        retained_order_stages = sorted({"foundation", "public_dns"} & set(order.split()))
        if retained_order_stages:
            errors.append(f"retained shared stage present in disposable destroy order: {', '.join(retained_order_stages)}")

    retained_stage_command_paths = sorted(set(re.findall(
        r"terraform\s+-chdir\s*=\s*[\"']?infrastructure/azure/stages/(foundation|public_dns)(?:[\"'\s]|$)",
        content,
        re.IGNORECASE,
    )))
    if retained_stage_command_paths:
        errors.append(f"retained shared stage targeted by Terraform command: {', '.join(retained_stage_command_paths)}")

    if re.search(r"run-id:\s*\${{\s*github\.run_id\s*}}", content):
        errors.append("destroy apply must not download a plan artifact from the current run")

    return errors


def image_publish_required_errors(content: str) -> list[str]:
    errors = [
        f"missing {name}"
        for name, pattern in IMAGE_PUBLISH_REQUIRED_PATTERNS.items()
        if re.search(pattern, content, re.IGNORECASE | re.MULTILINE | re.DOTALL) is None
    ]

    if re.search(r"(?m)^\s*packages\s*:\s*write\s*$", content):
        errors.append("ACR image publish workflow must not grant packages write")
    if re.search(r"ghcr\.io|GHCR", content, re.IGNORECASE):
        errors.append("ACR image publish workflow must not reference GHCR")
    if re.search(r"(?m)^\s*acr_login_server\s*:", content):
        errors.append("ACR image publish workflow must not expose acr_login_server as a dispatch input")
    dispatch_inputs = dispatch_input_names(content)
    if "terraform_workspace" in dispatch_inputs:
        errors.append("ACR image publish workflow must not expose terraform_workspace as a dispatch input")
    if "confirmation" in dispatch_inputs:
        errors.append("ACR image publish workflow must not expose confirmation as a dispatch input")
    for reference in (
        "REQUESTED_TERRAFORM_WORKSPACE",
        "inputs.terraform_workspace",
        "CONFIRMATION",
        "inputs.confirmation",
    ):
        if reference in content:
            errors.append(f"ACR image publish workflow must not reference {reference}")
    if re.search(r"terraform_workspace\s*=\s*.*TF_WORKSPACE|terraform_workspace.*GITHUB_OUTPUT|GITHUB_OUTPUT.*terraform_workspace", content, re.IGNORECASE) is None:
        errors.append("ACR image publish workflow must publish the derived workspace as a step output")

    azure_login_line = first_line(content, "azure/login")
    foundation_output_line = first_line(content, "container_registry_login_server")
    acr_login_line = first_line(content, "az acr login")
    if azure_login_line is not None and acr_login_line is not None and acr_login_line < azure_login_line:
        errors.append("ACR login must run after Azure login")
    if foundation_output_line is not None and acr_login_line is not None and foundation_output_line > acr_login_line:
        errors.append("foundation container registry output must be read before ACR login")

    return errors


def dispatch_input_names(content: str) -> set[str]:
    match = re.search(
        r"(?ms)^on:\n\s+workflow_dispatch:\n\s+inputs:\n(?P<inputs>.*?)(?=^\S)",
        content,
    )
    if match is None:
        return set()
    return set(re.findall(r"(?m)^\s{6}([A-Za-z0-9_]+)\s*:", match.group("inputs")))


def is_terraform_plan_workflow(path: pathlib.Path, content: str) -> bool:
    return path.name == "terraform-plan.yml" or re.search(
        r"(?m)^name:\s*(?:(?:Safe|Unsafe)\s+)?Terraform (?:Plan|Deploy)(?:\s+Fixture)?\s*$",
        content,
    ) is not None


def is_public_dns_workflow(path: pathlib.Path, content: str) -> bool:
    return path.name == "terraform-public-dns.yml" or re.search(
        r"(?m)^name:\s*(?:(?:Safe|Unsafe)\s+)?Terraform Public DNS(?:\s+Fixture)?\s*$",
        content,
    ) is not None


def terraform_plan_required_errors(content: str) -> list[str]:
    errors: list[str] = []
    dispatch_inputs = dispatch_input_names(content)
    if "terraform_workspace" in dispatch_inputs:
        errors.append("Terraform plan workflow must not expose terraform_workspace as a dispatch input")
    if "confirmation" in dispatch_inputs:
        errors.append("Terraform plan workflow must not expose confirmation as a dispatch input")
    if "REQUESTED_TERRAFORM_WORKSPACE" in content:
        errors.append("Terraform plan workflow must not validate a user-entered Terraform workspace")
    if "inputs.terraform_workspace" in content:
        errors.append("Terraform plan workflow must not read inputs.terraform_workspace")
    if "CONFIRMATION" in content or "inputs.confirmation" in content:
        errors.append("Terraform plan workflow must not require a user-entered confirmation")
    if re.search(r"terraform_workspace\s*=\s*.*TF_WORKSPACE|terraform_workspace.*GITHUB_OUTPUT|GITHUB_OUTPUT.*terraform_workspace", content, re.IGNORECASE) is None:
        errors.append("Terraform plan workflow must publish the derived workspace as a step output")
    required_patterns = {
        "environment-scoped stage order": r"ENVIRONMENT_STAGE_ORDER=.*foundation.*network_private_data_plane.*observability_foundation.*data_platform.*ai_services.*app_runtime.*managed_grafana.*edge",
        "public DNS exclusion": r"public_dns.*retained shared DNS infrastructure|retained public_dns stage is excluded|EXCLUDED_SHARED_STAGES=.*public_dns",
        "derived deployment scope": r"deployment_scope.*GITHUB_OUTPUT|GITHUB_OUTPUT.*deployment_scope|deployment_scope\s*=\s*.*DEPLOYMENT_SCOPE",
        "derived stage selection": r"deploy_foundation.*GITHUB_OUTPUT|GITHUB_OUTPUT.*deploy_foundation|deploy_foundation\s*=\s*.*DEPLOY_FOUNDATION",
        "stage plan script": r"terraform-stage-deploy\.sh\s+plan",
        "plan artifact upload": r"actions/upload-artifact",
        "same-run plan dependency": r"needs\s*:\s*\[\s*select\s*,\s*plan|needs\s*:\s*plan-",
        "approved apply artifact download": r"actions/download-artifact",
        "terraform apply approval environment": r"(?m)^\s*name\s*:\s*terraform-apply\s*$",
        "saved plan apply": r"terraform-stage-deploy\.sh\s+apply",
    }
    for name, pattern in required_patterns.items():
        if re.search(pattern, content, re.IGNORECASE | re.MULTILINE | re.DOTALL) is None:
            errors.append(f"missing {name}")
    if re.search(r"terraform\s+-chdir=.*apply\s+-input=false(?:\s|$)(?!.*PLAN_FILE)", content, re.IGNORECASE):
        errors.append("Terraform apply must use an approved saved plan file")
    forbidden_download_inputs = {
        "run-id": r"(?m)^\s*run-id\s*:",
        "repository": r"(?m)^\s*repository\s*:",
        "github-token": r"(?m)^\s*github-token\s*:",
    }
    for name, pattern in forbidden_download_inputs.items():
        if re.search(pattern, content, re.IGNORECASE | re.MULTILINE):
            errors.append(f"Terraform deploy workflow must not set download-artifact {name}")
    if re.search(r"(?m)^\s*-\s*public_dns\s*$", content):
        errors.append("Terraform deploy workflow must not expose public_dns as a normal stage option")
    if re.search(r"TARGET_STAGES=.*public_dns", content):
        errors.append("Terraform deploy workflow must not include public_dns in normal target stages")
    return errors


def public_dns_required_errors(content: str) -> list[str]:
    errors: list[str] = []
    dispatch_inputs = dispatch_input_names(content)
    required_inputs = {
        "operation",
        "environment",
        "azure_region",
        "customer_organization_slug",
    }
    for input_name in sorted(required_inputs - dispatch_inputs):
        errors.append(f"retained public DNS workflow missing {input_name} dispatch input")

    required_patterns = {
        "plan/apply operation": r"plan_apply",
        "delegation verification operation": r"verify_delegation",
        "retained stage constant": r"RETAINED_TERRAFORM_STAGE=.*public_dns|TERRAFORM_STAGE=.*public_dns",
        "retained workspace constant": r"RETAINED_WORKSPACE=.*pd_eastus2_internal|TF_WORKSPACE.*pd_eastus2_internal|pd_eastus2_internal.*TF_WORKSPACE",
        "pd-only environment gate": r"case\s+\"\$\{ENVIRONMENT\}\".*pd|test\s+\"\$\{ENVIRONMENT\}\"\s*=\s*\"pd\"",
        "eastus2-only region gate": r"case\s+\"\$\{AZURE_REGION\}\".*eastus2|test\s+\"\$\{AZURE_REGION\}\"\s*=\s*\"eastus2\"",
        "internal-only customer gate": r"case\s+\"\$\{CUSTOMER_ORGANIZATION_SLUG\}\".*internal|test\s+\"\$\{CUSTOMER_ORGANIZATION_SLUG\}\"\s*=\s*\"internal\"",
        "saved public DNS plan": r"terraform\s+-chdir=.*public_dns.*plan.*-out=.*PLAN_FILE|terraform\s+-chdir=.*STAGE_DIR.*plan.*-out=.*PLAN_FILE",
        "approved public DNS apply": r"terraform\s+-chdir=.*public_dns.*apply\s+-input=false\s+.*PLAN_FILE|terraform\s+-chdir=.*STAGE_DIR.*apply\s+-input=false\s+.*PLAN_FILE",
        "same-run public DNS artifact download": r"actions/download-artifact",
        "public DNS approval environment": r"(?m)^\s*name\s*:\s*terraform-public-dns-apply\s*$",
        "Cloudflare delegation output": r"cloudflare_delegation_ns_records",
        "public NS verification": r"dig\s+\+short\s+NS\s+tokenobs\.consultwithcloud\.com",
        "expected NS output": r"product_dns_zone_name_servers",
    }
    for name, pattern in required_patterns.items():
        if re.search(pattern, content, re.IGNORECASE | re.MULTILINE | re.DOTALL) is None:
            errors.append(f"missing {name}")

    forbidden_patterns = {
        "Cloudflare API credentials": r"CLOUDFLARE_API|CLOUDFLARE_TOKEN|cloudflare_api_token|cloudflare_zone_api_token",
        "Cloudflare Terraform provider": r"cloudflare/cloudflare|provider\s+\"cloudflare\"",
        "certificate private key material": r"PEM|PFX|ACME_ACCOUNT|PRIVATE_KEY|letsencrypt|lego",
        "public DNS destroy plan": r"terraform\s+-chdir=.*public_dns.*plan\s+-destroy|plan\s+-destroy.*public_dns",
    }
    for name, pattern in forbidden_patterns.items():
        if re.search(pattern, content, re.IGNORECASE | re.MULTILINE | re.DOTALL):
            errors.append(f"retained public DNS workflow must not contain {name}")

    if re.search(r"(?m)^\s*-\s*(dv|qa|pp)\s*$", content):
        errors.append("retained public DNS workflow must not expose non-pd environments")
    if re.search(r"(?m)^\s*-\s*(eastus|westeurope)\s*$", content):
        errors.append("retained public DNS workflow must not expose non-eastus2 regions")

    return errors


def validate_file(path: pathlib.Path) -> list[str]:
    content = path.read_text(encoding="utf-8")
    if not is_deployment_capable(content):
        return []

    errors: list[str] = []
    errors.extend(forbidden_trigger_errors(content))
    errors.extend(forbidden_command_errors(content))
    errors.extend(required_gate_errors(content))
    if is_terraform_plan_workflow(path, content):
        errors.extend(terraform_plan_required_errors(content))
    if is_public_dns_workflow(path, content):
        errors.extend(public_dns_required_errors(content))
    if is_destroy_capable(content):
        errors.extend(destroy_required_errors(content))
    if is_image_publish_capable(content):
        errors.extend(image_publish_required_errors(content))
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
