#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

usage() {
  cat <<'USAGE'
Usage:
  scripts/terraform-stage-check.sh STAGE [--plan] [--filter PATTERN] [--var name=value ...]

Runs bounded Terraform checks for infrastructure/azure/stages/STAGE.

Default:
  - terraform init -backend=false
  - terraform validate

With --plan:
  - terraform plan -out=tfplan
  - prints only the Plan line plus lines matching --filter when supplied
  - writes terraform-plan.json and prints resource_changes count

Environment:
  TERRAFORM_BIN overrides the Terraform executable.
USAGE
}

stage="${1:-}"
if [[ -z "$stage" || "$stage" == "-h" || "$stage" == "--help" ]]; then
  usage
  if [[ -z "$stage" ]]; then
    exit 2
  fi
  exit 0
fi
shift

run_plan=false
filter=""
var_args=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --plan)
      run_plan=true
      shift
      ;;
    --filter)
      filter="${2:-}"
      if [[ -z "$filter" ]]; then
        echo "--filter requires a pattern" >&2
        exit 2
      fi
      shift 2
      ;;
    --var)
      if [[ -z "${2:-}" ]]; then
        echo "--var requires name=value" >&2
        exit 2
      fi
      var_args+=("-var=$2")
      shift 2
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

terraform_bin="${TERRAFORM_BIN:-}"
if [[ -z "$terraform_bin" ]]; then
  if [[ -x "/Users/harisubramaniam/.terraform.versions/terraform_1.14.7" ]]; then
    terraform_bin="/Users/harisubramaniam/.terraform.versions/terraform_1.14.7"
  else
    terraform_bin="terraform"
  fi
fi

stage_dir="$ROOT_DIR/infrastructure/azure/stages/$stage"
if [[ ! -d "$stage_dir" ]]; then
  echo "Stage not found: $stage_dir" >&2
  exit 2
fi

echo "# Terraform Stage Check"
echo
printf -- "- Stage: \`%s\`\n" "$stage"
printf -- "- Terraform: \`%s\`\n" "$terraform_bin"
echo

"$terraform_bin" -chdir="$stage_dir" init -backend=false
"$terraform_bin" -chdir="$stage_dir" validate

if [[ "$run_plan" != true ]]; then
  exit 0
fi

plan_file="$stage_dir/tfplan"
plan_text="$stage_dir/terraform-plan.txt"
plan_json="$stage_dir/terraform-plan.json"

"$terraform_bin" -chdir="$stage_dir" plan -input=false -lock=false -out="$plan_file" "${var_args[@]}" > "$plan_text"

echo
echo "## Plan Summary"
echo
grep -E '^(Plan:|No changes\.)' "$plan_text" || true

if [[ -n "$filter" ]]; then
  echo
  echo "## Filtered Plan Evidence"
  echo
  grep -E "$filter" "$plan_text" || true
fi

"$terraform_bin" -chdir="$stage_dir" show -json "$plan_file" > "$plan_json"

echo
echo "## JSON Assertions"
echo
jq '{resource_changes: (.resource_changes // [] | length)}' "$plan_json"
