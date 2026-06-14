# Identity And Authorization Architecture

## Purpose

This document defines the production identity and authorization model for the Azure Production MVP and the Multi-Tenant SaaS Target State.

The design goal is to protect the Product Dashboard, Product APIs, Session Investigation View, content review workflows, and telemetry ingestion without turning external directory details into product business logic.

## Requirements

- Users authenticate with a Customer Organization identity provider, initially Microsoft Entra ID.
- Customer administrators can assign different product access levels to Microsoft Entra users or groups.
- The product supports PlatformAdmin, SecurityReviewer, EngineeringLead, Developer, and ReadOnlyViewer roles.
- Authorization decisions are tenant-aware and resource-scoped.
- Dashboard defaults are identity-minimized and non-punitive.
- Scoped Ingestion Credentials, not harness-emitted identity, authenticate telemetry upload and session ownership.
- The Azure Production MVP supports one Customer Organization but must not hardcode single-tenant assumptions into authorization.
- The Multi-Tenant SaaS Target State supports one Customer Organization connected to one or more identity tenants.

## Principles

Authentication proves the caller identity.

Authorization decides what the caller can do inside a Customer Organization.

External groups are input evidence for role resolution. They are not product authorization rules by themselves.

Product authorization must always include:

- Customer Organization.
- Authenticated subject.
- Product role.
- Product scope.
- Requested action.
- Data sensitivity.
- Content Capture Policy.

## Identity Model

### Customer Organization

Customer Organization is the product tenant. It owns product configuration, role mappings, teams, repositories, setup profiles, retention policy, content capture policy, pricing basis, and recommendation model policy.

In the Azure Production MVP, there is one Customer Organization. The code and schema still include a Customer Organization identifier on authorization-sensitive records.

In the Multi-Tenant SaaS Target State, one Customer Organization can connect one or more identity tenants.

### Identity Tenant

An Identity Tenant is an external identity authority connected to a Customer Organization.

For the first release, the Identity Tenant is Microsoft Entra ID.

Target-state identity tenant metadata includes:

- Customer Organization.
- Tenant issuer.
- Tenant ID.
- Allowed audiences.
- JWK metadata source.
- Display name.
- Status.
- Last successful sign-in validation.

### Product User

Product User is the product-side representation of an authenticated person.

The product stores stable external identity references, not local passwords.

Minimum fields:

- Customer Organization.
- Identity Tenant.
- External subject identifier.
- Display label.
- Email or user principal name where available.
- Status.
- First seen timestamp.
- Last seen timestamp.

## Role Model

Initial product roles:

- `PlatformAdmin`: manages Customer Organization settings, identity tenant connections, role mappings, harness setup, ingestion credentials, retention, pricing, and policy.
- `SecurityReviewer`: reviews sensitive captured content, redaction failures, approved excerpts, and security-sensitive evidence where policy allows.
- `EngineeringLead`: views assigned team and repository analytics, sessions, hotspots, and recommendations.
- `Developer`: views their own sessions and coaching, plus scoped team or repository data where policy allows.
- `ReadOnlyViewer`: views approved aggregate dashboards without sensitive session content or individual coaching by default.

Roles are product roles. Microsoft Entra app roles may carry coarse role claims, but the application still resolves the effective product role and scope from Product Role Mapping.

## Product Role Mapping

Product Role Mapping is stored in the Product Metadata Store.

It maps external identity evidence to product roles and scopes.

Supported mapping sources:

- Entra app role claim.
- Entra group object ID.
- Direct external user subject.
- Service principal or workload identity for service-to-service scenarios.

Each mapping includes:

- Customer Organization.
- Identity Tenant.
- External principal type.
- External principal identifier.
- Product role.
- Product scope.
- Status.
- Effective from timestamp.
- Optional expiry timestamp.
- Created by.
- Last changed by.
- Audit reference.

Product scopes:

- Customer Organization.
- Team.
- Repository.
- Harness setup profile.
- Session self-view.
- Content review queue.
- Pricing and budget configuration.
- Tenant administration.

## Entra Group Assignment Strategy

Customer administrators should assign Entra users or groups to product access through Entra application roles where possible.

The product defines app roles that correspond to coarse product access levels. Customer administrators assign Entra groups to those app roles in their enterprise application.

The product then maps the received app role claim to product roles and product scopes.

This keeps customer group IDs out of application business logic and avoids relying on every group membership appearing in a token.

Group object ID mappings are allowed when a Customer Organization needs finer control or when app-role assignment is not sufficient. Group names are not authorization identifiers.

## Dashboard Authentication Flow

The Product Dashboard is public HTTPS through the Production Edge and a private Product Dashboard Container App origin.

First-release flow:

