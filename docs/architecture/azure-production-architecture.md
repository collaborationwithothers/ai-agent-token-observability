# Azure Production Architecture

## Purpose

This document describes the top-level Azure architecture for the Azure Production MVP and the path toward the Multi-Tenant SaaS Target State.

It is the architecture map for the narrower documents on content capture, recommendations, identity and authorization, observability backends, and Terraform workflow guardrails.

## Architecture Principles

- The product is production-hosted on Azure. Local execution is developer convenience only.
- The first release is a tenant-aware Single-Enterprise Release for one Customer Organization.
- The target state is a vendor-operated multi-tenant SaaS platform.
- Codex CLI is the first production harness.
- Product ingestion is the system of record. Direct-to-monitor-only ingestion is not authoritative.
- Metrics, traces and logs or events, product metadata, and captured content use separate stores.
- Aggregate visualization and product investigation are separate user experiences.
- The product must support optimization without people ranking or blame workflows.
- Terraform is the infrastructure-as-code path.

## High-Level Component Map

```text
Developer machine
  Codex CLI
    OTLP metrics, traces, logs/events
    scoped ingestion credential
        |
        v
Azure Front Door Premium WAF
  managed certificates for product hostnames
  public routing to generated ACA FQDN origins
        |
        v
Azure Container Apps
  Product Ingestion Endpoint
  Product API
  Product Dashboard
        |
        +--> Azure Monitor workspace / managed Prometheus
        |      aggregate metrics for Managed Grafana
        |
        +--> Application Insights / Log Analytics
        |      traces, logs/events, diagnostics, investigation queries
        |
        +--> Azure Database for PostgreSQL Flexible Server
        |      product metadata, sessions, hotspots, recommendations,
        |      policies, pricing, content references, audit events
        |
        +--> Pre-Storage Content Redaction Pipeline
               |
               v
             Azure Blob Storage
               policy-approved captured content only

Azure Container Apps Jobs
  normalization
  hotspot detection
  recommendation generation
  content redaction
  retention cleanup
  pricing refresh
  reprocessing
  tenant maintenance

Azure Managed Grafana
  aggregate metrics and high-level hotspot panels
  deep links to Product Dashboard session investigation
```

## Edge And Ingress

Azure Front Door Premium WAF is the first-release production edge. It protects public HTTPS traffic, terminates public TLS with Azure Front Door managed certificates, and routes to generated Azure Container Apps FQDN origins.

Public DNS and certificates are defined in [public-dns-and-certificates.md](./public-dns-and-certificates.md). The first-release product DNS zone is `tokenobs.consultwithcloud.com`, delegated from the Cloudflare-managed apex zone `consultwithcloud.com` to Azure DNS.

Public endpoints:

- Product Dashboard.
- Product Ingestion Endpoint.
- Product and admin APIs where required.

Required edge behavior:

- HTTPS only.
- Azure Front Door managed certificates for explicit product hostnames.
- WAF managed rules.
- Rate limiting for ingestion and app traffic.
- Tenant-aware request routing where needed.
- Health probes.
- Logs and metrics for edge traffic.
- Public Front Door routing to generated Azure Container Apps FQDN origins.
- Direct origin access controls are deferred to the later network hardening slice.
- ACA origins configured with generated ACA FQDN as the origin host name and origin host header.

Azure API Management is a future API gateway option, not a first-release dependency. It can be introduced later behind Azure Front Door WAF when API lifecycle management, policy centralization, partner access, quotas, subscriptions, or API product packaging becomes a requirement.

Application Gateway WAF is not the first-release edge because this release is a public SaaS-style endpoint. It remains a future option for regional private-ingress or VNet-native patterns if the product later needs that shape.

Verified platform facts:

