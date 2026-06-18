# Terraform Production Infrastructure

This index routes Terraform infrastructure context to smaller reference files. Do not load every reference by default; choose the file that matches the changed boundary.

## References

- [Workspaces, Stages, And Modules](./terraform-production-infrastructure/workspaces-stages-and-modules.md)
  - Purpose and source documents.
  - Terraform decisions.
  - Workspace contract.
  - Manual remote state foundation.
  - Stage layout.
  - Resource group boundaries.
  - Module conventions.
  - AVM and provider map.
  - Stage variables.
  - Remote state references.
- [Workflow Guardrails And Commands](./terraform-production-infrastructure/workflow-guardrails-and-commands.md)
  - Public repository workflow guardrails.
  - Normal deploy and destroy workflow constraints.
  - Runtime image publishing boundaries.
  - Command contract.
- [Acceptance And Platform Facts](./terraform-production-infrastructure/acceptance-and-platform-facts.md)
  - Terraform resource acceptance criteria.
  - Verified platform facts and source links.

## Loading Guidance

- For a stage or module implementation issue, start with [Workspaces, Stages, And Modules](./terraform-production-infrastructure/workspaces-stages-and-modules.md).
- For GitHub Actions, deployment, destroy, or image-publish behavior, start with [Workflow Guardrails And Commands](./terraform-production-infrastructure/workflow-guardrails-and-commands.md).
- For issue acceptance criteria, validation requirements, or provider/platform facts, start with [Acceptance And Platform Facts](./terraform-production-infrastructure/acceptance-and-platform-facts.md).
- For Terraform plans, do not stream full text into the conversation by default. Prefer `terraform show -json` with `jq` assertions or narrow text filters.
