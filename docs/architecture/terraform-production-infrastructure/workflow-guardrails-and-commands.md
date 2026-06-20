## Public Repository Workflow Guardrails

Deployment-capable GitHub Actions must be designed for a public repository.

Infrastructure deletion is defined separately in [Infrastructure Deletion Workflow](../../operations/infrastructure-deletion.md). Deletion must use guarded Terraform destroy plans by stage and workspace, not tag-based Azure deletion.

The normal Terraform deploy workflow produces a saved plan artifact for each selected stage, waits on the `terraform-apply` GitHub environment, downloads the approved same-run artifact, verifies stage and workspace, and applies only that saved plan file. In an all-stage deploy, the workflow plans, reviews, and applies one stage before moving to the next dependent stage, so downstream stages can read remote state outputs produced earlier in the same run.

Runtime image publishing is also separate from Terraform plan. The image publish workflow builds the Product Dashboard, Product API, Product Ingestion Endpoint, and shared Product Jobs images, publishes them to Azure Container Registry with immutable commit SHA tags, and emits digest-pinned app runtime Terraform inputs. App runtime deploys consume a validated ACR Image Publish digest artifact before planning. By default, the deploy workflow selects the latest successful matching publish run for the derived workspace. Operators can optionally supply a specific ACR publish run ID as an override.

Allowed trigger:

```yaml
on:
  workflow_dispatch:
```

Forbidden triggers for Azure-changing workflows:

- `push`
- `pull_request`
- `pull_request_target`
- `workflow_run`
- `schedule`

Required normal deploy workflow inputs:

- `environment`
- `azure_region`
- optional `customer_organization_slug`, defaulting to `internal`
- optional `terraform_stage`
- optional `acr_publish_run_id` when `app_runtime` is included, used only as an advanced override

Required validations before Azure login:

- `github.repository` equals the expected repository.
- `github.event_name` equals `workflow_dispatch`.
- `github.actor` is in the allowed deployment actor list or belongs to the required protected environment reviewer path.
- `github.ref` is allowed for the requested environment.
- `environment` is one of `dv`, `qa`, `pp`, or `pd`.
- `azure_region` is in the allowed region list.
- `customer_organization_slug` is in the allowed deployment scope list.
- If supplied, `terraform_stage` is in the allow-list of normal deploy stage directories.
- `public_dns` is not accepted by the normal deploy workflow because it is retained shared DNS infrastructure owned from `pd_eastus2_internal`.
- Derived workspace equals `{environment}_{azureRegion}_{customerOrganizationSlug}`.
- The workflow publishes the derived workspace as an internal job output for downstream plan and apply jobs.
- The workflow does not require a manual confirmation phrase because the workspace is derived from validated inputs and apply jobs still require protected environment approval.
- If `app_runtime` is selected, the workflow validates ACR Image Publish evidence before Azure login. With no override, it selects the latest successful `ACR Image Publish` run on `main` that contains the non-expired derived `app-runtime-image-digests-{workspace}-{sha}` artifact. With an override, it validates the supplied run ID. In both cases, the run must belong to this repository, use the `ACR Image Publish` workflow, run on `main`, complete successfully from `workflow_dispatch`, derive the commit SHA from the selected run, and contain the derived workspace artifact.

The retained public DNS workflow is not a normal deployment workflow. Its controls are a fixed `public_dns` stage, the single owner workspace `pd_eastus2_internal`, repository, actor, branch, `pd`, `eastus2`, and `internal` gates, same-run saved plan artifact apply, the protected `terraform-public-dns-apply` GitHub environment, Cloudflare delegation output only, and public NS verification before edge deployment depends on the delegated zone.

Required GitHub permissions:

```yaml
permissions:
  contents: read
  id-token: write
```

Rules:

- `id-token: write` is allowed only for jobs that need Azure OIDC, including ACR publish jobs that authenticate to Azure before `az acr login`.
- `GITHUB_TOKEN` permissions must remain least privilege.
- Workflow scripts must not interpolate untrusted context directly into shell commands.
- Forked PRs must never receive Azure credentials or deployment-capable tokens.
- Apply jobs must use the `terraform-apply` GitHub environment with required reviewers.
- Managed Azure VNet runners may be used for private resource validation and Terraform operations, but they do not replace public repository workflow guards or Front Door origin isolation.
- Plan and apply must be separate jobs for each selected stage.
- Apply must use the exact plan artifact from the guarded plan job for the same stage and same run.
- `terraform apply -auto-approve` is forbidden in production workflows.
- When no `terraform_stage` is supplied, normal deploy targets environment-scoped stages one by one in dependency order: `foundation`, `network_private_data_plane`, `observability_foundation`, `data_platform`, `ai_services`, `app_runtime`, `managed_grafana`, and `edge`. Each stage's apply must complete before the next dependent stage is planned.
- `public_dns` must be planned and applied separately through `.github/workflows/terraform-public-dns.yml` as retained shared infrastructure before `edge` depends on its delegated Azure DNS zone output.
- The retained public DNS workflow must use the single owner workspace `pd_eastus2_internal`, require the protected `terraform-public-dns-apply` environment before applying the saved plan artifact, emit the manual Cloudflare NS records from `cloudflare_delegation_ns_records`, and provide a `verify_delegation` operation that compares public NS records with `product_dns_zone_name_servers`.
- `app_runtime` plans must use the digest-pinned `app-runtime-images.auto.tfvars.json` artifact produced by the selected ACR Image Publish run. Do not pass artifact names, mutable `latest` tags, or `example.azurecr.io` placeholder images into Terraform deploy.
- The deploy helper must read required upstream outputs centrally and fail closed before planning if an output is missing, null, empty, malformed, read from the `default` workspace, or read from a workspace other than the selected environment workspace.
- The only normal deploy cross-workspace exception is `edge` reading retained `public_dns.product_dns_zone` from `pd_eastus2_internal`.
- `app_runtime` consumes same-workspace non-secret contracts from `foundation`, `network_private_data_plane`, `observability_foundation`, `data_platform`, and `ai_services`, plus the selected digest-pinned image artifact.
- `edge` consumes same-workspace `app_runtime` origin evidence, same-workspace observability diagnostics, and the retained public DNS zone. It must not implement deferred Container Apps direct-origin blocking or Front Door Private Link origin isolation in this workflow contract.
- Normal deploy workflow summaries are sanitized and may include only the stage, workspace, commit SHA, selected ACR publish run ID when applicable, and result.

