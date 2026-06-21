import { useCallback, useEffect, useMemo, useState } from "react";
import "./App.css";
import { sanitizeGrafanaNavigation } from "./grafanaNavigation";

const defaultProductApiBaseUrl = "/api/v1";

export type ProductRole =
  | "PlatformAdmin"
  | "SecurityReviewer"
  | "EngineeringLead"
  | "Developer"
  | "ReadOnlyViewer";

export type ProductScopeKind =
  | "Organization"
  | "Team"
  | "Repository"
  | "HarnessProfile"
  | "Self"
  | "ContentReviewQueue"
  | "Pricing"
  | "TenantAdmin";

export type ProductScope = {
  kind: ProductScopeKind;
  scopeId: string | null;
};

export type DashboardFeatureFlag =
  | "sessions"
  | "contentReview"
  | "recommendations"
  | "identityAdmin"
  | "harnessSetup"
  | "pricing"
  | "budgets"
  | "audit";

export type DashboardPolicySummaries = {
  contentCapture?: {
    reviewQueueEnabled?: boolean;
    state?: "disabled" | "metadata_only" | "review_required" | "active" | string;
  };
  recommendations?: {
    enabled?: boolean;
    regenerationEnabled?: boolean;
    state?: "disabled" | "deterministic_only" | "active" | string;
  };
};

export type CurrentUser = {
  customerOrganization: {
    slug: string;
    displayName: string;
    dataResidencyRegion: string;
  };
  productUser: {
    displayLabel: string;
    email: string | null;
  };
  roles: ProductRole[];
  scopes: ProductScope[];
  featureFlags?: Partial<Record<DashboardFeatureFlag, boolean>>;
  policySummaries?: DashboardPolicySummaries;
  correlationId: string;
};

type ProblemDetails = {
  title?: string;
  status?: number;
  code?: string;
  correlationId?: string;
};

type BootstrapState =
  | { kind: "loading" }
  | { kind: "ready"; currentUser: CurrentUser }
  | { kind: "unauthenticated"; problem?: ProblemDetails }
  | { kind: "tenant-context"; problem?: ProblemDetails }
  | { kind: "forbidden"; problem?: ProblemDetails }
  | { kind: "error"; message: string; problem?: ProblemDetails };

export type DashboardRoute = {
  path: string;
  label: string;
  group: "investigate" | "govern" | "admin" | "personal";
  purpose: string;
  allowedRoles: "all" | ProductRole[];
  requiredScopeKinds: ProductScopeKind[];
  requiredFeatureFlag?: DashboardFeatureFlag;
  requiredPolicySummary?: "contentCaptureReview" | "recommendationsEnabled";
  showInNavigation?: boolean;
};

type OverviewFilters = {
  from: string;
  to: string;
  environment: string;
  region: string;
  harness: string;
  model: string;
  modelProvider: string;
  hotspotType: string;
  cacheBustCategory: string;
  findingState: string;
  signalType: string;
  result: string;
  rejectionReason: string;
};

type OverviewCostMixItem = {
  providerName: string;
  modelName: string;
  billingRoute: string;
  tokenType: string;
  costStatus: string;
  currency: string;
  estimatedCost: number | null;
  estimateCount: number;
  metricStatus: string;
};

type OverviewResponse = {
  costMix: OverviewCostMixItem[];
};

type PricingBasisItem = {
  pricingBasisId: string;
  harness: string;
  providerName: string;
  modelName: string;
  tokenType: string;
  billingRoute: string;
  currency: string;
  pricePerMillionTokens: number;
  pricingVersion: string;
  sourceKind: string;
  reviewState: string;
  effectiveFromUtc: string;
  effectiveToUtc: string | null;
  auditEventId: string;
  sourceMetadata: Record<string, string>;
};

type PricingBasisResponse = {
  items: PricingBasisItem[];
};

const contentReviewStates = ["review_required", "redaction_failed", "discarded", "approved_excerpt"] as const;
const maxApprovedExcerptUtf8Bytes = 2 * 1024;

export type ContentReviewState = (typeof contentReviewStates)[number];

type ContentReviewBlob = {
  container: string;
  blobName: string;
  blobUri?: string | null;
  blobVersion: string | null;
};

export type ContentReviewItem = {
  contentReferenceId: string;
  customerOrganizationId: string;
  agentSessionId: string | null;
  telemetryEnvelopeId: string;
  contentClass: string;
  captureState: ContentReviewState | string;
  redactionStatus: string;
  contentHash: string | null;
  blob: ContentReviewBlob | null;
  policyVersionId: string;
  redactionPipelineVersion: string | null;
  productRuleVersion: string | null;
  retentionClass: string;
  expiresAtUtc: string | null;
  recommendationEligible: boolean;
  auditEventId: string;
  approvedExcerpt: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
};

type ContentReviewListResponse = {
  items: ContentReviewItem[];
  nextCursor: string | null;
  totalEstimate: number;
};

export type ContentReviewDecision = "retry-redaction" | "discard" | "approve-excerpt" | "mark-recommendation-ineligible";

type ContentReviewDecisionResponse = {
  redactionReviewId: string;
  contentReference: ContentReviewItem | null;
  auditEventId: string;
  decision: string;
  decidedAtUtc: string;
};

type DataState<T> =
  | { kind: "loading" }
  | { kind: "ready"; data: T }
  | { kind: "error"; message: string; problem?: ProblemDetails };

