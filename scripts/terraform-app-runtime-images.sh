#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Usage:
  scripts/terraform-app-runtime-images.sh list --environment ENV --azure-region REGION [OPTIONS]
  scripts/terraform-app-runtime-images.sh dispatch --environment ENV --azure-region REGION --acr-publish-run-id RUN_ID [OPTIONS]

Options:
  --customer-organization-slug SLUG  Defaults to internal.
  --branch BRANCH                    Defaults to main.
  --limit COUNT                      Defaults to 20 for list.
  --terraform-stage STAGE            Defaults to app_runtime. Use all for full environment deploy.
  --repo OWNER/REPO                  Defaults to the current GitHub repository.

The helper lists successful ACR Image Publish runs that produced the expected
app runtime image digest artifact, then dispatches Terraform Deploy with the
selected run ID and derived commit SHA. Operators never type artifact names.
USAGE
}

command="${1:-}"
if [[ "${command}" == "-h" || "${command}" == "--help" || -z "${command}" ]]; then
  usage
  if [[ -z "${command}" ]]; then
    exit 2
  fi
  exit 0
fi
shift

environment=""
azure_region=""
customer_organization_slug="internal"
branch="main"
limit="20"
terraform_stage="app_runtime"
repo=""
acr_publish_run_id=""

while [[ "$#" -gt 0 ]]; do
  case "$1" in
    --environment)
      environment="${2:-}"
      shift 2
      ;;
    --azure-region)
      azure_region="${2:-}"
      shift 2
      ;;
    --customer-organization-slug)
      customer_organization_slug="${2:-}"
      shift 2
      ;;
    --branch)
      branch="${2:-}"
      shift 2
      ;;
    --limit)
      limit="${2:-}"
      shift 2
      ;;
    --terraform-stage)
      terraform_stage="${2:-}"
      shift 2
      ;;
    --repo)
      repo="${2:-}"
      shift 2
      ;;
    --acr-publish-run-id)
      acr_publish_run_id="${2:-}"
      shift 2
      ;;
    *)
      echo "Unsupported argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

case "${command}" in
  list|dispatch) ;;
  *)
    echo "Unsupported command: ${command}" >&2
    usage >&2
    exit 2
    ;;
esac

case "${environment}" in dv|qa|pp|pd) ;; *) echo "Unsupported environment: ${environment}" >&2; exit 1 ;; esac
case "${azure_region}" in eastus|eastus2|westeurope) ;; *) echo "Unsupported azure region: ${azure_region}" >&2; exit 1 ;; esac
case "${customer_organization_slug}" in *[!a-z0-9-]*|""|-*|*-) echo "Invalid customer organization slug: ${customer_organization_slug}" >&2; exit 1 ;; esac
case "${branch}" in main) ;; *) echo "Only main is deployable by the guarded Terraform workflow: ${branch}" >&2; exit 1 ;; esac
case "${limit}" in ''|*[!0-9]*) echo "Invalid limit: ${limit}" >&2; exit 1 ;; esac
case "${terraform_stage}" in all|foundation|network_private_data_plane|observability_foundation|data_platform|ai_services|app_runtime|managed_grafana|edge) ;; *) echo "Unsupported Terraform stage: ${terraform_stage}" >&2; exit 1 ;; esac

if [[ -z "${repo}" ]]; then
  repo="$(gh repo view --json nameWithOwner --jq .nameWithOwner)"
fi
test "${repo}" = "collaborationwithothers/ai-agent-token-observability"

workspace="${environment}_${azure_region}_${customer_organization_slug}"
expected_artifact_for_sha() {
  local sha="$1"
  printf 'app-runtime-image-digests-%s-%s\n' "${workspace}" "${sha}"
}

artifact_exists() {
  local run_id="$1"
  local sha="$2"
  local expected_artifact
  expected_artifact="$(expected_artifact_for_sha "${sha}")"

  gh api "repos/${repo}/actions/runs/${run_id}/artifacts" \
    --jq ".artifacts[] | select(.name == \"${expected_artifact}\" and .expired == false) | .name" \
    | grep -Fx "${expected_artifact}" >/dev/null
}

run_json() {
  local run_id="$1"
  gh run view "${run_id}" \
    --repo "${repo}" \
    --json databaseId,workflowName,headBranch,headSha,conclusion,status,event,url,createdAt
}

case "${command}" in
  list)
    runs_json="$(gh run list \
      --repo "${repo}" \
      --workflow "ACR Image Publish" \
      --branch "${branch}" \
      --status success \
      --limit "${limit}" \
      --json databaseId,headSha,createdAt,url)"

    printf 'workspace\t%s\n' "${workspace}"
    printf 'run_id\tcommit_sha\tcreated_at\tartifact\turl\n'
    jq -r '.[] | [.databaseId, .headSha, .createdAt, .url] | @tsv' <<<"${runs_json}" |
      while IFS=$'\t' read -r run_id sha created_at url; do
        expected_artifact="$(expected_artifact_for_sha "${sha}")"
        if artifact_exists "${run_id}" "${sha}"; then
          printf '%s\t%s\t%s\t%s\t%s\n' "${run_id}" "${sha}" "${created_at}" "${expected_artifact}" "${url}"
        fi
      done
    ;;
  dispatch)
    case "${acr_publish_run_id}" in ''|*[!0-9]*) echo "dispatch requires numeric --acr-publish-run-id." >&2; exit 1 ;; esac

    selected_run_json="$(run_json "${acr_publish_run_id}")"
    test "$(jq -r '.workflowName' <<<"${selected_run_json}")" = "ACR Image Publish"
    test "$(jq -r '.headBranch' <<<"${selected_run_json}")" = "${branch}"
    test "$(jq -r '.conclusion' <<<"${selected_run_json}")" = "success"
    test "$(jq -r '.status' <<<"${selected_run_json}")" = "completed"
    test "$(jq -r '.event' <<<"${selected_run_json}")" = "workflow_dispatch"

    acr_publish_commit_sha="$(jq -r '.headSha' <<<"${selected_run_json}")"
    artifact_exists "${acr_publish_run_id}" "${acr_publish_commit_sha}"

    workflow_args=(
      terraform-plan.yml
      --repo "${repo}"
      --ref "${branch}"
      -f "environment=${environment}"
      -f "azure_region=${azure_region}"
      -f "customer_organization_slug=${customer_organization_slug}"
      -f "acr_publish_run_id=${acr_publish_run_id}"
      -f "acr_publish_commit_sha=${acr_publish_commit_sha}"
    )
    if [[ "${terraform_stage}" != "all" ]]; then
      workflow_args+=(-f "terraform_stage=${terraform_stage}")
    fi

    gh workflow run "${workflow_args[@]}"

    printf 'Dispatched Terraform Deploy for workspace %s with ACR publish run %s at commit %s.\n' \
      "${workspace}" \
      "${acr_publish_run_id}" \
      "${acr_publish_commit_sha}"
    ;;
esac
