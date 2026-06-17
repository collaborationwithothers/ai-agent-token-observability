# Workflow Guardrail Validation

## Purpose

Deployment-capable GitHub Actions in this public repository must stay manual-only and must validate public-repository safety gates before Azure login or Terraform execution.

## Local Validation

Run:

```bash
scripts/validate-terraform-workflow-guardrails.sh --self-test
scripts/validate-terraform-workflow-guardrails.sh
```

The self-test validates committed safe and unsafe fixtures under:

```text
tests/workflow-guardrails/
```

The default command validates deployment-capable workflows under:

```text
.github/workflows/
```

## Guardrails

The validator fails deployment-capable workflows that:

- Use triggers other than `workflow_dispatch`.
- Do not target the `consultwithcloud-azure` runner group with the `gh-linux` label.
- Lack a job-level `if` gate for `github.event_name`, `github.repository`, `github.actor`, and `github.ref`.
- Lack least-privilege `contents: read` and `id-token: write` permissions.
- Lack repository, actor, branch, environment, region, customer slug, workspace derivation, and protected-environment gates.
- Normal Terraform deploy workflows do not derive selected stages, upload saved plan artifacts, download same-run artifacts for apply, gate apply through the `terraform-apply` GitHub environment, or apply only saved plan files.
- Normal Terraform deploy workflows expose or include the retained `public_dns` stage instead of leaving shared DNS to its separate lifecycle.
- Retained public DNS workflows do not pin `public_dns` to `pd_eastus2_internal`, do not gate apply through `terraform-public-dns-apply`, do not emit `cloudflare_delegation_ns_records`, or do not verify public NS delegation with `dig`.
- Retained public DNS workflows introduce Cloudflare API credentials, a Cloudflare Terraform provider, certificate private key material, ACME state, or a public DNS destroy plan.
- Deletion workflows do not gate apply through the `terraform-deletion-approval` GitHub environment.
- Run Azure login or Terraform before the guardrail validation step.
- Use forbidden destructive patterns such as `terraform apply -auto-approve`, `terraform destroy -auto-approve`, direct `terraform destroy`, resource group deletion, or tag-based resource deletion.

The validator is a local guardrail only. GitHub environment protection, Entra OIDC configuration, Azure RBAC, and Terraform backend access must still be configured outside this repository before workflows can run against Azure.