export const dashboardRoutes: DashboardRoute[] = [
  {
    path: "/overview",
    label: "Overview",
    group: "investigate",
    purpose: "Tenant, repository, harness, model, token, cost, and hotspot overview.",
    allowedRoles: "all",
    requiredScopeKinds: ["Organization", "Team", "Repository", "Self"]
  },
  {
    path: "/sessions",
    label: "Sessions",
    group: "investigate",
    purpose: "Role-scoped session search and investigation entry point.",
    allowedRoles: ["Developer", "EngineeringLead", "PlatformAdmin"],
    requiredScopeKinds: ["Organization", "Team", "Repository", "Self"],
    requiredFeatureFlag: "sessions"
  },
  {
    path: "/sessions/:sessionId",
    label: "Session detail",
    group: "investigate",
    purpose: "Session investigation shell for authorized session detail.",
    allowedRoles: ["Developer", "EngineeringLead", "PlatformAdmin", "SecurityReviewer"],
    requiredScopeKinds: ["Organization", "Team", "Repository", "Self", "ContentReviewQueue"],
    requiredFeatureFlag: "sessions",
    showInNavigation: false
  },
  {
    path: "/content-review",
    label: "Content review",
    group: "govern",
    purpose: "Metadata-only content review queue entry point.",
    allowedRoles: ["SecurityReviewer", "PlatformAdmin"],
    requiredScopeKinds: ["ContentReviewQueue"],
    requiredFeatureFlag: "contentReview",
    requiredPolicySummary: "contentCaptureReview"
  },
  {
    path: "/recommendations",
    label: "Recommendations",
    group: "govern",
    purpose: "Visible recommendation queue and status entry point.",
    allowedRoles: ["Developer", "EngineeringLead", "PlatformAdmin"],
    requiredScopeKinds: ["Organization", "Team", "Repository", "Self"],
    requiredFeatureFlag: "recommendations",
    requiredPolicySummary: "recommendationsEnabled"
  },
  {
    path: "/admin/identity",
    label: "Identity",
    group: "admin",
    purpose: "Identity tenants and Product Role Mapping administration.",
    allowedRoles: ["PlatformAdmin"],
    requiredScopeKinds: ["Organization", "TenantAdmin"],
    requiredFeatureFlag: "identityAdmin"
  },
  {
    path: "/admin/harness-setup",
    label: "Harness setup",
    group: "admin",
    purpose: "Harness setup profiles and scoped ingestion credential lifecycle.",
    allowedRoles: ["PlatformAdmin"],
    requiredScopeKinds: ["Organization", "TenantAdmin", "HarnessProfile"],
    requiredFeatureFlag: "harnessSetup"
  },
  {
    path: "/admin/pricing",
    label: "Pricing",
    group: "admin",
    purpose: "Harness Pricing Basis review entry point.",
    allowedRoles: ["PlatformAdmin"],
    requiredScopeKinds: ["Organization", "TenantAdmin", "Pricing"],
    requiredFeatureFlag: "pricing"
  },
  {
    path: "/admin/budgets",
    label: "Budgets",
    group: "admin",
    purpose: "Non-punitive budget alert policy entry point.",
    allowedRoles: ["PlatformAdmin", "EngineeringLead"],
    requiredScopeKinds: ["Organization", "Team", "Repository", "Pricing"],
    requiredFeatureFlag: "budgets"
  },
  {
    path: "/admin/audit",
    label: "Audit",
    group: "admin",
    purpose: "Governance audit event query entry point.",
    allowedRoles: ["PlatformAdmin", "SecurityReviewer"],
    requiredScopeKinds: ["Organization", "TenantAdmin", "ContentReviewQueue", "HarnessProfile"],
    requiredFeatureFlag: "audit"
  },
  {
    path: "/settings/me",
    label: "My settings",
    group: "personal",
    purpose: "Personal session visibility and preferences.",
    allowedRoles: "all",
    requiredScopeKinds: [
      "Organization",
      "Team",
      "Repository",
      "HarnessProfile",
      "Self",
      "ContentReviewQueue",
      "Pricing",
      "TenantAdmin"
    ]
  }
];

const routeGroups: Array<{ id: DashboardRoute["group"]; label: string }> = [
  { id: "investigate", label: "Investigate" },
  { id: "govern", label: "Govern" },
  { id: "admin", label: "Admin" },
  { id: "personal", label: "Personal" }
];

