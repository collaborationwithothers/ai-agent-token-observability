# Infrastructure Deletion Workflow

## Purpose

This document defines how Azure infrastructure can be deleted safely for the Azure Production MVP.

The goal is to remove disposable environment resources without deleting shared foundation resources such as remote state, shared Key Vaults where present, DNS, Azure Container Registry, and deployment identities.

## Decision

Infrastructure deletion uses Terraform stage-based destroy only.

Deletion must not use tag-based Azure resource deletion.

The workflow destroys approved disposable stages in reverse dependency order. It must never run a broad Azure resource deletion script, Azure CLI tag query deletion, portal cleanup checklist, or subscription/resource-group wipe that can bypass Terraform state and shared-resource protections.

## Rationale

Terraform already owns the planned Azure production resources by stage and workspace. Destroying through Terraform keeps deletion scoped to resources in the selected stage state.

Tag-based deletion is rejected because:

- Tags can drift.
- Shared resources can accidentally receive disposable-environment tags.
- Azure resources created outside the current stage can be caught by broad tag queries.
- It bypasses the stage dependency model and reviewable Terraform destroy plan.
- It is too risky for a public repository with manually triggered workflows.

## Retained Shared Resources

The deletion workflow must never destroy these resources:

| Resource | Reason |
| --- | --- |
| Terraform remote state resource group | Required to read state and audit prior infrastructure |
| Terraform remote state storage account | Required for backend state and locking |
| Terraform remote state container | Required for stage state |
| Shared Key Vault if provisioned | Holds application secrets and future BYOC certificate material if that deferred option is later adopted |
| Azure DNS zone `tokenobs.consultwithcloud.com` | Delegated product DNS zone |
| Public DNS Terraform stage `public_dns` | Owns the retained delegated product DNS zone |
| Cloudflare apex delegation records | Outside Terraform ownership for this repo |
| Azure Container Registry | Shared image source and rollback boundary |
| Shared deployment identities | Required for future deploy, destroy, and operations workflows |
| GitHub OIDC federated credential resources | Required for workflow authentication |
| Governance audit store if already provisioned as shared | Required for traceability |

Managed Grafana, Log Analytics, Application Insights, and Azure Monitor workspace retention is environment-specific until the observability architecture decides whether those resources are shared or disposable.

## Disposable Stage Order

Destroy runs in reverse dependency order.

Default disposable destroy order:

```text
edge
managed_grafana
app_runtime
ai_services
data_platform
observability_foundation
network_private_data_plane
foundation
```

Rules:

- `foundation` may be destroyed only if it does not contain any retained shared resource.
- `public_dns` is intentionally excluded from the disposable destroy order.
- If `foundation` contains shared Key Vault, ACR, deployment identities, or DNS ownership, split those resources into a retained shared-foundation stage before implementing deletion.
- The deletion workflow must refuse to destroy any stage that contains a retained shared resource.
- Stages must document whether they are disposable or retained.

## Workflow Trigger

Deletion-capable workflows must use `workflow_dispatch` only.

Forbidden triggers:

- `push`
- `pull_request`
- `pull_request_target`
- `workflow_run`
- `schedule`

Required inputs:

- `environment`
- `azure_region`
- `customer_organization_slug`
- `destroy_scope`
- `confirmation`
- `reason`

Allowed `destroy_scope` values:

- `environment`
- `stage`

Disallowed `destroy_scope` values:

- `subscription`
- `resource_group_by_tag`
- `all_tagged_resources`
- `shared`

## Required Gates

The workflow must validate all of these before Azure login:

- Expected repository.
- `workflow_dispatch` event.
- Expected actor or protected environment approval path.
- Allowed branch for the requested environment.
- Allowed environment.
- Allowed region.
- Allowed Customer Organization slug.
- Derived workspace equals `{environment}_{azureRegion}_{customerOrganizationSlug}`.
- Destroy scope is allowed.
- Confirmation text exactly matches the requested destroy target.
- Reason is non-empty.
- Target stages are in the disposable stage allow-list.
- Target stages are not in the retained shared stage deny-list.

Recommended confirmation format:

```text
destroy {environment}_{azureRegion}_{customerOrganizationSlug} {destroy_scope}
```

Example:

```text
destroy dv_eastus_internal environment
```

`pp` and `pd` deletion jobs must use GitHub environments with required reviewers and prevent self-review where available.

## Command Contract

Destroy planning:

```bash
terraform init
terraform workspace select "$TF_WORKSPACE"
terraform validate
terraform plan -destroy -out "$DESTROY_PLAN_FILE"
```

Destroy apply:

```bash
terraform workspace show
terraform apply "$DESTROY_PLAN_FILE"
```

Rules:

- Do not use `terraform destroy` directly in the apply job.
- Do not use `terraform apply -auto-approve`.
- Do not use `terraform destroy -auto-approve`.
- Do not use Terraform `-target` for normal environment deletion.
- Apply must use the reviewed destroy plan artifact.
- The selected workspace must be displayed before apply.
- The default workspace must never be used.
- Destroy plans must be retained as workflow artifacts for audit.

## State Handling

The workflow must not delete Terraform state blobs.

After successful deletion:

- Keep state for audit unless an explicit state-retention policy says otherwise.
- Record the deleted stage, workspace, actor, time, commit SHA, plan artifact ID, and reason.
- Do not delete the remote state storage account or container.
- Do not manually edit state in the normal workflow.

Manual state repair is a break-glass operation and must have a separate runbook.

## Guardrail Validator Rules

The workflow guardrail validator must fail if:

- Any deletion-capable workflow has forbidden triggers.
- A workflow contains Azure CLI or PowerShell commands that delete resources by tag.
- A workflow contains resource group deletion commands.
- A workflow contains subscription-wide delete commands.
- A workflow contains `terraform destroy -auto-approve`.
- A workflow contains `terraform apply -auto-approve`.
- A workflow runs destroy against the default workspace.
- A workflow can target retained shared stages.
- A workflow lacks repository, actor, branch, environment, region, workspace, confirmation, and protected-environment checks.

## Acceptance Criteria

- Deletion is possible only through manual `workflow_dispatch`.
- Deletion uses Terraform destroy plans by stage and workspace.
- Destroy runs in reverse dependency order.
- Retained shared resources are denied by workflow validation.
- Tag-based deletion is forbidden.
- Resource-group deletion is forbidden.
- Subscription-wide deletion is forbidden.
- `terraform apply -auto-approve` and `terraform destroy -auto-approve` are forbidden.
- Destroy plan artifacts are reviewable before apply.
- `pp` and `pd` require protected environment approval.
- Remote state remains after environment deletion.

## Verified Platform Facts

- Terraform `destroy` deprovisions objects managed by a Terraform configuration and is commonly suited to temporary infrastructure cleanup rather than long-lived production resources: https://developer.hashicorp.com/terraform/cli/commands/destroy
- Terraform plan supports destroy mode with `terraform plan -destroy`: https://developer.hashicorp.com/terraform/cli/commands/plan
- Terraform `azurerm` backend stores state in Azure Blob Storage and supports state locking and consistency checking with Azure Blob Storage native capabilities: https://developer.hashicorp.com/terraform/language/backend/azurerm
- GitHub Actions `workflow_dispatch` supports manually triggered workflows with inputs: https://docs.github.com/actions/using-workflows/workflow-syntax-for-github-actions#onworkflow_dispatch
- GitHub environments support required reviewers for deployment jobs: https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments
