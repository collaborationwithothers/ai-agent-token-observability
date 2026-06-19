# Runtime Service Topology

## Purpose

This document defines the Azure Production MVP runtime split across Azure Container Apps and Azure Container Apps Jobs.

It closes the service-topology gap in the production architecture docs so implementation issues can refer to concrete runtime boundaries.

## Source Documents

- [Azure Production Architecture](./azure-production-architecture.md)
- [Product API Contract](./product-api-contract.md)
- [Codex Production Ingestion Contract](./codex-production-ingestion-contract.md)
- [Identity And Authorization Architecture](./identity-and-authorization.md)
- [Content Capture And Redaction Architecture](./content-capture-and-redaction.md)
- [Recommendation Engine Architecture](./recommendation-engine.md)

## MVP Runtime Decision

The Azure Production MVP uses three long-running Azure Container Apps:

| Container App | Ingress | Primary responsibility |
| --- | --- | --- |
| Product Dashboard | Public through Azure Front Door Premium WAF to generated ACA FQDN origin | User-facing dashboard shell and static or server-rendered frontend |
| Product API | Private to Product Dashboard or public only through Azure Front Door Premium WAF when required, routed to generated ACA FQDN origin | Product API, admin API surfaces, session investigation, identity, content review, recommendations, pricing, budgets, and audit routes |
| Product Ingestion Endpoint | Public only through Azure Front Door Premium WAF to generated ACA FQDN origin | OTLP/HTTP ingestion for Codex CLI Agent Telemetry Signals |

The MVP uses one shared jobs container image with explicit job commands for bounded background work.

| Job command | Trigger type | Primary responsibility |
| --- | --- | --- |
| `normalize-telemetry` | Event or schedule | Normalize accepted telemetry envelopes into product records |
| `detect-hotspots` | Event or schedule | Create or update Token Hotspots from normalized evidence |
| `generate-recommendations` | Event or manual | Generate deterministic and policy-approved LLM-assisted recommendation records |
| `redact-content` | Event, manual, or retry | Run bounded redaction retries or review-approved excerpt processing |
| `refresh-pricing` | Schedule or manual | Fetch automated pricing seed candidates and prepare reviewable diffs |
| `retention-cleanup` | Schedule | Apply Data-Class Retention Policy to product metadata and captured content references |
| `reprocess-session` | Manual | Re-run normalization, hotspot detection, or recommendations for a scoped session |
| `tenant-maintenance` | Schedule or manual | Run tenant-scoped maintenance tasks such as policy materialization and consistency checks |

## Why This Split

The three-app split keeps different traffic and security profiles separate without over-fragmenting the MVP:

- Product Dashboard is optimized for browser traffic, Entra sign-in, and user experience.
- Product API owns product authorization and dashboard data contracts.
- Product Ingestion Endpoint owns high-volume machine-to-machine telemetry validation and OTLP parsing.
- Jobs stay out of the request path and can be scaled, retried, and operated independently.

Production ingress rule:

- Public users and harnesses reach product services only through Azure Front Door.
- Azure Front Door routes to generated Azure Container Apps FQDN origins in the current deployable path.
- Origin isolation beyond public Front Door routing is deferred to a later network hardening slice.
- Front Door managed certificates protect the public hostnames; ACA origin TLS uses the generated ACA hostname and certificate.
- Managed Azure VNet GitHub runners may validate allowlisted resources, but runner placement does not replace Front Door controls.

Admin API routes are part of Product API in the MVP. They are not a separate Container App unless future scale, isolation, or compliance requirements justify that split.

The shared jobs image is an MVP delivery-speed tradeoff. It avoids building and operating many nearly identical worker images before job boundaries prove they need independent release cadence, dependencies, or scaling.

## Container App Responsibilities

### Product Dashboard

Responsibilities:

- Authenticate users through the configured federated identity flow.
- Bootstrap user context from `GET /api/v1/me`.
- Render aggregate dashboard entry points, session investigation, content review, recommendation review, governance, and administration screens.
- Link to Managed Grafana for aggregate observability where appropriate.

It must not:

- Query PostgreSQL, Blob Storage, Log Analytics, Application Insights, or Managed Prometheus directly from the browser.
- Hold product authorization logic that bypasses Product API.
- Expose raw captured content outside Product API content-review checks.

### Product API

Responsibilities:

- Implement the [Product API Contract](./product-api-contract.md).
- Resolve Authorization Context for every non-health request.
- Enforce Product Role Mapping and product scopes.
- Serve Session Investigation View data to the dashboard.
- Own admin API surfaces for identity, role mappings, setup profiles, credentials, pricing, budget policies, and audit queries.
- Coordinate recommendation regeneration requests and content review decisions.