Guardrail validator:

- A committed validator must inspect `.github/workflows/*.yml`.
- The validator must fail if an Azure-changing workflow has forbidden triggers.
- The validator must fail if a normal Azure-changing workflow lacks repository, actor, environment, region, derived workspace, branch, OIDC, permissions, and protected environment checks.
- The validator must fail if the normal deploy or related operational workflows reintroduce manual workspace or confirmation dispatch inputs.
- The validator must fail if the retained public DNS workflow lacks its documented retained-stage controls.
- The validator must fail if `terraform apply -auto-approve` appears in deployment-capable workflows.
- The validator must fail if the normal Terraform deploy workflow can apply without the `terraform-apply` environment, without downloading the saved plan artifact, or with `public_dns` in normal target stages.
- The validator must fail if the normal Terraform deploy workflow can plan `app_runtime` without ACR publish run validation, accepts image artifact names or commit SHAs as dispatch input, uses mutable `latest`, or allows placeholder app runtime images.
- The validator must fail if cross-stage output wiring reads environment stage outputs from the wrong workspace, allows the `default` workspace, omits retained `public_dns` handling for `edge`, omits the app runtime image digest artifact requirement, or writes raw Terraform output, ACA FQDNs, private endpoint details, or secret-like values to workflow summaries.
- The validator must fail if the retained public DNS workflow can target non-`pd_eastus2_internal` scope, manage Cloudflare API/provider state, handle certificate material, omit public NS verification, or plan a public DNS destroy.
- The validator must have tests with unsafe workflow fixtures.

Recommended validator path:

```text
scripts/validate-terraform-workflow-guardrails.sh
tests/workflow-guardrails/
```

## Command Contract

Local validation from a stage directory:

```bash
terraform fmt -check -recursive
terraform init -backend=false
terraform validate
```

Remote plan flow:

```bash
TF_WORKSPACE=default terraform init
env -u TF_WORKSPACE terraform workspace select "$TF_WORKSPACE" || env -u TF_WORKSPACE terraform workspace new "$TF_WORKSPACE"
terraform validate
terraform plan -out "$PLAN_FILE"
```

Remote apply flow:

```bash
terraform workspace show
terraform apply "$PLAN_FILE"
```

The remote apply flow is implemented by guarded per-stage apply jobs in `.github/workflows/terraform-plan.yml`. Each apply job must run after its matching plan job, wait for `terraform-apply` environment approval, download the same-run plan artifact, select the derived workspace, and apply the saved plan file. In all-stage mode, the next stage plan job depends on the previous stage apply job.

App runtime deploy sequence:

```bash
scripts/terraform-app-runtime-images.sh list \
  --environment dv \
  --azure-region eastus2 \
  --customer-organization-slug internal

scripts/terraform-app-runtime-images.sh dispatch \
  --environment dv \
  --azure-region eastus2 \
  --customer-organization-slug internal
```

The helper lists successful `ACR Image Publish` runs with the expected workspace artifact and dispatches `.github/workflows/terraform-plan.yml`. By default, the workflow selects the latest successful matching publish run when `app_runtime` is included. Use `--acr-publish-run-id RUN_ID` only when intentionally pinning a known publish run. Use `--terraform-stage all` only when intentionally running the full environment stage chain.

Rules:

- Always run `terraform validate` before `terraform plan`.
- Never plan or apply from the default workspace.
- Never apply without displaying the selected workspace.
- Never apply without an approved plan artifact.
- Never apply a newly generated plan in the apply job when a reviewed saved plan artifact is required.
- Do not use `terraform apply -auto-approve` in guarded production workflows.

The currently installed local Terraform binary in this workspace is `v1.3.5`, which is older than the current Terraform version reported by the CLI. Implementation issues should decide the pinned Terraform version before creating workflow jobs.