- Azure Front Door provides global application delivery and WAF integration: https://learn.microsoft.com/en-us/azure/frontdoor/front-door-overview
- Azure Front Door WAF supports rate limiting rules: https://learn.microsoft.com/en-us/azure/web-application-firewall/afds/waf-front-door-rate-limit
- Azure Front Door Premium supports origin isolation options that are deferred from the current deployable path: https://learn.microsoft.com/en-us/azure/frontdoor/private-link
- Azure Container Apps supports public ingress with generated FQDN origins for the current Front Door path: https://learn.microsoft.com/en-us/azure/container-apps/ingress-overview
- Azure Container Apps supports ingress and built-in authentication for external ingress-enabled apps: https://learn.microsoft.com/en-us/azure/container-apps/ingress-overview and https://learn.microsoft.com/en-us/azure/container-apps/authentication

## Compute

Production app compute uses Azure Container Apps.

Long-running HTTP services:

- Product Dashboard.
- Product API.
- Product Ingestion Endpoint.

Admin and configuration API routes are part of Product API for the Azure Production MVP.

The detailed runtime split is defined in [runtime-topology.md](./runtime-topology.md).

The Product Dashboard frontend architecture is defined in [product-dashboard-ux.md](./product-dashboard-ux.md).

Finite or event-driven background tasks use Azure Container Apps Jobs from one shared jobs image with explicit job commands:

- Telemetry normalization.
- Hotspot detection.
- Recommendation generation.
- Content redaction.
- Retention cleanup.
- Reprocessing.
- Tenant maintenance.
- Pricing refresh.

Verified platform facts:

- Azure Container Apps hosts containerized applications and microservices: https://learn.microsoft.com/en-us/azure/container-apps/overview
- Azure Container Apps Jobs run finite-duration tasks: https://learn.microsoft.com/en-us/azure/container-apps/jobs

## Product Ingestion

The Product Ingestion Endpoint accepts Codex CLI Agent Telemetry Signals over OTLP.

The detailed first-release contract is defined in [codex-production-ingestion-contract.md](./codex-production-ingestion-contract.md).

Every ingestion request must be validated before it is accepted:

- Customer Organization.
- Scoped Ingestion Credential.
- Developer identity derived from the credential.
- Harness setup profile.
- Harness and schema version.
- Content Capture Policy.
- Data residency region.

Accepted telemetry is normalized into product records and routed to the appropriate backend.

Routing rules:

- Aggregate metrics go to the metrics backend for Managed Grafana.
- Traces, logs, and events go to the trace/log backend for investigation and diagnostics.
- Normalized domain records go to PostgreSQL.
- Policy-approved captured content goes through the Pre-Storage Content Redaction Pipeline before Blob Storage.

Direct-to-monitor-only ingestion is not authoritative because it bypasses product validation, policy enforcement, credential identity, tenant scope, and normalized session modeling.

## Observability Backends

The architecture uses an Observability Backend Split.

| Data class | Store | Primary use |
| --- | --- | --- |
| Aggregate metrics | Azure Monitor workspace / managed Prometheus | Managed Grafana dashboards, alerts, trends |
| Traces, logs, events | Application Insights / Log Analytics | Investigation queries, app diagnostics, correlation |
| Product metadata | Azure Database for PostgreSQL Flexible Server | Sessions, tenants, policies, hotspots, recommendations, audit |
| Captured content | Azure Blob Storage | Redacted and policy-approved content artifacts |

Azure Managed Grafana is the Managed Grafana Surface. It visualizes aggregate token burn, cost trends, model mix, harness activity, cache-related signals, and high-level hotspot panels.

The Managed Grafana dashboard boundary is defined in [managed-grafana-dashboards.md](./managed-grafana-dashboards.md). Product aggregate metric names, labels, and PromQL query contracts are defined in [aggregate-metrics-contract.md](./aggregate-metrics-contract.md). First-release Grafana dashboards use Azure Monitor workspace or managed Prometheus as the primary aggregate metrics data source. Grafana does not own session investigation, raw trace review, content review, or recommendation workflows.

The Product Dashboard owns Session Investigation View, content review, governance, and recommendations.

The Product Dashboard is a React SPA backed by Product API. It must not query the product stores or telemetry stores directly.

Verified platform facts:

- Azure Monitor managed service for Prometheus is a managed Prometheus metrics service: https://learn.microsoft.com/en-us/azure/azure-monitor/metrics/prometheus-metrics-overview
- Azure Managed Grafana can visualize Azure Monitor data: https://learn.microsoft.com/en-us/azure/azure-monitor/visualize/visualize-grafana-overview
- Azure Monitor managed service for Prometheus can connect to Azure Managed Grafana: https://learn.microsoft.com/en-us/azure/azure-monitor/metrics/prometheus-grafana
- Azure Monitor includes Log Analytics and Application Insights for telemetry analysis: https://learn.microsoft.com/en-us/azure/azure-monitor/

## Product Metadata Store

Azure Database for PostgreSQL Flexible Server is the Product Metadata Store.

The logical product data model is defined in [data-model.md](./data-model.md).

The Product API route contract is defined in [product-api-contract.md](./product-api-contract.md).

It stores:

- Customer Organizations.
- Federated identity mappings.
- Product Role Mappings.
- Teams.
- Repository enrollment records.
- Harness setup profiles.
- Scoped Ingestion Credential metadata.
- Sessions.
- Normalized telemetry summaries.
- Token Hotspots.
- Recommendation records.
- Pricing basis and pricing update review records.
- Content references and redaction status.
- Retention policies.
- Governance Audit Events.

PostgreSQL does not store full captured content by default. It stores metadata, references, hashes, classifications, status, policy version, retention class, and audit context.

Required access pattern:

- Managed identity where supported.
- Least-privilege database roles.
- Tenant-aware schema.
- Explicit Customer Organization scope on product records.
- Private data-plane access where feasible.

Verified platform facts:

- Azure Database for PostgreSQL Flexible Server is a managed PostgreSQL service: https://learn.microsoft.com/en-us/azure/postgresql/overview
- PostgreSQL Flexible Server supports Microsoft Entra authentication: https://learn.microsoft.com/en-us/azure/postgresql/security/security-entra-configure
- Managed identity authentication patterns are documented for PostgreSQL Flexible Server: https://learn.microsoft.com/en-us/azure/postgresql/security/security-connect-with-managed-identity

## Content Capture Store

Azure Blob Storage is the Content Capture Store.

Captured content is stored only when:

- Content Capture Policy allows capture.
- The content passes the Pre-Storage Content Redaction Pipeline.
- The Redaction Failure Gate does not block storage.
- The content is within the allowed retention class.
- The content is linked to product metadata and audit context.

The product uses Platform-Managed Encryption. Customer Managed Keys are not offered.

Blob Storage organization should support the tenant isolation tier:

- Shared tier: tenant-scoped containers or prefixes with strict authorization.
- Dedicated tier: dedicated storage accounts can be added in target state if required.

Blob lifecycle management should enforce retention for captured content.

Verified platform facts:

- Azure Storage encrypts data at rest: https://learn.microsoft.com/en-us/azure/storage/common/storage-service-encryption
- Blob lifecycle management supports rule-based transition and deletion: https://learn.microsoft.com/en-us/azure/storage/blobs/lifecycle-management-overview
- Azure Storage supports private endpoints: https://learn.microsoft.com/en-us/azure/storage/common/storage-private-endpoints

## Identity And Authorization Architecture

The product uses Federated Customer Identity and Product Role Mapping. The detailed model is defined in [identity-and-authorization.md](./identity-and-authorization.md).

Key boundaries:

- Users authenticate through the Customer Organization identity provider, initially Microsoft Entra ID.
- Customer Entra users or groups can be assigned to product app roles.
- Runtime authorization resolves product roles and product scopes from Product Role Mapping.
- Raw external group names are not business authorization decisions.
- Scoped Ingestion Credential identity is authoritative for telemetry upload and session ownership.
- Harness-emitted identity is retained as evidence.
- Identity mismatch must be flagged, not silently merged.

## Data Residency And Region Strategy

Each Customer Organization has a Customer Data Residency Region.

The Azure Production MVP is single-region active per environment. Multi-region placement, failover, and tenant data-residency tiers are target-state concerns.