export function App() {
  const [bootstrapState, setBootstrapState] = useState<BootstrapState>({ kind: "loading" });
  const [currentPath, setCurrentPath] = useBrowserPath();
  const productApiBaseUrl = useMemo(readProductApiBaseUrl, []);

  useEffect(() => {
    let isActive = true;

    async function loadCurrentUser() {
      try {
        const response = await fetch(`${productApiBaseUrl}/me`, {
          credentials: "include",
          headers: {
            Accept: "application/json"
          }
        });
        const problem = response.ok ? undefined : await readProblemDetails(response);

        if (!isActive) {
          return;
        }

        if (response.ok) {
          const currentUser = (await response.json()) as CurrentUser;
          setBootstrapState({ kind: "ready", currentUser });
          return;
        }

        if (response.status === 401) {
          setBootstrapState({ kind: "unauthenticated", problem });
          return;
        }

        if (problem?.code === "tenant_context_required" || problem?.code === "tenant_context_ambiguous") {
          setBootstrapState({ kind: "tenant-context", problem });
          return;
        }

        if (response.status === 403) {
          setBootstrapState({ kind: "forbidden", problem });
          return;
        }

        setBootstrapState({
          kind: "error",
          message: "Product API did not return dashboard context.",
          problem
        });
      } catch (error) {
        if (isActive) {
          setBootstrapState({
            kind: "error",
            message: error instanceof Error ? error.message : "Product API context request failed."
          });
        }
      }
    }

    loadCurrentUser();

    return () => {
      isActive = false;
    };
  }, [productApiBaseUrl]);

  const currentUser = bootstrapState.kind === "ready" ? bootstrapState.currentUser : undefined;
  const visibleRoutes = useMemo(
    () => (currentUser ? dashboardRoutes.filter((route) => isRouteVisible(route, currentUser)) : []),
    [currentUser]
  );
  const navigationRoutes = useMemo(
    () => visibleRoutes.filter((route) => route.showInNavigation !== false),
    [visibleRoutes]
  );
  const activeRoute = useMemo(() => resolveRoute(currentPath, dashboardRoutes), [currentPath]);

  useEffect(() => {
    if (bootstrapState.kind !== "ready" || currentPath !== "/") {
      return;
    }

    const nextRoute = navigationRoutes[0]?.path ?? "/overview";
    setCurrentPath(toConcreteRoute(nextRoute), { replace: true });
  }, [bootstrapState.kind, currentPath, navigationRoutes, setCurrentPath]);

  if (bootstrapState.kind === "loading") {
    return <StatusSurface title="Loading dashboard context" detail="Requesting authorized Product Dashboard context." />;
  }

  if (bootstrapState.kind === "unauthenticated") {
    return (
      <StatusSurface
        title="Unauthorized"
        detail="Sign in through the configured product identity flow before opening the dashboard."
        problem={bootstrapState.problem}
      />
    );
  }

  if (bootstrapState.kind === "tenant-context") {
    return (
      <StatusSurface
        title="Tenant context required"
        detail="The dashboard cannot choose a Customer Organization without an explicit Product API context."
        problem={bootstrapState.problem}
      />
    );
  }

  if (bootstrapState.kind === "forbidden") {
    return (
      <StatusSurface
        title="Not authorized"
        detail="Product Role Mapping did not allow dashboard bootstrap for this user."
        problem={bootstrapState.problem}
      />
    );
  }

  if (bootstrapState.kind === "error") {
    return <StatusSurface title="Dashboard unavailable" detail={bootstrapState.message} problem={bootstrapState.problem} />;
  }

  if (visibleRoutes.length === 0) {
    return (
      <StatusSurface
        title="No routes available"
        detail="The resolved Product Role Mapping did not expose any Product Dashboard routes."
      />
    );
  }

  const isAuthorizedRoute = activeRoute ? isRouteVisible(activeRoute, bootstrapState.currentUser) : false;

  return (
    <div className="dashboard-app">
      <aside className="dashboard-sidebar" aria-label="Product Dashboard navigation">
        <div className="brand-block">
          <span className="brand-mark" aria-hidden="true">
            TO
          </span>
          <div>
            <p className="eyebrow">Product Dashboard</p>
            <h1>AI Agent Token Observability</h1>
          </div>
        </div>
        <nav className="route-nav">
          {routeGroups.map((group) => {
            const groupRoutes = navigationRoutes.filter((route) => route.group === group.id);

            if (groupRoutes.length === 0) {
              return null;
            }

            return (
              <section className="nav-group" key={group.id}>
                <h2>{group.label}</h2>
                <ul>
                  {groupRoutes.map((route) => (
                    <li key={route.path}>
                      <a
                        aria-current={activeRoute?.path === route.path ? "page" : undefined}
                        href={toConcreteRoute(route.path)}
                        onClick={(event) => {
                          event.preventDefault();
                          setCurrentPath(toConcreteRoute(route.path));
                        }}
                      >
                        {route.label}
                      </a>
                    </li>
                  ))}
                </ul>
              </section>
            );
          })}
        </nav>
      </aside>
      <main className="dashboard-main">
        <header className="dashboard-header">
          <div>
            <p className="eyebrow">{bootstrapState.currentUser.customerOrganization.displayName}</p>
            <h2>{activeRoute?.label ?? "Route not found"}</h2>
          </div>
          <dl className="context-list" aria-label="Active dashboard context">
            <div>
              <dt>Tenant</dt>
              <dd>{bootstrapState.currentUser.customerOrganization.slug}</dd>
            </div>
            <div>
              <dt>Region</dt>
              <dd>{bootstrapState.currentUser.customerOrganization.dataResidencyRegion}</dd>
            </div>
            <div>
              <dt>User</dt>
              <dd>{bootstrapState.currentUser.productUser.displayLabel}</dd>
            </div>
          </dl>
        </header>

        {!activeRoute ? (
          <RouteState
            title="Route not found"
            detail="This Product Dashboard route is not part of the production route map."
          />
        ) : !isAuthorizedRoute ? (
          <RouteState
            title="Not authorized"
            detail="This route is outside the current Product Role Mapping."
            route={activeRoute}
          />
        ) : activeRoute.path === "/overview" ? (
          <OverviewShell
            currentPath={currentPath}
            productApiBaseUrl={productApiBaseUrl}
            setCurrentPath={setCurrentPath}
          />
        ) : activeRoute.path === "/content-review" ? (
          <ContentReviewShell currentUser={bootstrapState.currentUser} productApiBaseUrl={productApiBaseUrl} />
        ) : activeRoute.path === "/admin/pricing" ? (
          <PricingShell productApiBaseUrl={productApiBaseUrl} />
        ) : (
          <RouteState title={activeRoute.label} detail={activeRoute.purpose} route={activeRoute} />
        )}
      </main>
    </div>
  );
}

