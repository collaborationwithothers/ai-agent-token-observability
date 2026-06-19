#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Usage:
  scripts/terraform-stage-deploy.sh plan
  scripts/terraform-stage-deploy.sh apply

Required environment:
  TERRAFORM_STAGE
  TF_WORKSPACE
  ENVIRONMENT
  AZURE_REGION
  CUSTOMER_ORGANIZATION_SLUG
  PLAN_ARTIFACT_DIR
  TF_STATE_RESOURCE_GROUP_NAME
  TF_STATE_STORAGE_ACCOUNT_NAME
  TF_STATE_CONTAINER_NAME
USAGE
}

mode="${1:-}"
if [[ "${mode}" == "-h" || "${mode}" == "--help" || -z "${mode}" ]]; then
  usage
  if [[ -z "${mode}" ]]; then
    exit 2
  fi
  exit 0
fi

case "${mode}" in
  plan|apply) ;;
  *)
    echo "Unsupported mode: ${mode}" >&2
    usage >&2
    exit 2
    ;;
esac

required_env_vars=(
  TERRAFORM_STAGE
  TF_WORKSPACE
  ENVIRONMENT
  AZURE_REGION
  CUSTOMER_ORGANIZATION_SLUG
  PLAN_ARTIFACT_DIR
  TF_STATE_RESOURCE_GROUP_NAME
  TF_STATE_STORAGE_ACCOUNT_NAME
  TF_STATE_CONTAINER_NAME
)

for name in "${required_env_vars[@]}"; do
  if [[ -z "${!name:-}" ]]; then
    echo "Missing required environment variable: ${name}" >&2
    exit 1
  fi
done

case "${TERRAFORM_STAGE}" in
  foundation|network_private_data_plane|observability_foundation|data_platform|ai_services|app_runtime|managed_grafana|edge) ;;
  public_dns)
    echo "public_dns is retained shared DNS infrastructure and is not deployed by this workflow." >&2
    exit 1
    ;;
  *)
    echo "Unsupported Terraform stage: ${TERRAFORM_STAGE}" >&2
    exit 1
    ;;
esac

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STAGE_DIR="${ROOT_DIR}/infrastructure/azure/stages/${TERRAFORM_STAGE}"
PLAN_STAGE_DIR="${PLAN_ARTIFACT_DIR}/${TERRAFORM_STAGE}"
PLAN_FILE="${PLAN_STAGE_DIR}/${TERRAFORM_STAGE}-${TF_WORKSPACE}.tfplan"
PLAN_TEXT="${PLAN_STAGE_DIR}/${TERRAFORM_STAGE}-${TF_WORKSPACE}.txt"

init_stage() {
  local terraform_stage="$1"
  local terraform_workspace="$2"
  local create_workspace="${3:-false}"
  local stage_dir="${ROOT_DIR}/infrastructure/azure/stages/${terraform_stage}"

  test -d "${stage_dir}"
  cp "${stage_dir}/backend.azurerm.tf.example" "${stage_dir}/backend.generated.tf"

  terraform -chdir="${stage_dir}" init -input=false \
    -backend-config="resource_group_name=${TF_STATE_RESOURCE_GROUP_NAME}" \
    -backend-config="storage_account_name=${TF_STATE_STORAGE_ACCOUNT_NAME}" \
    -backend-config="container_name=${TF_STATE_CONTAINER_NAME}" \
    -backend-config="key=${terraform_stage}.tfstate" \
    -backend-config="use_azuread_auth=true"

  if [[ "${create_workspace}" == "true" ]]; then
    env -u TF_WORKSPACE terraform -chdir="${stage_dir}" workspace select "${terraform_workspace}" || env -u TF_WORKSPACE terraform -chdir="${stage_dir}" workspace new "${terraform_workspace}"
  else
    env -u TF_WORKSPACE terraform -chdir="${stage_dir}" workspace select "${terraform_workspace}"
  fi

  selected_workspace="$(terraform -chdir="${stage_dir}" workspace show)"
  test "${selected_workspace}" = "${terraform_workspace}"
  test "${selected_workspace}" != "default"
}

terraform_output_json() {
  local terraform_stage="$1"
  local terraform_workspace="$2"
  local output_name="$3"

  init_stage "${terraform_stage}" "${terraform_workspace}" false >&2
  terraform -chdir="${ROOT_DIR}/infrastructure/azure/stages/${terraform_stage}" output -json "${output_name}"
}

terraform_output_raw() {
  local terraform_stage="$1"
  local terraform_workspace="$2"
  local output_name="$3"

  init_stage "${terraform_stage}" "${terraform_workspace}" false >&2
  terraform -chdir="${ROOT_DIR}/infrastructure/azure/stages/${terraform_stage}" output -raw "${output_name}"
}

common_var_args() {
  printf '%s\n' \
    "-var=environment=${ENVIRONMENT}" \
    "-var=azure_region=${AZURE_REGION}" \
    "-var=customer_organization_slug=${CUSTOMER_ORGANIZATION_SLUG}" \
    "-var=terraform_workspace_name=${TF_WORKSPACE}" \
    "-var=resource_instance=core" \
    "-var=tags={environment=\"${ENVIRONMENT}\",region=\"${AZURE_REGION}\",product=\"token-observability\",owner=\"platform\",data_classification=\"internal\",managed_by=\"terraform\"}"
}

case "${mode}" in
  plan)
    mkdir -p "${PLAN_STAGE_DIR}"
    init_stage "${TERRAFORM_STAGE}" "${TF_WORKSPACE}" true
    terraform -chdir="${STAGE_DIR}" validate

    mapfile -t var_args < <(common_var_args)
    case "${TERRAFORM_STAGE}" in
      data_platform)
        foundation_deployment_identities="$(terraform_output_json foundation "${TF_WORKSPACE}" deployment_identities)"
        diagnostic_destinations="$(terraform_output_json observability_foundation "${TF_WORKSPACE}" diagnostic_destinations)"
        postgresql_ad_administrators="$(
          jq -c '
            with_entries(.value = {
              tenant_id: .value.tenant_id,
              object_id: .value.principal_id,
              principal_name: .value.name,
              principal_type: "ServicePrincipal"
            })
          ' <<<"${foundation_deployment_identities}"
        )"
        var_args+=("-var=postgresql_ad_administrators=${postgresql_ad_administrators}")
        var_args+=("-var=diagnostic_destinations=${diagnostic_destinations}")
        ;;
      edge)
        app_runtime_container_app_fqdns="$(terraform_output_json app_runtime "${TF_WORKSPACE}" container_app_fqdns)"
        public_dns_zone="$(terraform_output_json public_dns pd_eastus2_internal product_dns_zone)"
        var_args+=("-var=container_app_fqdns=${app_runtime_container_app_fqdns}")
        var_args+=("-var=azure_dns_zone=${public_dns_zone}")
        ;;
    esac

    init_stage "${TERRAFORM_STAGE}" "${TF_WORKSPACE}" true
    terraform -chdir="${STAGE_DIR}" plan -input=false -out="${PLAN_FILE}" "${var_args[@]}"
    terraform -chdir="${STAGE_DIR}" show -no-color "${PLAN_FILE}" > "${PLAN_TEXT}"
    ;;
  apply)
    test -f "${PLAN_FILE}"
    init_stage "${TERRAFORM_STAGE}" "${TF_WORKSPACE}" false
    terraform -chdir="${STAGE_DIR}" apply -input=false "${PLAN_FILE}"
    ;;
esac
