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

Optional environment:
  APP_RUNTIME_IMAGES_TFVARS_PATH
  GRAFANA_ADMIN_GROUP_OBJECT_ID
  GRAFANA_VIEWER_GROUP_OBJECT_ID
  GRAFANA_EDITOR_GROUP_OBJECT_ID
  ALLOW_PRODUCTION_GRAFANA_EDITORS
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
RETAINED_PUBLIC_DNS_WORKSPACE="pd_eastus2_internal"
GRAFANA_ENTRA_AUDIENCE="6f2d169c-08f3-4a4c-a982-bcaf2d038c45"

assert_stage_output_workspace() {
  local terraform_stage="$1"
  local terraform_workspace="$2"

  if [[ "${terraform_stage}" == "public_dns" && "${terraform_workspace}" == "${RETAINED_PUBLIC_DNS_WORKSPACE}" ]]; then
    return 0
  fi

  if [[ "${terraform_workspace}" != "${TF_WORKSPACE}" ]]; then
    echo "Refusing to read ${terraform_stage} output from workspace ${terraform_workspace}; expected ${TF_WORKSPACE}." >&2
    exit 1
  fi

  if [[ "${terraform_workspace}" == "default" ]]; then
    echo "Refusing to read ${terraform_stage} output from the default Terraform workspace." >&2
    exit 1
  fi
}

init_stage() {
  local terraform_stage="$1"
  local terraform_workspace="$2"
  local create_workspace="${3:-false}"
  local stage_dir="${ROOT_DIR}/infrastructure/azure/stages/${terraform_stage}"

  test -d "${stage_dir}"
  cp "${stage_dir}/backend.azurerm.tf.example" "${stage_dir}/backend.generated.tf"

  TF_WORKSPACE=default terraform -chdir="${stage_dir}" init -input=false \
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
  local output_value

  assert_stage_output_workspace "${terraform_stage}" "${terraform_workspace}"
  init_stage "${terraform_stage}" "${terraform_workspace}" false >&2
  output_value="$(terraform -chdir="${ROOT_DIR}/infrastructure/azure/stages/${terraform_stage}" output -json "${output_name}")"
  if [[ -z "${output_value}" ]] || ! jq -e 'type != "null"' <<<"${output_value}" >/dev/null; then
    echo "Required output ${terraform_stage}.${output_name} is missing or null in workspace ${terraform_workspace}." >&2
    exit 1
  fi
  printf '%s\n' "${output_value}"
}

terraform_output_raw() {
  local terraform_stage="$1"
  local terraform_workspace="$2"
  local output_name="$3"
  local output_value

  assert_stage_output_workspace "${terraform_stage}" "${terraform_workspace}"
  init_stage "${terraform_stage}" "${terraform_workspace}" false >&2
  output_value="$(terraform -chdir="${ROOT_DIR}/infrastructure/azure/stages/${terraform_stage}" output -raw "${output_name}")"
  if [[ -z "${output_value}" ]]; then
    echo "Required output ${terraform_stage}.${output_name} is missing or empty in workspace ${terraform_workspace}." >&2
    exit 1
  fi
  printf '%s\n' "${output_value}"
}

require_uuid_env() {
  local name="$1"
  local value

  value="$(printenv "${name}" || true)"
  if [[ -z "${value}" ]]; then
    echo "Missing required environment variable for managed_grafana: ${name}" >&2
    exit 1
  fi

  if [[ ! "${value}" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]]; then
    echo "${name} must be a Microsoft Entra group object ID UUID." >&2
    exit 1
  fi

  printf '%s\n' "${value}"
}

optional_uuid_env() {
  local name="$1"
  local value

  value="$(printenv "${name}" || true)"
  if [[ -z "${value}" ]]; then
    return 0
  fi

  if [[ ! "${value}" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]]; then
    echo "${name} must be a Microsoft Entra group object ID UUID when supplied." >&2
    exit 1
  fi

  printf '%s\n' "${value}"
}

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

