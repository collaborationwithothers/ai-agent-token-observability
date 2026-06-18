## Public Repository Workflow Guardrails

Deployment-capable GitHub Actions must be designed for a public repository.

Infrastructure deletion is defined separately in [Infrastructure Deletion Workflow](../../operations/infrastructure-deletion.md). Deletion must use guarded Terraform destroy plans by stage and workspace, not tag-based Azure deletion.

The normal Terraform deploy workflow produces a saved plan artifact for each selected stage, waits on the `terraform-apply` GitHub environment, downloads the approved same-run artifact, verifies stage and workspace, and applies only that saved plan file. In an all-stage deploy, the workflow plans, reviews, and applies one stage before moving to the next dependent stage, so downstream stages can read remote state outputs produced earlier in the same run.

Runtime image publishing is also separate from Terraform plan. The image publish workflow builds the Product Dashboard, Product API, Product Ingestion Endpoint, and shared Product Jobs images, publishes them to Azure Container Registry with immutable commit SHA tags, and emits digest-pinned app runtime Terraform inputs.

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
- `customer_organization_slug`
- optional `terraform_stage`
- `confirmation`

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
- `confirmation` exactly matches `deploy {workspace}` for an all-stage deploy or `deploy {stage} {workspace}` for a single-stage deploy.

The retained public DNS workflow is the only Terraform apply exception to the `confirmation` input requirement. It is not a normal deployment workflow. Its replacement controls are a fixed `public_dns` stage, the single owner workspace `pd_eastus2_internal`, repository, actor, branch, `pd`, `eastus2`, and `internal` gates, same-run saved plan artifact apply, the protected `terraform-public-dns-apply` GitHub environment, Cloudflare delegation output only, and public NS verification before edge deployment depends on the delegated zone.

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

Guardrail validator:

- A committed validator must inspect `.github/workflows/*.yml`.
- The validator must fail if an Azure-changing workflow has forbidden triggers.
- The validator must fail if a normal Azure-changing workflow lacks repository, actor, environment, region, workspace, branch, OIDC, permissions, and confirmation checks.
- The validator must fail if the retained public DNS workflow lacks its documented replacement controls for the confirmation exception.
- The validator must fail if `terraform apply -auto-approve` appears in deployment-capable workflows.
- The validator must fail if the normal Terraform deploy workflow can apply without the `terraform-apply` environment, without downloading the saved plan artifact, or with `public_dns` in normal target stages.
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
terraform init
terraform workspace select "$TF_WORKSPACE" || terraform workspace new "$TF_WORKSPACE"
terraform validate
terraform plan -out "$PLAN_FILE"
```

Remote apply flow:

```bash
terraform workspace show
terraform apply "$PLAN_FILE"
```

The remote apply flow is implemented by guarded per-stage apply jobs in `.github/workflows/terraform-plan.yml`. Each apply job must run after its matching plan job, wait for `terraform-apply` environment approval, download the same-run plan artifact, select the derived workspace, and apply the saved plan file. In all-stage mode, the next stage plan job depends on the previous stage apply job.

Rules:

- Always run `terraform validate` before `terraform plan`.
- Never plan or apply from the default workspace.
- Never apply without displaying the selected workspace.
- Never apply without an approved plan artifact.
- Never apply a newly generated plan in the apply job when a reviewed saved plan artifact is required.
- Do not use `terraform apply -auto-approve` in guarded production workflows.

The currently installed local Terraform binary in this workspace is `v1.3.5`, which is older than the current Terraform version reported by the CLI. Implementation issues should decide the pinned Terraform version before creating workflow jobs.
