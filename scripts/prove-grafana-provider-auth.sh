#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROOF_DIR="$ROOT_DIR/infrastructure/azure/proofs/grafana_provider_auth"
GRAFANA_ENTRA_AUDIENCE="6f2d169c-08f3-4a4c-a982-bcaf2d038c45"

usage() {
  cat <<'USAGE'
Usage:
  scripts/prove-grafana-provider-auth.sh --mode entra --endpoint GRAFANA_ENDPOINT
  scripts/prove-grafana-provider-auth.sh --mode service-account --endpoint GRAFANA_ENDPOINT

Modes:
  entra            Use a Microsoft Entra access token for the Azure Managed Grafana data plane.
  service-account  Use GRAFANA_SERVICE_ACCOUNT_TOKEN as an explicit fallback.

Environment:
  TERRAFORM_BIN                 Optional Terraform executable override.
  GRAFANA_ENTRA_ACCESS_TOKEN    Optional pre-acquired Azure Managed Grafana audience token.
  GRAFANA_SERVICE_ACCOUNT_TOKEN Required only for service-account mode.
USAGE
}

mode=""
endpoint=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --mode)
      mode="${2:-}"
      if [[ -z "$mode" ]]; then
        echo "--mode requires entra or service-account." >&2
        exit 2
      fi
      shift 2
      ;;
    --endpoint)
      endpoint="${2:-}"
      if [[ -z "$endpoint" ]]; then
        echo "--endpoint requires an Azure Managed Grafana endpoint URL." >&2
        exit 2
      fi
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

if [[ -z "$mode" || -z "$endpoint" ]]; then
  usage >&2
  exit 2
fi

case "$mode" in
  entra|service-account) ;;
  *)
    echo "--mode must be entra or service-account." >&2
    exit 2
    ;;
esac

if [[ ! "$endpoint" =~ ^https://[^[:space:]]+$ ]]; then
  echo "--endpoint must be an HTTPS URL without whitespace." >&2
  exit 2
fi

terraform_bin="${TERRAFORM_BIN:-}"
if [[ -z "$terraform_bin" ]]; then
  if [[ -x "/Users/harisubramaniam/.terraform.versions/terraform_1.14.7" ]]; then
    terraform_bin="/Users/harisubramaniam/.terraform.versions/terraform_1.14.7"
  else
    terraform_bin="terraform"
  fi
fi

if ! command -v "$terraform_bin" >/dev/null 2>&1 && [[ ! -x "$terraform_bin" ]]; then
  echo "Terraform executable not found. Set TERRAFORM_BIN to a Terraform 1.14 executable." >&2
  exit 2
fi

proof_output="$(mktemp)"
cleanup() {
  rm -f "$proof_output"
}
trap cleanup EXIT

clear_terraform_logging_env() {
  local name

  while IFS='=' read -r name _; do
    case "$name" in
      TF_LOG|TF_LOG_*|TF_ACC_LOG_PATH)
        unset "$name"
        ;;
    esac
  done < <(env)
}

run_probe() {
  local selected_mode="$1"

  "$terraform_bin" -chdir="$PROOF_DIR" init -backend=false -input=false -no-color >"$proof_output" 2>&1
  if ! "$terraform_bin" -chdir="$PROOF_DIR" plan -input=false -lock=false -no-color >"$proof_output" 2>&1; then
    printf 'Grafana provider authentication proof failed for mode `%s`.\n' "$selected_mode" >&2
    echo "Terraform output was suppressed to avoid leaking credentials or API response details." >&2
    exit 1
  fi

  printf 'Grafana provider authentication proof succeeded for mode `%s`.\n' "$selected_mode"
  echo "No Grafana response bodies, bearer tokens, service account tokens, or Terraform outputs were printed."
}

case "$mode" in
  entra)
    token="${GRAFANA_ENTRA_ACCESS_TOKEN:-}"
    if [[ -z "$token" ]]; then
      if ! command -v az >/dev/null 2>&1; then
        echo "Azure CLI is required for entra mode unless GRAFANA_ENTRA_ACCESS_TOKEN is set." >&2
        exit 2
      fi
      token="$(az account get-access-token \
        --resource "$GRAFANA_ENTRA_AUDIENCE" \
        --query accessToken \
        --output tsv \
        --only-show-errors)"
    fi

    if [[ -z "$token" ]]; then
      echo "Failed to acquire an Azure Managed Grafana Microsoft Entra token." >&2
      exit 1
    fi

    (
      set +x
      export GRAFANA_URL="$endpoint"
      export GRAFANA_AUTH="$token"
      unset GRAFANA_HTTP_HEADERS
      clear_terraform_logging_env
      run_probe "entra"
    )
    ;;
  service-account)
    token="${GRAFANA_SERVICE_ACCOUNT_TOKEN:-}"
    if [[ -z "$token" ]]; then
      echo "GRAFANA_SERVICE_ACCOUNT_TOKEN is required for service-account mode." >&2
      exit 2
    fi

    (
      set +x
      export GRAFANA_URL="$endpoint"
      export GRAFANA_AUTH="$token"
      unset GRAFANA_HTTP_HEADERS
      clear_terraform_logging_env
      run_probe "service-account"
    )
    ;;
esac