managed_grafana_workspace_name() {
  local azure_region_code

  case "${AZURE_REGION}" in
    eastus) azure_region_code="eus" ;;
    eastus2) azure_region_code="eus2" ;;
    westeurope) azure_region_code="weu" ;;
    *) azure_region_code="$(printf '%s' "${AZURE_REGION}" | tr -d '-' | cut -c1-6)" ;;
  esac

  printf 'amg-%s-%s-%s\n' "${ENVIRONMENT}" "${azure_region_code}" "${CUSTOMER_ORGANIZATION_SLUG}"
}

normalize_grafana_endpoint() {
  local endpoint="$1"

  endpoint="${endpoint%/}"
  if [[ -n "${endpoint}" && "${endpoint}" != https://* ]]; then
    endpoint="https://${endpoint}"
  fi

  printf '%s\n' "${endpoint}"
}

managed_grafana_endpoint_from_state() {
  terraform -chdir="${STAGE_DIR}" output -raw grafana_endpoint 2>/dev/null || true
}

managed_grafana_endpoint_from_azure() {
  local observability_resource_group_name="$1"
  local grafana_workspace_name

  if ! command -v az >/dev/null 2>&1; then
    return 0
  fi

  grafana_workspace_name="$(managed_grafana_workspace_name)"
  az grafana show \
    --name "${grafana_workspace_name}" \
    --resource-group "${observability_resource_group_name}" \
    --query "endpoint || properties.endpoint" \
    --output tsv \
    --only-show-errors 2>/dev/null || true
}

prepare_managed_grafana_provider_env() {
  local observability_resource_group_name="${1:-}"
  local endpoint
  local token

  if [[ "${TERRAFORM_STAGE}" != "managed_grafana" ]]; then
    return 0
  fi

  clear_terraform_logging_env

  endpoint="${GRAFANA_URL:-}"
  if [[ -z "${endpoint}" ]]; then
    endpoint="$(managed_grafana_endpoint_from_state)"
  fi
  if [[ -z "${endpoint}" ]]; then
    if [[ -z "${observability_resource_group_name}" ]]; then
      observability_resource_group_name="$(terraform_output_raw observability_foundation "${TF_WORKSPACE}" observability_resource_group_name)"
    fi
    endpoint="$(managed_grafana_endpoint_from_azure "${observability_resource_group_name}")"
  fi

  endpoint="$(normalize_grafana_endpoint "${endpoint}")"
  if [[ -z "${endpoint}" ]]; then
    echo "Azure Managed Grafana endpoint is not available yet; Grafana provider environment was not configured." >&2
    echo "If this is the first managed_grafana apply for the workspace, rerun the managed_grafana stage after the Azure workspace exists." >&2
    return 0
  fi
  if [[ ! "${endpoint}" =~ ^https://[^[:space:]]+$ ]]; then
    echo "Azure Managed Grafana endpoint must be an HTTPS URL without whitespace." >&2
    exit 1
  fi

  token="${GRAFANA_ENTRA_ACCESS_TOKEN:-}"
  if [[ -z "${token}" ]]; then
    if ! command -v az >/dev/null 2>&1; then
      echo "Azure CLI is required to acquire the Azure Managed Grafana provider token unless GRAFANA_ENTRA_ACCESS_TOKEN is set." >&2
      exit 1
    fi
    token="$(az account get-access-token \
      --resource "${GRAFANA_ENTRA_AUDIENCE}" \
      --query accessToken \
      --output tsv \
      --only-show-errors)"
  fi
  if [[ -z "${token}" ]]; then
    echo "Failed to acquire an Azure Managed Grafana Microsoft Entra token." >&2
    exit 1
  fi

  export GRAFANA_URL="${endpoint}"
  export GRAFANA_AUTH="${token}"
  unset GRAFANA_HTTP_HEADERS
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

append_stage_summary() {
  local action="$1"
  local result="$2"

  if [[ -z "${GITHUB_STEP_SUMMARY:-}" ]]; then
    return 0
  fi

  {
    printf '## Terraform %s Result\n\n' "${action}"
    printf -- '- Stage: `%s`\n' "${TERRAFORM_STAGE}"
    printf -- '- Workspace: `%s`\n' "${TF_WORKSPACE}"
    printf -- '- Commit SHA: `%s`\n' "${GITHUB_SHA:-unknown}"
    if [[ -n "${ACR_PUBLISH_RUN_ID:-}" && "${TERRAFORM_STAGE}" == "app_runtime" ]]; then
      printf -- '- ACR image publish run ID: `%s`\n' "${ACR_PUBLISH_RUN_ID}"
    fi
    printf -- '- Result: `%s`\n' "${result}"
  } >> "${GITHUB_STEP_SUMMARY}"
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
      ai_services)
        diagnostic_destinations="$(terraform_output_json observability_foundation "${TF_WORKSPACE}" diagnostic_destinations)"
        var_args+=("-var=diagnostic_destinations=${diagnostic_destinations}")
        ;;
      app_runtime)
        if [[ -z "${APP_RUNTIME_IMAGES_TFVARS_PATH:-}" ]]; then
          echo "Missing APP_RUNTIME_IMAGES_TFVARS_PATH for app_runtime plan. Select a successful ACR Image Publish run and pass its app-runtime-images.auto.tfvars.json artifact." >&2
          exit 1
        fi
        if [[ ! -s "${APP_RUNTIME_IMAGES_TFVARS_PATH}" ]]; then
          echo "APP_RUNTIME_IMAGES_TFVARS_PATH does not reference a readable non-empty file: ${APP_RUNTIME_IMAGES_TFVARS_PATH}" >&2
          exit 1
        fi
        container_registry_id="$(terraform_output_raw foundation "${TF_WORKSPACE}" container_registry_id)"
        container_registry_login_server="$(terraform_output_raw foundation "${TF_WORKSPACE}" container_registry_login_server)"
        artifact_container_registry_server="$(jq -er '.container_registry_server' "${APP_RUNTIME_IMAGES_TFVARS_PATH}")"
        if [[ "${artifact_container_registry_server}" != "${container_registry_login_server}" ]]; then
          echo "App runtime image artifact registry ${artifact_container_registry_server} does not match foundation output ${container_registry_login_server}." >&2
          exit 1
        fi
        network_subnet_ids="$(terraform_output_json network_private_data_plane "${TF_WORKSPACE}" subnet_ids)"
        diagnostic_destinations="$(terraform_output_json observability_foundation "${TF_WORKSPACE}" diagnostic_destinations)"
        data_platform_configuration_contract="$(
          jq -nc \
            --arg postgresql_server_fqdn "$(terraform_output_raw data_platform "${TF_WORKSPACE}" postgresql_server_fqdn)" \
            --arg storage_account_name "$(terraform_output_raw data_platform "${TF_WORKSPACE}" storage_account_name)" \
            --argjson postgresql_database_names "$(terraform_output_json data_platform "${TF_WORKSPACE}" postgresql_database_names)" \
            --argjson storage_container_names "$(terraform_output_json data_platform "${TF_WORKSPACE}" storage_container_names)" \
            --argjson captured_content_storage_contract "$(terraform_output_json data_platform "${TF_WORKSPACE}" captured_content_storage_contract)" \
            --argjson operational_storage_contract "$(terraform_output_json data_platform "${TF_WORKSPACE}" operational_storage_contract)" \
            --argjson storage_lifecycle_contract "$(terraform_output_json data_platform "${TF_WORKSPACE}" storage_lifecycle_contract)" \
            '{
              postgresql_server_fqdn: $postgresql_server_fqdn,
              postgresql_database_names: $postgresql_database_names,
              storage_account_name: $storage_account_name,
              storage_container_names: $storage_container_names,
              captured_content_storage_contract: $captured_content_storage_contract,
              operational_storage_contract: $operational_storage_contract,
              storage_lifecycle_contract: $storage_lifecycle_contract
            }'
        )"
        ai_services_configuration_contract="$(
          terraform_output_json ai_services "${TF_WORKSPACE}" ai_services_configuration_contract \
            | jq -c 'del(.private_endpoint_ids)'
        )"
        recommendation_model_deployment_contracts="$(terraform_output_json ai_services "${TF_WORKSPACE}" recommendation_model_deployment_contracts)"
        language_pii_detection_contract="$(terraform_output_json ai_services "${TF_WORKSPACE}" language_pii_detection_contract)"
        content_safety_contract="$(terraform_output_json ai_services "${TF_WORKSPACE}" content_safety_contract)"
        if ! jq -e '
          .container_apps.consumer_stage == "app_runtime" and
          .container_app_jobs.consumer_stage == "app_runtime" and
          (.container_apps.log_analytics_workspace_resource_id | type == "string" and length > 0) and
          (.container_app_jobs.log_analytics_workspace_resource_id | type == "string" and length > 0)
        ' <<<"${diagnostic_destinations}" >/dev/null; then
          echo "observability_foundation.diagnostic_destinations must include container_apps and container_app_jobs contracts for app_runtime." >&2
          exit 1
        fi
        if ! jq -e '
          (.container_apps_infrastructure | type == "string" and length > 0)
        ' <<<"${network_subnet_ids}" >/dev/null; then
          echo "network_private_data_plane.subnet_ids must include container_apps_infrastructure for app_runtime." >&2
          exit 1
        fi
        if ! jq -e '
          (.postgresql_server_fqdn | type == "string" and length > 0) and
          (.postgresql_database_names.product_metadata | type == "string" and length > 0) and
          (.storage_account_name | type == "string" and length > 0) and
          (.captured_content_storage_contract.redaction_required_before_storage == true)
        ' <<<"${data_platform_configuration_contract}" >/dev/null; then
          echo "data_platform outputs must include non-secret runtime configuration contracts for app_runtime." >&2
          exit 1
        fi
        if ! jq -e '
          (.endpoint | type == "string" and length > 0) and
          (.recommendation_model_deployment_aliases | type == "array")
        ' <<<"${ai_services_configuration_contract}" >/dev/null; then
          echo "ai_services output must include a non-secret AI services configuration contract for app_runtime." >&2
          exit 1
        fi
        var_args+=("-var=container_registry_id=${container_registry_id}")
        var_args+=("-var-file=${APP_RUNTIME_IMAGES_TFVARS_PATH}")
        var_args+=("-var=container_registry_server=${container_registry_login_server}")
        var_args+=("-var=network_subnet_ids=${network_subnet_ids}")
        var_args+=("-var=diagnostic_destinations=${diagnostic_destinations}")
        var_args+=("-var=data_platform_configuration_contract=${data_platform_configuration_contract}")
        var_args+=("-var=ai_services_configuration_contract=${ai_services_configuration_contract}")
        var_args+=("-var=recommendation_model_deployment_contracts=${recommendation_model_deployment_contracts}")
        var_args+=("-var=language_pii_detection_contract=${language_pii_detection_contract}")
        var_args+=("-var=content_safety_contract=${content_safety_contract}")
        ;;
      managed_grafana)
        grafana_admin_group_object_id="$(require_uuid_env GRAFANA_ADMIN_GROUP_OBJECT_ID)"
        grafana_viewer_group_object_id="$(require_uuid_env GRAFANA_VIEWER_GROUP_OBJECT_ID)"
        grafana_editor_group_object_id="$(optional_uuid_env GRAFANA_EDITOR_GROUP_OBJECT_ID)"
        allow_production_grafana_editors="${ALLOW_PRODUCTION_GRAFANA_EDITORS:-false}"
        case "${allow_production_grafana_editors}" in
          true|false) ;;
          *)
            echo "ALLOW_PRODUCTION_GRAFANA_EDITORS must be true or false when supplied." >&2
            exit 1
            ;;
        esac
        observability_resource_group_name="$(terraform_output_raw observability_foundation "${TF_WORKSPACE}" observability_resource_group_name)"
        observability_resource_group_ids="$(terraform_output_json observability_foundation "${TF_WORKSPACE}" resource_group_ids)"
        metrics_data_source_identifiers="$(terraform_output_json observability_foundation "${TF_WORKSPACE}" metrics_data_source_identifiers)"
        observability_resource_group_id="$(
          jq -er '.observability' <<<"${observability_resource_group_ids}"
        )"
        if ! jq -e '
          .aggregate_metrics.type == "azure_monitor_workspace" and
          .aggregate_metrics.boundary == "aggregate_metrics_only" and
          (.aggregate_metrics.resource_id | type == "string" and length > 0) and
          (.aggregate_metrics.consumer_stages | index("managed_grafana") != null)
        ' <<<"${metrics_data_source_identifiers}" >/dev/null; then
          echo "observability_foundation.metrics_data_source_identifiers must expose aggregate_metrics as an aggregate-only Azure Monitor workspace for managed_grafana." >&2
          exit 1
        fi
        var_args+=("-var=observability_resource_group_name=${observability_resource_group_name}")
        var_args+=("-var=observability_resource_group_id=${observability_resource_group_id}")
        var_args+=("-var=metrics_data_source_identifiers=${metrics_data_source_identifiers}")
        var_args+=("-var=grafana_admin_group_object_id=${grafana_admin_group_object_id}")
        var_args+=("-var=grafana_viewer_group_object_id=${grafana_viewer_group_object_id}")
        var_args+=("-var=allow_production_grafana_editors=${allow_production_grafana_editors}")
        if [[ -n "${grafana_editor_group_object_id}" ]]; then
          var_args+=("-var=grafana_editor_group_object_id=${grafana_editor_group_object_id}")
        fi
        prepare_managed_grafana_provider_env "${observability_resource_group_name}"
        ;;
      edge)
        app_runtime_container_app_fqdns="$(terraform_output_json app_runtime "${TF_WORKSPACE}" container_app_fqdns)"
        app_runtime_container_app_environment_id="$(terraform_output_raw app_runtime "${TF_WORKSPACE}" container_app_environment_id)"
        app_runtime_container_app_environment_public_network_access="$(terraform_output_raw app_runtime "${TF_WORKSPACE}" container_app_environment_public_network_access)"
        diagnostic_destinations="$(terraform_output_json observability_foundation "${TF_WORKSPACE}" diagnostic_destinations)"
        public_dns_zone="$(terraform_output_json public_dns "${RETAINED_PUBLIC_DNS_WORKSPACE}" product_dns_zone)"
        edge_log_analytics_workspace_id="$(jq -er '.front_door.log_analytics_workspace_resource_id' <<<"${diagnostic_destinations}")"
        if ! jq -e '
          has("product_dashboard") and
          has("product_api") and
          has("product_ingestion_endpoint") and
          all(.[]; type == "string" and length > 0)
        ' <<<"${app_runtime_container_app_fqdns}" >/dev/null; then
          echo "app_runtime.container_app_fqdns must include product_dashboard, product_api, and product_ingestion_endpoint for edge." >&2
          exit 1
        fi
        if ! jq -e '
          .front_door.consumer_stage == "edge" and
          (.front_door.log_analytics_workspace_resource_id | type == "string" and length > 0)
        ' <<<"${diagnostic_destinations}" >/dev/null; then
          echo "observability_foundation.diagnostic_destinations must include front_door for edge." >&2
          exit 1
        fi
        if ! jq -e '
          .manage_records == true and
          .name == "tokenobs.consultwithcloud.com" and
          (.resource_group_name | type == "string" and length > 0)
        ' <<<"${public_dns_zone}" >/dev/null; then
          echo "public_dns.product_dns_zone must be the retained delegated tokenobs.consultwithcloud.com zone for edge." >&2
          exit 1
        fi
        var_args+=("-var=container_app_fqdns=${app_runtime_container_app_fqdns}")
        var_args+=("-var=container_app_environment_id=${app_runtime_container_app_environment_id}")
        var_args+=("-var=container_app_environment_public_network_access=${app_runtime_container_app_environment_public_network_access}")
        var_args+=("-var=diagnostic_destinations=${diagnostic_destinations}")
        var_args+=("-var=log_analytics_workspace_id=${edge_log_analytics_workspace_id}")
        var_args+=("-var=azure_dns_zone=${public_dns_zone}")
        ;;
    esac

    init_stage "${TERRAFORM_STAGE}" "${TF_WORKSPACE}" true
    terraform -chdir="${STAGE_DIR}" plan -input=false -out="${PLAN_FILE}" "${var_args[@]}"
    terraform -chdir="${STAGE_DIR}" show -no-color "${PLAN_FILE}" > "${PLAN_TEXT}"
    append_stage_summary "plan" "planned"
    ;;
  apply)
    test -f "${PLAN_FILE}"
    init_stage "${TERRAFORM_STAGE}" "${TF_WORKSPACE}" false
    prepare_managed_grafana_provider_env
    terraform -chdir="${STAGE_DIR}" apply -input=false "${PLAN_FILE}"
    append_stage_summary "apply" "applied"
    ;;
esac