function OverviewShell({
  currentPath,
  productApiBaseUrl,
  setCurrentPath
}: {
  currentPath: string;
  productApiBaseUrl: string;
  setCurrentPath: (path: string, options?: { replace?: boolean }) => void;
}) {
  const [filters, setFilters] = useState<OverviewFilters>(() => readOverviewFilters());
  const [overviewState, setOverviewState] = useState<DataState<OverviewResponse>>({ kind: "loading" });

  useEffect(() => {
    setFilters(readOverviewFilters());
  }, [currentPath]);

  useEffect(() => {
    let isActive = true;

    async function loadOverview() {
      setOverviewState({ kind: "loading" });
      try {
        const response = await fetch(`${productApiBaseUrl}/overview${window.location.search}`, {
          credentials: "include",
          headers: { Accept: "application/json" }
        });
        const problem = response.ok ? undefined : await readProblemDetails(response);

        if (!isActive) {
          return;
        }

        if (!response.ok) {
          setOverviewState({
            kind: "error",
            message: problem?.title ?? "Overview data request failed.",
            problem
          });
          return;
        }

        setOverviewState({ kind: "ready", data: (await response.json()) as OverviewResponse });
      } catch (error) {
        if (isActive) {
          setOverviewState({
            kind: "error",
            message: error instanceof Error ? error.message : "Overview data request failed."
          });
        }
      }
    }

    loadOverview();

    return () => {
      isActive = false;
    };
  }, [currentPath, productApiBaseUrl]);

  return (
    <section className="route-surface" aria-labelledby="overview-title">
      <div className="route-heading">
        <div>
          <p className="eyebrow">Overview</p>
          <h3 id="overview-title">Tenant-aware query state</h3>
        </div>
        <span className="state-chip">{overviewState.kind === "ready" ? "Product API data loaded" : "Loading"}</span>
      </div>
      <form
        className="filter-grid"
        onSubmit={(event) => {
          event.preventDefault();
          setCurrentPath(`/overview${toOverviewQuery(filters)}`);
        }}
      >
        <label>
          <span>From</span>
          <input
            type="date"
            value={filters.from}
            onChange={(event) => setFilters({ ...filters, from: event.target.value })}
          />
        </label>
        <label>
          <span>To</span>
          <input
            type="date"
            value={filters.to}
            onChange={(event) => setFilters({ ...filters, to: event.target.value })}
          />
        </label>
        <label>
          <span>Environment</span>
          <input
            value={filters.environment}
            onChange={(event) => setFilters({ ...filters, environment: event.target.value })}
            placeholder="dv"
          />
        </label>
        <label>
          <span>Region</span>
          <input
            value={filters.region}
            onChange={(event) => setFilters({ ...filters, region: event.target.value })}
            placeholder="eastus2"
          />
        </label>
        <label>
          <span>Harness</span>
          <select
            value={filters.harness}
            onChange={(event) => setFilters({ ...filters, harness: event.target.value })}
          >
            <option value="">Any authorized harness</option>
            <option value="codex">Codex</option>
          </select>
        </label>
        <label>
          <span>Model</span>
          <input
            value={filters.model}
            onChange={(event) => setFilters({ ...filters, model: event.target.value })}
            placeholder="Model alias"
          />
        </label>
        <label>
          <span>Model provider</span>
          <input
            value={filters.modelProvider}
            onChange={(event) => setFilters({ ...filters, modelProvider: event.target.value })}
            placeholder="openai"
          />
        </label>
        <label>
          <span>Finding state</span>
          <input
            value={filters.findingState}
            onChange={(event) => setFilters({ ...filters, findingState: event.target.value })}
            placeholder="confirmed"
          />
        </label>
        <button type="submit">Apply filters</button>
      </form>
      {overviewState.kind === "loading" ? (
        <div className="empty-state">
          <h4>Loading aggregate cost mix</h4>
          <p>The dashboard is requesting authorized aggregate cost data.</p>
        </div>
      ) : overviewState.kind === "error" ? (
        <InlineProblem title="Overview unavailable" message={overviewState.message} problem={overviewState.problem} />
      ) : overviewState.data.costMix.length === 0 ? (
        <div className="empty-state">
          <h4>No aggregate cost mix yet</h4>
          <p>Cost buckets appear after pricing basis and cost estimates are available.</p>
        </div>
      ) : (
        <CostMixTable items={overviewState.data.costMix} />
      )}
    </section>
  );
}