1. User opens the Product Dashboard through Azure Front Door.
2. Front Door applies WAF, rate limits, TLS, and routing policy.
3. Front Door routes to the Dashboard Container App through the private origin path.
4. The Dashboard Container App requires Microsoft Entra authentication.
5. The product API validates the authenticated principal and tenant context.
6. The product resolves Product Role Mapping from the Product Metadata Store.
7. The product enforces action and data-sensitivity checks for every request.

Authentication redirect URIs, callback URLs, cookies, and browser-visible links must use the public Front Door hostnames. Generated Azure Container Apps hostnames must not appear in first-release user-facing auth flows.

Container Apps built-in authentication can be used as an ingress authentication guard, but it is not the product authorization engine. Product authorization remains application-owned.

## Product API Authorization

Every Product API request must resolve an Authorization Context.

Authorization Context fields:

- Customer Organization.
- Identity Tenant.
- Authenticated subject.
- Product User.
- Effective roles.
- Effective scopes.
- Requested resource.
- Requested action.
- Requested data class.
- Content sensitivity.
- Recommendation evidence state.

API authorization checks must be explicit. No API should rely only on route grouping, frontend hiding, Grafana permissions, or external group names.

## Managed Grafana Authorization

Managed Grafana is an aggregate observability surface.

Grafana views must not expose sensitive session content, captured content, redaction queues, or individual coaching by default.

Grafana access is coarse-grained for the Azure Production MVP.

First-release Grafana access maps Microsoft Entra groups to Azure Managed Grafana built-in roles only:

| Entra group purpose | Azure Managed Grafana role | Scope |
| --- | --- | --- |
| Grafana administrators | Grafana Admin | Workspace operations, data source wiring, folders, and dashboard provisioning |
| Grafana dashboard editors | Grafana Editor | Controlled editing of aggregate dashboard JSON and panels in non-production only |
| Grafana dashboard viewers | Grafana Viewer | Read-only aggregate dashboard access |

Grafana roles must not mirror Product Dashboard roles such as ProductOwner, EngineeringLead, Developer, SecurityReviewer, or PlatformAdmin.

Production Grafana is human Viewer-only by default. Production Admin access is limited to a small break-glass or platform operations group, and routine production dashboard changes must flow through versioned dashboard JSON and Terraform. Human Grafana Editor assignments are allowed in `dv` and `qa`; `pp` or `pd` Editor assignments require an explicit exception.

Grafana role mappings are configured with environment-scoped Terraform variables:

| Variable | Role |
| --- | --- |
| `grafana_admin_group_object_id` | Grafana Admin |
| `grafana_editor_group_object_id` | Grafana Editor |
| `grafana_viewer_group_object_id` | Grafana Viewer |

The variables must contain Microsoft Entra group object IDs, not display names. Object IDs are not secrets, but they are authorization-sensitive configuration. In `pp` and `pd`, `grafana_editor_group_object_id` must be null or empty unless `allow_production_grafana_editors = true` and the apply receives the required workflow approval.

Session drill-down belongs in the Product Dashboard, where product authorization can enforce Customer Organization, role, scope, content policy, and audit rules.

## Session Investigation Authorization

Session Investigation View is role and scope protected.

Default visibility:

- Developer can view their own sessions and coaching.
- EngineeringLead can view sessions for assigned teams or repositories, subject to content policy and identity-minimized defaults.
- SecurityReviewer can view redaction failures and policy-approved sensitive evidence queues.
- PlatformAdmin can administer policy and mappings but should not automatically bypass content review policy.
- ReadOnlyViewer sees aggregate metrics only unless explicitly granted a narrower investigation scope.

Individual coaching must not appear in public dashboards, leaderboard-style views, or manager rankings.

## Captured Content Authorization

Captured content access is separate from session metadata access.

To read a Captured Content Blob or approved excerpt, the caller must satisfy all of:

- Authenticated user.
- Customer Organization match.
- Role grants content access.
- Scope grants the relevant session, team, repository, or review queue.
- Content Capture Policy allows access.
- Redaction state is approved for viewing.
- Audit event is written.

If content is marked `redaction_failed` or `review_required`, it is not visible in normal Session Investigation View. It is visible only to authorized SecurityReviewer workflows as metadata and bounded review material.

## Telemetry Ingestion Identity

Telemetry upload is not authenticated by the dashboard user's interactive session.

Telemetry upload uses Scoped Ingestion Credentials.

The credential is issued for:

- Customer Organization.
- Developer.
- Harness.
- Harness setup profile.
- Optional repository or team scope.
- Expiry and rotation policy.

Credential-derived developer identity is authoritative for telemetry upload, attribution, self-view, and session ownership.

