#!/usr/bin/env bash
set -euo pipefail

RETAINED_PUBLIC_DNS_WORKSPACE="pd_eastus2_internal"

assert_stage_output_workspace() {
  echo "Refusing to read ${terraform_stage} output from workspace ${terraform_workspace}; expected ${TF_WORKSPACE}."
  echo "Refusing to read ${terraform_stage} output from the default Terraform workspace."
}

init_stage() {
  :
}

terraform_output_json() {
  init_stage "${terraform_stage}" "${terraform_workspace}" false >&2
  echo "Required output ${terraform_stage}.${output_name} is missing or null in workspace ${terraform_workspace}."
}

terraform_output_raw() {
  init_stage "${terraform_stage}" "${terraform_workspace}" false >&2
  echo "Required output ${terraform_stage}.${output_name} is missing or empty in workspace ${terraform_workspace}."
}

append_stage_summary() {
  {
    printf -- '- Stage: `%s`\n' "${TERRAFORM_STAGE}"
    printf -- '- Workspace: `%s`\n' "${TF_WORKSPACE}"
    printf -- '- Commit SHA: `%s`\n' "${GITHUB_SHA}"
    printf -- '- ACR image publish run ID: `%s`\n' "${ACR_PUBLISH_RUN_ID}"
    printf -- '- Container Apps environment ID: `%s`\n' "${container_app_environment_id}"
    printf -- '- Result: `%s`\n' "${result}"
  } >> "${GITHUB_STEP_SUMMARY}"
}

# APP_RUNTIME_IMAGES_TFVARS_PATH
# app_runtime) -var-file=${APP_RUNTIME_IMAGES_TFVARS_PATH}
# terraform_output_raw foundation "${TF_WORKSPACE}" container_registry_id
# terraform_output_raw foundation "${TF_WORKSPACE}" container_registry_login_server
# artifact_container_registry_server="${container_registry_login_server}"
# terraform_output_json network_private_data_plane "${TF_WORKSPACE}" subnet_ids
# terraform_output_json observability_foundation "${TF_WORKSPACE}" diagnostic_destinations
# data_platform_configuration_contract
# ai_services_configuration_contract
# -var=network_subnet_ids=
# -var=diagnostic_destinations=
# -var=data_platform_configuration_contract=
# -var=ai_services_configuration_contract=
# -var=metrics_data_source_identifiers=
# terraform_output_json app_runtime "${TF_WORKSPACE}" container_app_fqdns
# terraform_output_raw app_runtime "${TF_WORKSPACE}" container_app_environment_id
# terraform_output_raw app_runtime "${TF_WORKSPACE}" container_app_environment_public_network_access
# terraform_output_json public_dns "${RETAINED_PUBLIC_DNS_WORKSPACE}" product_dns_zone
# -var=container_app_environment_id=
