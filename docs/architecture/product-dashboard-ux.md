# Product Dashboard UX Architecture

## Purpose

This document defines the Product Dashboard frontend architecture, primary route map, role-specific visibility model, and UX contracts for session investigation, content review, recommendations, and administration.

It closes the frontend gap in the implementation readiness review. Visual design details can evolve later, but implementation issues should not rediscover the dashboard framework or product surface boundaries.

## Source Documents

- [Azure Production MVP PRD](../prd/azure-production-mvp.md)
- [Production Target State Spec](../specs/production-target-state.md)
- [Azure Production Architecture](./azure-production-architecture.md)
- [Runtime Service Topology](./runtime-topology.md)
- [Product API Contract](./product-api-contract.md)
- [Identity And Authorization Architecture](./identity-and-authorization.md)
- [Recommendation Engine Architecture](./recommendation-engine.md)
- [ADR 0003](../adr/0003-use-react-spa-for-production-dashboard.md)

## Frontend Decision

The Product Dashboard is a React SPA built with Vite and TypeScript.

The dashboard is hosted as the Product Dashboard Azure Container App and deployed behind Azure Front Door Premium WAF with a private ACA origin. It calls Product API for all product data, authorization context, session investigation, content review, recommendation, pricing, budget, and audit workflows.

Product API is the only backend contract for the dashboard. The browser must not query PostgreSQL, Blob Storage, Log Analytics, Application Insights, Managed Prometheus, or Managed Grafana data sources directly.

## Frontend Stack Boundary

MVP stack:

- React.
- TypeScript.
- Vite.
- Client-side routing.
- API client generated or typed from Product API contracts.
- Query/cache library for server state.
- Component-level state for local UI behavior only.
- Charting library selected only when the Managed Grafana and Product Dashboard visual contracts are finalized.

Explicitly out of scope for the MVP:

- Blazor dashboard carry-forward.
- Next.js route handlers as a second Product API.
- Browser-direct telemetry store queries.
- Browser-direct captured content store access.
- GraphQL unless a future API decision replaces REST.

## Route Map

| Route | Primary users | Purpose |
| --- | --- | --- |
| `/` | All authenticated users | Redirect to overview or first allowed route |
| `/overview` | All authenticated users | Customer Organization, team, repository, harness, model, token, cost, and hotspot overview |
| `/sessions` | Developer, EngineeringLead, PlatformAdmin | Search and filter visible sessions without people-ranking views |
| `/sessions/:sessionId` | Developer, EngineeringLead, PlatformAdmin, SecurityReviewer where scoped | Session Investigation View |
| `/content-review` | SecurityReviewer, PlatformAdmin | Review `review_required` and `redaction_failed` content references |
| `/recommendations` | Developer, EngineeringLead, PlatformAdmin | Recommendation queue and status across visible scopes |
| `/admin/identity` | PlatformAdmin | Identity tenants and Product Role Mappings |
| `/admin/harness-setup` | PlatformAdmin | Harness setup profiles and Scoped Ingestion Credential lifecycle |
| `/admin/pricing` | PlatformAdmin | Harness Pricing Basis and pricing update review |
| `/admin/budgets` | PlatformAdmin, EngineeringLead where scoped | Non-Punitive Budget Alert policies |
| `/admin/audit` | PlatformAdmin, SecurityReviewer where scoped | Governance Audit Event query |
| `/settings/me` | All authenticated users | Personal session visibility and preferences |

Routes must render a not-authorized state when the user is authenticated but lacks the required product role or scope. They must not hide authorization failures behind empty data states.

## Dashboard Bootstrap

On load, the dashboard calls `GET /api/v1/me`.

The response drives:

- Active Customer Organization context.
- Product roles and scopes.
- Allowed routes.
- Visible teams and repositories.
- Feature flags.
- Content capture and recommendation policy summaries.
- Default dashboard filters.

If tenant context is missing or ambiguous, the dashboard renders a tenant-context error and does not call lower-level product routes.

## Session Investigation View

The Session Investigation View is the primary product workflow.

Required panels:

- Session summary.
- Metric quality markers.
- Token and estimated-cost breakdown.
- Timeline of turns, invocations, tool activity, token observations, and cache evidence.
- Token Hotspots with attribution type and confidence.
- Prompt Cache Breakage diagnostics when evidence exists.
- Content evidence states, not raw content by default.
- Recommendations with rationale, expected benefit, evidence, confidence, and authority state.
- Audit context for content, recommendation, and policy-sensitive actions.