Harness-emitted identity is stored as evidence. If the harness-emitted identity conflicts with credential-derived identity, the product flags an identity mismatch and keeps the credential-derived identity authoritative.

## Service Identity

Azure services use managed identities where possible.

Service identities include:

- Product Dashboard API identity.
- Product Ingestion Endpoint identity.
- Background job identity.
- Recommendation generation identity.
- Redaction job identity.
- Pricing seed refresh identity.

Service identities receive least-privilege Azure RBAC assignments and product service roles.

No service identity should receive broad data-plane access unless its job requires it.

## Authorization Matrix

| Capability | PlatformAdmin | SecurityReviewer | EngineeringLead | Developer | ReadOnlyViewer |
| --- | --- | --- | --- | --- | --- |
| Manage Customer Organization | Yes | No | No | No | No |
| Manage Product Role Mapping | Yes | No | No | No | No |
| Manage Content Capture Policy | Yes | Review input | No | No | No |
| View aggregate metrics | Yes | Yes | Scoped | Scoped | Approved only |
| View own sessions | Yes | No by default | No by default | Yes | No |
| View team or repo sessions | Scoped | No by default | Scoped | No by default | No |
| View captured content | Policy scoped | Review scoped | No by default | Own approved excerpts only | No |
| Review redaction failures | No by default | Yes | No | No | No |
| Regenerate recommendations | Scoped | Scoped for review | Scoped | Own sessions | No |
| Manage pricing basis | Yes | No | No | No | No |
| Export customer data | Yes, audited | Security export only | No by default | Own export only | No |

## Audit Requirements

Governance Audit Events are required for:

- Identity tenant connection changes.
- Product role mapping changes.
- User first seen and role resolution failures.
- Access denied events for sensitive content.
- Captured content reads.
- Redaction reviewer decisions.
- Scoped Ingestion Credential creation, rotation, revocation, and use anomalies.
- Identity mismatch events.
- Recommendation generation and regeneration.
- Pricing basis changes.
- Export and deletion workflows.

Audit records must include Customer Organization, actor, effective role, action, target resource, decision, timestamp, and correlation ID.

## Failure Behavior

Fail closed when:

- Token issuer is unknown.
- Audience is invalid.
- Customer Organization cannot be resolved.
- No effective Product Role Mapping exists.
- Role exists but scope is missing.
- Content sensitivity exceeds caller role or scope.
- Group overage cannot be resolved when group-based mapping is required.
- Ingestion credential is expired, revoked, unknown, or outside scope.

Failing closed should produce an audit record and a user-safe error message.

## MVP Boundary

The Azure Production MVP includes:

- One Customer Organization.
- One connected Microsoft Entra identity tenant.
- Product roles listed in this document.
- Entra app-role based assignment where possible.
- Product Role Mapping records in PostgreSQL.
- Container Apps hosted Product Dashboard and APIs.
- Scoped Ingestion Credential identity for Codex CLI telemetry upload.
- Captured content access separation.
- Governance audit events for security-sensitive authorization decisions.

The Azure Production MVP does not need:

- Self-service multi-identity-tenant onboarding.
- Customer-managed keys.
- Device-management-driven silent enrollment.
- Person-ranking dashboards.
- Raw group-name authorization.

## Target-State Additions

The Multi-Tenant SaaS Target State adds:

- Multiple Customer Organizations.
- Multiple Identity Tenants per Customer Organization.
- Customer self-service identity connection workflow.
- Break-glass support workflow with customer approval and audit.
- Dedicated tenant isolation tiers.
- Automated role mapping drift detection.
- Self-service data export and deletion.
- More granular policy scopes for content, recommendations, pricing, and budgets.

## Verified Platform Facts

- Microsoft Entra app roles can be assigned to users and groups and emitted in the `roles` claim: https://learn.microsoft.com/en-us/entra/identity-platform/howto-add-app-roles-in-apps
- Microsoft recommends app roles for ISV scenarios because app roles are application-specific and avoid hardcoding tenant-specific group names into application authorization: https://learn.microsoft.com/en-us/entra/identity-platform/howto-add-app-roles-in-apps#app-roles-vs-groups
- Group claims can hit overage limits, requiring applications to handle missing group claims and query Microsoft Graph where needed: https://learn.microsoft.com/en-us/security/zero-trust/develop/configure-tokens-group-claims-app-roles#group-overages
- Azure Container Apps provides built-in authentication and authorization integration with Microsoft Entra ID, but application code still validates app-specific roles and claims: https://learn.microsoft.com/en-us/azure/container-apps/authentication
- Azure Front Door WAF supports managed rules, custom rules, and rate-limit rules: https://learn.microsoft.com/en-us/azure/web-application-firewall/afds/afds-overview