It must not:

- Accept OTLP telemetry directly from harnesses.
- Trust browser-visible roles without server-side authorization checks.
- Return captured content bodies unless policy, redaction status, role, and scope allow it.

### Product Ingestion Endpoint

Responsibilities:

- Accept Codex CLI telemetry over OTLP/HTTP.
- Authenticate Scoped Ingestion Credentials.
- Resolve Customer Organization, developer identity, setup profile, harness, schema version, and Content Capture Policy.
- Reject invalid or unsupported telemetry before normalization.
- Route accepted telemetry into the Observability Backend Split and Product Metadata Store.
- Create ingestion rejection records for rejected but auditable requests.

It must not:

- Serve Product Dashboard routes.
- Expose admin operations.
- Treat direct Azure Monitor ingestion as the product source of record.

## Job Image Contract

The shared jobs image exposes explicit commands. Each command must:

- Accept Customer Organization scope where tenant-specific work is performed.
- Accept correlation and operation IDs for audit and retry tracking.
- Be idempotent where retries are possible.
- Emit job telemetry to Application Insights or Log Analytics.
- Use the same product data access libraries and authorization-aware service boundaries as Product API where practical.
- Fail closed when required policy or tenant context is missing.

Each job command should have separate deployment configuration for CPU, memory, timeout, schedule, retry, and scale settings even when the container image is shared.

## Scaling And Operations

Scaling expectations:

- Product Dashboard scales on browser traffic.
- Product API scales on dashboard and admin API traffic.
- Product Ingestion Endpoint scales on telemetry volume and request size.
- Jobs scale per command based on trigger type, queue depth, schedule, or manual execution needs.

Operational expectations:

- Each Container App has independent health and readiness probes.
- Product Ingestion Endpoint has stricter request-size, rate-limit, and rejection monitoring.
- Product API has stricter authorization, content-review, and audit monitoring.
- Product Dashboard has frontend availability and auth-flow monitoring.
- Each job command emits success, failure, duration, retry count, and affected Customer Organization metrics.

## Future Split Criteria

Split a runtime later only when there is evidence for one of these needs:

- Different release cadence.
- Different scaling profile.
- Different network exposure.
- Different data-access privilege boundary.
- Different runtime dependencies.
- Different SLO or incident ownership.
- Compliance isolation requirement.

Likely future splits:

- Admin API from Product API.
- Content redaction worker image from the shared jobs image.
- Recommendation generation worker image from the shared jobs image.
- Dedicated ingestion regional endpoints for multi-tenant scale or data residency.

## MVP Acceptance Criteria

- The architecture names exactly three long-running Container Apps for the MVP.
- Admin API routes are implemented as Product API routes for MVP.
- Product Ingestion Endpoint is the only public harness telemetry ingestion service.
- Product public traffic reaches Container Apps only through Azure Front Door Premium.
- Direct-origin blocking is deferred to a later origin isolation hardening slice.
- Background processing uses one shared jobs image with explicit commands.
- Job commands are independently configurable even when the image is shared.
- Product Dashboard does not query data stores or telemetry stores directly.
- Product API is the browser-facing product authorization boundary.
- Runtime split preserves the non-punitive and privacy guardrails already defined in product docs.

## Verified Platform Facts

- Azure Container Apps hosts containerized applications and microservices: https://learn.microsoft.com/en-us/azure/container-apps/overview
- Azure Container Apps supports ingress for application traffic: https://learn.microsoft.com/en-us/azure/container-apps/ingress-overview
- Azure Container Apps ingress with `external` is accessible through its FQDN, which is why origin isolation remains deferred hardening work: https://learn.microsoft.com/en-us/azure/container-apps/ingress-overview
- Azure Front Door Premium supports origin isolation options that are deferred from the current deployable path: https://learn.microsoft.com/en-us/azure/frontdoor/private-link
- Azure Container Apps supports Front Door integration patterns that can be reconsidered in the deferred hardening slice: https://learn.microsoft.com/en-us/azure/container-apps/front-door-custom-virtual-network-private-link
- Azure Container Apps supports built-in authentication and authorization for external ingress-enabled apps: https://learn.microsoft.com/en-us/azure/container-apps/authentication
- Azure Container Apps Jobs run finite-duration tasks and support manual, scheduled, and event-driven triggers: https://learn.microsoft.com/en-us/azure/container-apps/jobs
- Azure Container Apps containers support startup command arguments and environment variables: https://learn.microsoft.com/en-us/azure/container-apps/containers
- Azure Container Apps environment variables can use secret references: https://learn.microsoft.com/en-us/azure/container-apps/environment-variables