Terraform workspaces use:

```text
{environment}_{region}_{customerOrganizationSlug}
```

Examples:

```text
dv_eastus_internal
qa_westeurope_internal
pp_eastus2_internal
pd_eastus2_internal
```

Data that should remain in the Customer Data Residency Region unless policy allows otherwise:

- Product metadata.
- Captured content.
- Detailed traces, logs, and events.
- Recommendation evidence.
- Redaction evidence.

## Deployment Architecture

Infrastructure is Terraform-first with Azure Blob Storage remote state. The implementation-ready stage, module, workspace, and workflow guardrail contract is defined in [terraform-production-infrastructure.md](./terraform-production-infrastructure.md).

Terraform implementation rules:

- Use Azure Verified Modules where suitable modules exist.
- Use AzureRM resources when no suitable AVM exists.
- Use AzAPI only for provider gaps.
- Keep state separated by Region Environment Workspace.
- Keep environments explicit: `dv`, `qa`, `pp`, `pd`.

Deployment-capable GitHub Actions must follow Public Repository Workflow Guardrails:

- `workflow_dispatch` only.
- No `pull_request` or `pull_request_target` trigger for Azure-changing jobs.
- Expected repository validation.
- Expected actor validation.
- Branch and environment validation.
- Explicit region and environment inputs.
- Derived workspace validation.
- Least-privilege `GITHUB_TOKEN` permissions.
- Azure OIDC with least privilege.
- Protected environment approval for normal deploy apply.
- Customer organization slug defaults to `internal` and remains overrideable for non-internal customer scopes.
- Retained public DNS apply is fixed to `public_dns` in `pd_eastus2_internal`, protected by the public DNS apply environment, limited to same-run saved plan apply, and paired with public NS verification.
- Environment protection for `pp` and `pd`.
- Guardrail validation script and tests.

Guarded Terraform Apply is allowed only after all deployment gates pass.

## Security Boundaries

Mandatory boundaries:

- Public HTTPS only for product ingress.
- WAF and rate limits at the production edge.
- Product-level tenant validation on every ingestion request.
- Product-level authorization for every dashboard and API request.
- Managed identity for Azure-hosted service access where feasible.
- Private access for data stores where feasible.
- Platform-managed encryption only.
- No Customer Managed Keys.
- No store-then-redact content capture.
- No silent content capture.
- No people ranking or developer blame workflows.

## Architecture Open Questions

These are tracked in [implementation-readiness-review.md](./implementation-readiness-review.md). Requirements-level architecture decisions are ready for implementation issue creation. Remaining items are implementation proofs, environment-specific values, or capacity-validation tasks.

Resolved by current docs:

- Exact OTLP protocol shape for Codex CLI ingestion.
- PostgreSQL logical schema.
- Product API route contract.
- Runtime service topology across Container Apps and jobs.
- Product Dashboard frontend framework and UX contract.
- Blob Storage container and prefix model.
- Redaction recognizer list, confidence thresholds, and retention defaults.
- Recommendation engine contract, deployment aliases, validation gates, and prompt-template version model.
- Terraform stage and module layout.
- Terraform manually created remote state foundation and workspace validation rules.
- Public-repository workflow guardrail validator rules.
- Product subdomain delegation to Azure DNS.
- Public TLS through Front Door managed certificates.
- Public Front Door routing to generated ACA FQDN origins, with origin isolation deferred.
- Deferred BYOC certificate renewal runtime.
- Infrastructure deletion workflow and retained shared-resource boundary.
- Day-1 operations baseline.
- Production codebase transition boundary.

Implementation proof or environment-specific items:

- PostgreSQL row-level security implementation strategy, if used in addition to application-level tenant scoping.
- Exact Azure OpenAI model SKU per deployment alias after regional capacity and structured-output support validation.
- Exact Managed Grafana environment-specific Entra group object ID values, Entra token provider compatibility proof, and Product Dashboard link allowlist implementation.
- Deferred origin isolation hardening design and end-to-end proof.