The view must clearly distinguish:

- Observed metrics.
- Estimated metrics.
- Mixed totals.
- Unavailable metrics.
- LLM-inferred candidate hotspots.
- Confirmed Token Hotspots.
- Content not captured.
- Content review required.
- Redaction failed.
- Approved bounded excerpt.

## Role-Specific Visibility

| Role | Default dashboard behavior |
| --- | --- |
| Developer | Own sessions, own recommendations, own setup status, no team-wide people comparison |
| EngineeringLead | Scoped team or repository trends, sessions, hotspots, and recommendations without individual ranking |
| SecurityReviewer | Content review queue, approved excerpts, redaction states, audit context, no broad cost leaderboard |
| PlatformAdmin | Tenant configuration, identity mappings, harness setup, pricing, budgets, audit, and operational views |
| ReadOnlyViewer | Aggregate overview and permitted scoped session metadata, no content review or mutating actions |

The dashboard must avoid product surfaces that rank developers by waste, wrongness, cost, or token burn. Person-scoped search exists for self-view and authorized investigation only.

## Content Evidence UX

Content states are first-class UI states:

- `not_captured`: No content exists because policy or telemetry did not allow capture.
- `redacted_summary`: Redacted and policy-approved summary is available.
- `approved_excerpt`: Privileged reviewer approved a bounded excerpt.
- `review_required`: Metadata is available, content body is blocked pending review.
- `redaction_failed`: Metadata is available, content body was not stored as a Captured Content Blob.
- `discarded`: Reviewer discarded content, audit record remains.

The dashboard must not render raw failed content. Content review decisions call Product API and create Governance Audit Events.

## Recommendation UX

Recommendation cards and detail views show:

- Recommendation type.
- Trigger reason.
- Expected benefit.
- Confidence.
- Evidence links.
- Rule or model version.
- Authority state.
- Regeneration status.
- Visible content excerpt state.

On-demand regeneration is an asynchronous request. The dashboard must show request status rather than blocking on an LLM response.

## Admin UX

MVP administration covers:

- Identity tenant records.
- Product Role Mappings.
- Harness setup profiles.
- Scoped Ingestion Credential issue, rotate, revoke, and metadata list.
- Pricing update review.
- Non-Punitive Budget Alert policies.
- Governance Audit Event search.

Credential secrets are displayed once on creation or rotation. Later views show metadata only.

## Product Dashboard And Managed Grafana

Managed Grafana remains the aggregate observability surface. Product Dashboard remains the product investigation and governance surface.

The dashboard may link to Grafana aggregate panels through the native Azure Managed Grafana endpoint. The first release does not require a `grafana.tokenobs.consultwithcloud.com` vanity hostname.

Grafana may link back to Product Dashboard using aggregate filter parameters only: time range, environment, region, harness, model, model provider, hotspot type, cache-bust category, finding state, signal type, result, and rejection reason.

Grafana links land on `/overview` or `/sessions`. They must not link directly to raw content, evidence packets, content review queues, Blob Storage, telemetry stores, or unauthorized session detail. If a linked view later exposes a session-specific route, Product Dashboard must call Product API and enforce authorization before showing session detail.

## MVP Acceptance Criteria

- The dashboard is implemented as React, TypeScript, and Vite.
- The dashboard is hosted as the Product Dashboard Container App.
- Product API is the only backend contract used by the dashboard.
- `GET /api/v1/me` bootstraps role, scope, feature, and tenant context.
- Session Investigation View covers summary, timeline, metrics, hotspots, cache diagnostics, content states, recommendations, and audit context.
- Content review screens never display raw failed content.
- Recommendation regeneration is asynchronous.
- Dashboard routes enforce role-specific visibility and show explicit not-authorized states.
- The UI does not include people-ranking, individual waste ranking, or blame-oriented views.
- Grafana-originated links are treated as untrusted filters and do not bypass Product API authorization.

## Verified Platform And Framework Facts

- React is a library for building UI from components: https://react.dev/learn/your-first-component
- React documentation recommends using a build tool such as Vite, Parcel, or Rsbuild when building a React app from scratch: https://react.dev/learn/build-a-react-app-from-scratch
- Vite supports project creation from templates for popular frameworks: https://vite.dev/guide/
- Azure Container Apps supports external and internal ingress for containerized apps: https://learn.microsoft.com/azure/container-apps/ingress-overview
- Azure Container Apps supports built-in authentication and authorization for external ingress-enabled apps: https://learn.microsoft.com/azure/container-apps/authentication
