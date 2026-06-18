## Issue Shape

Each implementation issue must include:

- User outcome.
- Product surface.
- Data model entities touched.
- Authorization scope.
- Telemetry, content, or recommendation evidence involved.
- Azure resources involved.
- Tests and verification.
- Non-punitive and privacy guardrails.
- Source documents.
- Acceptance criteria.

Avoid:

- Component-only issues with no user or operational outcome.
- Issues that silently revive local-only mode.
- Issues that mix unrelated milestones.
- Issues that require production secrets or private operational details in public GitHub comments.

## Label Model

Recommended labels:

| Label | Purpose |
| --- | --- |
| `type:feature` | Product behavior |
| `type:infra` | Terraform, Azure, workflow, or deployment behavior |
| `type:docs` | Documentation only |
| `type:test` | Test or validation-only work |
| `area:api` | Product API |
| `area:ingestion` | Product Ingestion Endpoint |
| `area:jobs` | ACA Jobs and background work |
| `area:dashboard` | React Product Dashboard |
| `area:grafana` | Managed Grafana dashboards and provisioning |
| `area:terraform` | Terraform stages, modules, and workflow validation |
| `area:ops` | Day-1 operations, alerts, runbooks, validation drills |
| `area:data` | Product metadata, PostgreSQL, Blob Storage |
| `area:identity` | Entra, Product Role Mapping, Scoped Ingestion Credential |
| `guardrail:privacy` | Content, redaction, retention, and sensitive data constraints |
| `guardrail:non-punitive` | No ranking, no blame, coaching-oriented UX |
| `guardrail:public-repo` | Public repository workflow and issue safety |
| `guardrail:tenant` | Customer Organization and tenant isolation constraints |
| `status:blocked` | Blocked by external dependency or decision |
| `status:proof-needed` | Requires a documented implementation proof |

## Verified Platform Facts

- GitHub Issues can be associated with milestones, issue types, projects, assignees, and labels: https://docs.github.com/en/issues/tracking-your-work-with-issues/using-issues/creating-an-issue
- GitHub milestones track progress on groups of issues or pull requests in a repository: https://docs.github.com/issues/using-labels-and-milestones-to-track-work/about-milestones
- GitHub labels categorize issues and pull requests in a repository: https://docs.github.com/en/issues/using-labels-and-milestones-to-track-work/managing-labels
- GitHub sub-issues can break larger pieces of work into related tasks: https://docs.github.com/en/issues/tracking-your-work-with-issues/using-issues/adding-sub-issues