function CostMixTable({ items }: { items: OverviewCostMixItem[] }) {
  return (
    <div className="table-wrap">
      <table>
        <thead>
          <tr>
            <th>Provider</th>
            <th>Model</th>
            <th>Token type</th>
            <th>Billing route</th>
            <th>Cost status</th>
            <th>Estimated cost</th>
            <th>Metric state</th>
          </tr>
        </thead>
        <tbody>
          {items.map((item) => (
            <tr key={`${item.providerName}:${item.modelName}:${item.tokenType}:${item.billingRoute}:${item.costStatus}`}>
              <td>{item.providerName}</td>
              <td>{item.modelName}</td>
              <td>{formatMachineText(item.tokenType)}</td>
              <td>{formatMachineText(item.billingRoute)}</td>
              <td>{formatMachineText(item.costStatus)}</td>
              <td>{formatMoney(item.estimatedCost, item.currency)}</td>
              <td>{formatMachineText(item.metricStatus)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function PricingShell({ productApiBaseUrl }: { productApiBaseUrl: string }) {
  const [pricingState, setPricingState] = useState<DataState<PricingBasisResponse>>({ kind: "loading" });

  const loadPricing = useCallback(async () => {
    setPricingState({ kind: "loading" });
    try {
      const response = await fetch(`${productApiBaseUrl}/pricing/basis`, {
        credentials: "include",
        headers: { Accept: "application/json" }
      });
      const problem = response.ok ? undefined : await readProblemDetails(response);

      if (!response.ok) {
        setPricingState({
          kind: "error",
          message: problem?.title ?? "Pricing basis request failed.",
          problem
        });
        return;
      }

      setPricingState({ kind: "ready", data: (await response.json()) as PricingBasisResponse });
    } catch (error) {
      setPricingState({
        kind: "error",
        message: error instanceof Error ? error.message : "Pricing basis request failed."
      });
    }
  }, [productApiBaseUrl]);

  useEffect(() => {
    loadPricing();
  }, [loadPricing]);

  async function reviewPricing(pricingBasisId: string, decision: "approve" | "reject") {
    const response = await fetch(`${productApiBaseUrl}/pricing/basis/${pricingBasisId}/${decision}`, {
      method: "POST",
      credentials: "include",
      headers: {
        Accept: "application/json",
        "Content-Type": "application/json",
        "Idempotency-Key": `${decision}-${pricingBasisId}-${Date.now()}`
      },
      body: JSON.stringify({ decisionReason: "dashboard_review" })
    });

    if (!response.ok) {
      const problem = await readProblemDetails(response);
      setPricingState({
        kind: "error",
        message: problem?.title ?? "Pricing review request failed.",
        problem
      });
      return;
    }

    await loadPricing();
  }

  return (
    <section className="route-surface" aria-labelledby="pricing-title">
      <div className="route-heading">
        <div>
          <p className="eyebrow">Admin</p>
          <h3 id="pricing-title">Harness Pricing Basis</h3>
        </div>
        <span className="state-chip">{pricingState.kind === "ready" ? "Review queue loaded" : "Loading"}</span>
      </div>
      {pricingState.kind === "loading" ? (
        <div className="empty-state">
          <h4>Loading pricing basis</h4>
          <p>Provider seed candidates and customer overrides are loading from Product API.</p>
        </div>
      ) : pricingState.kind === "error" ? (
        <InlineProblem title="Pricing unavailable" message={pricingState.message} problem={pricingState.problem} />
      ) : pricingState.data.items.length === 0 ? (
        <div className="empty-state">
          <h4>No pricing basis records</h4>
          <p>Provider refresh jobs create candidate records for review.</p>
        </div>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Provider</th>
                <th>Model</th>
                <th>Token type</th>
                <th>Route</th>
                <th>Rate</th>
                <th>Source</th>
                <th>State</th>
                <th>Audit</th>
                <th>Review</th>
              </tr>
            </thead>
            <tbody>
              {pricingState.data.items.map((item) => (
                <tr key={item.pricingBasisId}>
                  <td>{item.providerName}</td>
                  <td>{item.modelName}</td>
                  <td>{formatMachineText(item.tokenType)}</td>
                  <td>{formatMachineText(item.billingRoute)}</td>
                  <td>{formatMoney(item.pricePerMillionTokens, item.currency)} / 1M</td>
                  <td>
                    {formatMachineText(item.sourceKind)}
                    <small>{item.sourceMetadata.source_url ?? item.sourceMetadata.provider_sku_name ?? "metadata recorded"}</small>
                  </td>
                  <td>{formatMachineText(item.reviewState)}</td>
                  <td>{item.auditEventId}</td>
                  <td>
                    {item.reviewState === "candidate" ? (
                      <div className="button-row">
                        <button type="button" onClick={() => reviewPricing(item.pricingBasisId, "approve")}>
                          Approve
                        </button>
                        <button type="button" className="secondary-button" onClick={() => reviewPricing(item.pricingBasisId, "reject")}>
                          Reject
                        </button>
                      </div>
                    ) : (
                      <span className="muted-text">Closed</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

function ContentReviewShell({
  currentUser,
  productApiBaseUrl
}: {
  currentUser: CurrentUser;
  productApiBaseUrl: string;
}) {
  const [selectedState, setSelectedState] = useState<ContentReviewState>("review_required");
  const [selectedContentReferenceId, setSelectedContentReferenceId] = useState<string | null>(null);
  const [listState, setListState] = useState<DataState<ContentReviewListResponse>>({ kind: "loading" });
  const [detailState, setDetailState] = useState<DataState<ContentReviewItem> | { kind: "idle" }>({ kind: "idle" });
  const [decisionState, setDecisionState] = useState<
    | { kind: "idle" }
    | { kind: "submitting"; decision: ContentReviewDecision }
    | { kind: "success"; data: ContentReviewDecisionResponse }
    | { kind: "error"; decision: ContentReviewDecision; message: string; problem?: ProblemDetails }
  >({ kind: "idle" });
  const [decisionReason, setDecisionReason] = useState("dashboard_review");
  const [approvedExcerpt, setApprovedExcerpt] = useState("");

  const loadItems = useCallback(async () => {
    setListState({ kind: "loading" });
    try {
      const data = await fetchContentReviewItems(productApiBaseUrl, selectedState);
      setListState({ kind: "ready", data });
      setSelectedContentReferenceId((current) => {
        if (current && data.items.some((item) => item.contentReferenceId === current)) {
          return current;
        }

        return data.items[0]?.contentReferenceId ?? null;
      });
    } catch (error) {
      const problem = getProblemDetails(error);
      setListState({
        kind: "error",
        message: problem?.title ?? getErrorMessage(error, "Content review queue request failed."),
        problem
      });
      setSelectedContentReferenceId(null);
    }
  }, [productApiBaseUrl, selectedState]);

  useEffect(() => {
    setDecisionState({ kind: "idle" });
    loadItems();
  }, [loadItems]);

  const loadDetail = useCallback(
    async (contentReferenceId: string) => {
      setDetailState({ kind: "loading" });
      try {
        const item = await fetchContentReviewItem(productApiBaseUrl, contentReferenceId);
        setDetailState({ kind: "ready", data: item });
        setApprovedExcerpt(item.approvedExcerpt ?? "");
      } catch (error) {
        const problem = getProblemDetails(error);
        setDetailState({
          kind: "error",
          message: problem?.title ?? getErrorMessage(error, "Content review detail request failed."),
          problem
        });
      }
    },
    [productApiBaseUrl]
  );

  useEffect(() => {
    if (!selectedContentReferenceId) {
      setDetailState({ kind: "idle" });
      return;
    }

    loadDetail(selectedContentReferenceId);
  }, [loadDetail, selectedContentReferenceId]);

  const detailItem = detailState.kind === "ready" ? detailState.data : null;
  const excerptBytes = getUtf8ByteCount(approvedExcerpt);
  const isReviewable = detailItem ? isReviewableContentState(detailItem.captureState) : false;
  const policyAllowsDecisions = policySummaryAllowsRoute("contentCaptureReview", currentUser.policySummaries);
  const approveExcerptBlocked = !isReviewable || !policyAllowsDecisions || excerptBytes > maxApprovedExcerptUtf8Bytes || approvedExcerpt.trim().length === 0;

  async function submitDecision(decision: ContentReviewDecision) {
    if (!detailItem) {
      return;
    }

    setDecisionState({ kind: "submitting", decision });
    try {
      const data = await submitContentReviewDecision(productApiBaseUrl, detailItem.contentReferenceId, decision, {
        decisionReason,
        approvedExcerpt: decision === "approve-excerpt" ? approvedExcerpt : undefined
      });
      setDecisionState({ kind: "success", data });
      if (data.contentReference) {
        setDetailState({ kind: "ready", data: data.contentReference });
      } else {
        await loadDetail(detailItem.contentReferenceId);
      }
      await loadItems();
    } catch (error) {
      const problem = getProblemDetails(error);
      setDecisionState({
        kind: "error",
        decision,
        message: problem?.title ?? getErrorMessage(error, "Content review decision failed."),
        problem
      });
    }
  }

  return (
    <section className="route-surface" aria-labelledby="content-review-title">
      <div className="route-heading">
        <div>
          <p className="eyebrow">Governance</p>
          <h3 id="content-review-title">Content Review Queue</h3>
        </div>
        <span className="state-chip">{listState.kind === "ready" ? `${listState.data.totalEstimate} items` : "Loading"}</span>
      </div>

      <div className="segmented-control" aria-label="Content review state filter">
        {contentReviewStates.map((state) => (
          <button
            type="button"
            key={state}
            aria-pressed={selectedState === state}
            onClick={() => {
              setSelectedState(state);
              setSelectedContentReferenceId(null);
            }}
          >
            {formatMachineText(state)}
          </button>
        ))}
      </div>

      <div className="content-review-layout">
        <div className="content-review-list" aria-label="Content review items">
          {listState.kind === "loading" ? (
            <div className="empty-state">
              <h4>Loading review queue</h4>
              <p>Product API is returning metadata-only content review items.</p>
            </div>
          ) : listState.kind === "error" ? (
            <InlineProblem title="Content review unavailable" message={listState.message} problem={listState.problem} />
          ) : listState.data.items.length === 0 ? (
            <div className="empty-state">
              <h4>No {formatMachineText(selectedState)} items</h4>
              <p>The selected content review state has no visible metadata records.</p>
            </div>
          ) : (
            <ul>
              {listState.data.items.map((item) => (
                <li key={item.contentReferenceId}>
                  <button
                    type="button"
                    aria-current={selectedContentReferenceId === item.contentReferenceId ? "true" : undefined}
                    onClick={() => {
                      setDecisionState({ kind: "idle" });
                      setSelectedContentReferenceId(item.contentReferenceId);
                    }}
                  >
                    <strong>{formatMachineText(item.captureState)}</strong>
                    <span>{item.contentReferenceId}</span>
                    <small>{item.telemetryEnvelopeId}</small>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>

        <div className="content-review-detail">
          {detailState.kind === "idle" ? (
            <div className="empty-state">
              <h4>Select a review item</h4>
              <p>Only metadata and previously approved excerpts are shown.</p>
            </div>
          ) : detailState.kind === "loading" ? (
            <div className="empty-state">
              <h4>Loading item detail</h4>
              <p>Product API is returning metadata for the selected content reference.</p>
            </div>
          ) : detailState.kind === "error" ? (
            <InlineProblem title="Content reference unavailable" message={detailState.message} problem={detailState.problem} />
          ) : (
            <>
              <ContentReviewMetadata item={detailState.data} />
              {decisionState.kind === "success" ? <ContentReviewDecisionStatus response={decisionState.data} /> : null}
              {decisionState.kind === "error" ? (
                <InlineProblem title="Content review decision failed" message={decisionState.message} problem={decisionState.problem} />
              ) : null}
              <div className="decision-panel" aria-label="Content review decisions">
                <label>
                  <span>Decision reason</span>
                  <input value={decisionReason} onChange={(event) => setDecisionReason(event.target.value)} />
                </label>
                <div className="button-row">
                  <button
                    type="button"
                    disabled={!isReviewable || decisionState.kind === "submitting"}
                    onClick={() => submitDecision("retry-redaction")}
                  >
                    Retry redaction
                  </button>
                  <button
                    type="button"
                    className="secondary-button"
                    disabled={!isReviewable || decisionState.kind === "submitting"}
                    onClick={() => submitDecision("discard")}
                  >
                    Discard
                  </button>
                  <button
                    type="button"
                    className="secondary-button"
                    disabled={!isReviewable || decisionState.kind === "submitting"}
                    onClick={() => submitDecision("mark-recommendation-ineligible")}
                  >
                    Mark recommendation ineligible
                  </button>
                </div>
                <label>
                  <span>Approved excerpt</span>
                  <textarea
                    value={approvedExcerpt}
                    onChange={(event) => setApprovedExcerpt(event.target.value)}
                    maxLength={maxApprovedExcerptUtf8Bytes}
                  />
                </label>
                <p className={excerptBytes > maxApprovedExcerptUtf8Bytes ? "limit-warning" : "muted-text"}>
                  {excerptBytes} of {maxApprovedExcerptUtf8Bytes} UTF-8 bytes
                </p>
                <button
                  type="button"
                  disabled={approveExcerptBlocked || decisionState.kind === "submitting"}
                  onClick={() => submitDecision("approve-excerpt")}
                >
                  Approve excerpt
                </button>
                {!policyAllowsDecisions ? (
                  <p className="limit-warning">Content Capture Policy does not currently allow review decisions.</p>
                ) : null}
              </div>
            </>
          )}
        </div>
      </div>
    </section>
  );
}

function ContentReviewMetadata({ item }: { item: ContentReviewItem }) {
  return (
    <div className="metadata-stack">
      <dl className="detail-grid" aria-label="Content reference metadata">
        <DetailItem label="Content reference" value={item.contentReferenceId} />
        <DetailItem label="Session" value={item.agentSessionId ?? "Not linked"} />
        <DetailItem label="Telemetry envelope" value={item.telemetryEnvelopeId} />
        <DetailItem label="Content class" value={formatMachineText(item.contentClass)} />
        <DetailItem label="Capture state" value={formatMachineText(item.captureState)} />
        <DetailItem label="Redaction status" value={formatMachineText(item.redactionStatus)} />
        <DetailItem label="Policy version" value={item.policyVersionId} />
        <DetailItem label="Pipeline version" value={item.redactionPipelineVersion ?? "Unavailable"} />
        <DetailItem label="Product rule version" value={item.productRuleVersion ?? "Unavailable"} />
        <DetailItem label="Retention class" value={formatMachineText(item.retentionClass)} />
        <DetailItem label="Expires" value={item.expiresAtUtc ?? "Not scheduled"} />
        <DetailItem label="Recommendation eligible" value={item.recommendationEligible ? "Yes" : "No"} />
        <DetailItem label="Audit event" value={item.auditEventId} />
        <DetailItem label="Created" value={item.createdAtUtc} />
        <DetailItem label="Updated" value={item.updatedAtUtc} />
      </dl>
      {item.blob ? (
        <dl className="detail-grid" aria-label="Captured blob metadata">
          <DetailItem label="Blob container" value={item.blob.container} />
          <DetailItem label="Blob name" value={item.blob.blobName} />
          <DetailItem label="Blob version" value={item.blob.blobVersion ?? "Unavailable"} />
        </dl>
      ) : null}
      {item.approvedExcerpt ? (
        <div className="approved-excerpt">
          <h4>Approved excerpt</h4>
          <p>{item.approvedExcerpt}</p>
        </div>
      ) : (
        <div className="empty-state">
          <h4>No approved excerpt</h4>
          <p>Raw failed content is not rendered in this workflow.</p>
        </div>
      )}
    </div>
  );
}

function DetailItem({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  );
}

function ContentReviewDecisionStatus({ response }: { response: ContentReviewDecisionResponse }) {
  return (
    <dl className="detail-grid decision-status" aria-label="Decision audit status">
      <DetailItem label="Decision" value={formatMachineText(response.decision)} />
      <DetailItem label="Audit event" value={response.auditEventId} />
      <DetailItem label="Reviewed" value={response.decidedAtUtc} />
      <DetailItem
        label="Resulting state"
        value={response.contentReference ? formatMachineText(response.contentReference.captureState) : "Reference unavailable"}
      />
    </dl>
  );
}

function InlineProblem({
  title,
  message,
  problem
}: {
  title: string;
  message: string;
  problem?: ProblemDetails;
}) {
  return (
    <div className="empty-state" role="alert">
      <h4>{title}</h4>
      <p>{message}</p>
      {problem?.status ? <p>Status: {problem.status}</p> : null}
      {problem?.code ? <p>Code: {problem.code}</p> : null}
      {problem?.correlationId ? <p>Correlation ID: {problem.correlationId}</p> : null}
    </div>
  );
}

function formatMachineText(value: string) {
  return value.replaceAll("_", " ");
}

function formatMoney(value: number | null, currency: string) {
  if (value === null) {
    return "Unavailable";
  }

  return new Intl.NumberFormat(undefined, {
    style: "currency",
    currency,
    maximumFractionDigits: 6
  }).format(value);
}

function RouteState({ title, detail, route }: { title: string; detail: string; route?: DashboardRoute }) {
  return (
    <section className="route-surface" aria-labelledby="route-state-title">
      <div className="route-heading">
        <div>
          <p className="eyebrow">{route?.group ?? "Dashboard"}</p>
          <h3 id="route-state-title">{title}</h3>
        </div>
        <span className="state-chip">Shell ready</span>
      </div>
      <div className="empty-state">
        <h4>{route?.path ?? "No route data"}</h4>
        <p>{detail}</p>
      </div>
    </section>
  );
}

function StatusSurface({ title, detail, problem }: { title: string; detail: string; problem?: ProblemDetails }) {
  return (
    <main className="status-shell">
      <section className="status-panel" aria-live="polite">
        <p className="eyebrow">Product Dashboard</p>
        <h1>{title}</h1>
        <p>{detail}</p>
        {problem?.correlationId ? (
          <dl className="status-metadata">
            <div>
              <dt>Correlation ID</dt>
              <dd>{problem.correlationId}</dd>
            </div>
          </dl>
        ) : null}
      </section>
    </main>
  );
}

class ProductApiProblemError extends Error {
  constructor(public readonly problem?: ProblemDetails) {
    super(problem?.title ?? "Product API request failed.");
  }
}

export async function fetchContentReviewItems(
  productApiBaseUrl: string,
  state: ContentReviewState,
  fetchImpl: typeof fetch = fetch
): Promise<ContentReviewListResponse> {
  const response = await fetchImpl(`${productApiBaseUrl}/content-review/items?state=${encodeURIComponent(state)}`, {
    credentials: "include",
    headers: { Accept: "application/json" }
  });

  if (!response.ok) {
    throw new ProductApiProblemError(await readProblemDetails(response));
  }

  const data = (await response.json()) as ContentReviewListResponse;

  return {
    ...data,
    items: data.items.map(sanitizeContentReviewItem)
  };
}

export async function fetchContentReviewItem(
  productApiBaseUrl: string,
  contentReferenceId: string,
  fetchImpl: typeof fetch = fetch
): Promise<ContentReviewItem> {
  const response = await fetchImpl(
    `${productApiBaseUrl}/content-review/items/${encodeURIComponent(contentReferenceId)}`,
    {
      credentials: "include",
      headers: { Accept: "application/json" }
    }
  );

  if (!response.ok) {
    throw new ProductApiProblemError(await readProblemDetails(response));
  }

  return sanitizeContentReviewItem((await response.json()) as ContentReviewItem);
}

export async function submitContentReviewDecision(
  productApiBaseUrl: string,
  contentReferenceId: string,
  decision: ContentReviewDecision,
  body: { decisionReason: string; approvedExcerpt?: string },
  fetchImpl: typeof fetch = fetch
): Promise<ContentReviewDecisionResponse> {
  if (decision === "approve-excerpt" && getUtf8ByteCount(body.approvedExcerpt ?? "") > maxApprovedExcerptUtf8Bytes) {
    throw new ProductApiProblemError({
      title: "Approved excerpt exceeds the bounded excerpt size limit.",
      status: 400,
      code: "validation_failed"
    });
  }

  const requestBody =
    decision === "approve-excerpt"
      ? { decisionReason: body.decisionReason, approvedExcerpt: body.approvedExcerpt ?? "" }
      : { decisionReason: body.decisionReason };
  const response = await fetchImpl(
    `${productApiBaseUrl}/content-review/items/${encodeURIComponent(contentReferenceId)}/${decision}`,
    {
      method: "POST",
      credentials: "include",
      headers: {
        Accept: "application/json",
        "Content-Type": "application/json",
        "Idempotency-Key": createIdempotencyKey(decision, contentReferenceId)
      },
      body: JSON.stringify(requestBody)
    }
  );

  if (!response.ok) {
    throw new ProductApiProblemError(await readProblemDetails(response));
  }

  const data = (await response.json()) as ContentReviewDecisionResponse;

  return {
    ...data,
    contentReference: data.contentReference ? sanitizeContentReviewItem(data.contentReference) : null
  };
}

export function isReviewableContentState(captureState: string) {
  return captureState === "review_required" || captureState === "redaction_failed";
}

export function sanitizeContentReviewItem(item: ContentReviewItem): ContentReviewItem {
  return {
    contentReferenceId: item.contentReferenceId,
    customerOrganizationId: item.customerOrganizationId,
    agentSessionId: item.agentSessionId,
    telemetryEnvelopeId: item.telemetryEnvelopeId,
    contentClass: item.contentClass,
    captureState: item.captureState,
    redactionStatus: item.redactionStatus,
    contentHash: item.contentHash,
    blob: item.blob
      ? {
          container: item.blob.container,
          blobName: item.blob.blobName,
          blobVersion: item.blob.blobVersion
        }
      : null,
    policyVersionId: item.policyVersionId,
    redactionPipelineVersion: item.redactionPipelineVersion,
    productRuleVersion: item.productRuleVersion,
    retentionClass: item.retentionClass,
    expiresAtUtc: item.expiresAtUtc,
    recommendationEligible: item.recommendationEligible,
    auditEventId: item.auditEventId,
    approvedExcerpt: item.approvedExcerpt,
    createdAtUtc: item.createdAtUtc,
    updatedAtUtc: item.updatedAtUtc
  };
}

export function getUtf8ByteCount(value: string) {
  return new TextEncoder().encode(value).byteLength;
}

function createIdempotencyKey(decision: ContentReviewDecision, contentReferenceId: string) {
  const randomSuffix = globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random()}`;

  return `${decision}-${contentReferenceId}-${randomSuffix}`;
}

function getProblemDetails(error: unknown) {
  return error instanceof ProductApiProblemError ? error.problem : undefined;
}

function getErrorMessage(error: unknown, fallback: string) {
  return error instanceof Error ? error.message : fallback;
}

function useBrowserPath(): [string, (path: string, options?: { replace?: boolean }) => void] {
  const readPath = useCallback(() => sanitizeGrafanaNavigation(window.location.pathname, window.location.search), []);
  const [path, setPath] = useState(() => {
    const sanitizedPath = readPath();
    const currentPath = `${window.location.pathname}${window.location.search}`;

    if (sanitizedPath !== currentPath) {
      window.history.replaceState(null, "", sanitizedPath);
    }

    return sanitizedPath;
  });

  useEffect(() => {
    const onPopState = () => {
      const sanitizedPath = readPath();
      const currentPath = `${window.location.pathname}${window.location.search}`;

      if (sanitizedPath !== currentPath) {
        window.history.replaceState(null, "", sanitizedPath);
      }

      setPath(sanitizedPath);
    };
    window.addEventListener("popstate", onPopState);

    return () => {
      window.removeEventListener("popstate", onPopState);
    };
  }, [readPath]);

  const navigate = useCallback(
    (nextPath: string, options?: { replace?: boolean }) => {
      const [nextPathname, nextSearch = ""] = nextPath.split("?");
      const sanitizedPath = sanitizeGrafanaNavigation(nextPathname || "/", nextSearch ? `?${nextSearch}` : "");

      if (options?.replace) {
        window.history.replaceState(null, "", sanitizedPath);
      } else {
        window.history.pushState(null, "", sanitizedPath);
      }

      setPath(readPath());
    },
    [readPath]
  );

  return [path, navigate];
}

function readProductApiBaseUrl() {
  const configured = window.__TOKENOBSERVABILITY_CONFIG__?.productApiBaseUrl?.trim();

  if (!configured) {
    return defaultProductApiBaseUrl;
  }

  const normalized = configured.replace(/\/+$/, "");

  return normalized.endsWith("/api/v1") ? normalized : `${normalized}/api/v1`;
}

async function readProblemDetails(response: Response): Promise<ProblemDetails | undefined> {
  const contentType = response.headers.get("content-type") ?? "";

  if (!contentType.includes("application/json")) {
    return undefined;
  }

  try {
    return (await response.json()) as ProblemDetails;
  } catch {
    return undefined;
  }
}

export function resolveRoute(path: string, routes: DashboardRoute[]) {
  const pathname = path.split("?")[0] || "/";

  if (pathname === "/") {
    return routes.find((route) => route.path === "/overview");
  }

  return routes.find((route) => route.path === pathname) ?? matchSessionDetail(pathname, routes);
}

function matchSessionDetail(pathname: string, routes: DashboardRoute[]) {
  const segments = pathname.split("/").filter(Boolean);

  if (segments.length === 2 && segments[0] === "sessions" && segments[1]) {
    return routes.find((route) => route.path === "/sessions/:sessionId");
  }

  return undefined;
}

export function isRouteVisible(route: DashboardRoute, currentUser: CurrentUser) {
  const roleAllowed =
    route.allowedRoles === "all" || route.allowedRoles.some((role) => currentUser.roles.includes(role));
  const scopeAllowed = route.requiredScopeKinds.some((scopeKind) => scopeMatchesRoute(scopeKind, currentUser.scopes));
  const featureAllowed = route.requiredFeatureFlag
    ? currentUser.featureFlags?.[route.requiredFeatureFlag] !== false
    : true;
  const policyAllowed = policySummaryAllowsRoute(route.requiredPolicySummary, currentUser.policySummaries);

  return roleAllowed && scopeAllowed && featureAllowed && policyAllowed;
}

export function scopeMatchesRoute(requiredScopeKind: ProductScopeKind, scopes: ProductScope[]) {
  return scopes.some(
    (scope) =>
      scope.kind === requiredScopeKind ||
      (requiredScopeKind !== "ContentReviewQueue" && scope.kind === "Organization")
  );
}

function policySummaryAllowsRoute(
  requiredPolicySummary: DashboardRoute["requiredPolicySummary"],
  policySummaries: DashboardPolicySummaries | undefined
) {
  if (!requiredPolicySummary) {
    return true;
  }

  if (requiredPolicySummary === "contentCaptureReview") {
    const contentCapture = policySummaries?.contentCapture;
    return contentCapture?.reviewQueueEnabled === true &&
      (contentCapture.state === "review_required" || contentCapture.state === "active");
  }

  const recommendations = policySummaries?.recommendations;
  return recommendations?.enabled !== false && recommendations?.state !== "disabled";
}

function toConcreteRoute(route: string) {
  return route === "/sessions/:sessionId" ? "/sessions" : route;
}

function readOverviewFilters(): OverviewFilters {
  const params = new URLSearchParams(window.location.search);

  return {
    from: params.get("from") ?? "",
    to: params.get("to") ?? "",
    environment: params.get("environment") ?? "",
    region: params.get("region") ?? "",
    harness: params.get("harness") ?? "",
    model: params.get("model") ?? "",
    modelProvider: params.get("modelProvider") ?? "",
    hotspotType: params.get("hotspotType") ?? "",
    cacheBustCategory: params.get("cacheBustCategory") ?? "",
    findingState: params.get("findingState") ?? "",
    signalType: params.get("signalType") ?? "",
    result: params.get("result") ?? "",
    rejectionReason: params.get("rejectionReason") ?? ""
  };
}

function toOverviewQuery(filters: OverviewFilters) {
  const params = new URLSearchParams();

  Object.entries(filters).forEach(([key, value]) => {
    if (value.trim()) {
      params.set(key, value.trim());
    }
  });

  const query = params.toString();

  return query ? `?${query}` : "";
}
