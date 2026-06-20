import { useCallback, useEffect, useMemo, useState } from "react";
import "./App.css";
import { sanitizeGrafanaNavigation } from "./grafanaNavigation";

const defaultProductApiBaseUrl = "/api/v1";

type ProductRole =
  | "PlatformAdmin"
  | "SecurityReviewer"
  | "EngineeringLead"
  | "Developer"
  | "ReadOnlyViewer";

type ProductScopeKind =
  | "Organization"
  | "Team"
  | "Repository"
  | "HarnessProfile"
  | "Self"
  | "ContentReviewQueue"
  | "Pricing"
  | "TenantAdmin";

type ProductScope = {
  kind: ProductScopeKind;
  scopeId: string | null;
};

type CurrentUser = {
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

type DashboardRoute = {
  path: string;
  label: string;
  group: "investigate" | "govern" | "admin" | "personal";
  purpose: string;
  allowedRoles: "all" | ProductRole[];
  requiredScopeKinds: ProductScopeKind[];
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

const dashboardRoutes: DashboardRoute[] = [
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
    requiredScopeKinds: ["Organization", "Team", "Repository", "Self"]
  },
  {
    path: "/sessions/:sessionId",
    label: "Session detail",
    group: "investigate",
    purpose: "Session investigation shell for authorized session detail.",
    allowedRoles: ["Developer", "EngineeringLead", "PlatformAdmin", "SecurityReviewer"],
    requiredScopeKinds: ["Organization", "Team", "Repository", "Self", "ContentReviewQueue"],
    showInNavigation: false
  },
  {
    path: "/content-review",
    label: "Content review",
    group: "govern",
    purpose: "Metadata-only content review queue entry point.",
    allowedRoles: ["SecurityReviewer", "PlatformAdmin"],
    requiredScopeKinds: ["Organization", "ContentReviewQueue"]
  },
  {
    path: "/recommendations",
    label: "Recommendations",
    group: "govern",
    purpose: "Visible recommendation queue and status entry point.",
    allowedRoles: ["Developer", "EngineeringLead", "PlatformAdmin"],
    requiredScopeKinds: ["Organization", "Team", "Repository", "Self"]
  },
  {
    path: "/admin/identity",
    label: "Identity",
    group: "admin",
    purpose: "Identity tenants and Product Role Mapping administration.",
    allowedRoles: ["PlatformAdmin"],
    requiredScopeKinds: ["Organization", "TenantAdmin"]
  },
  {
    path: "/admin/harness-setup",
    label: "Harness setup",
    group: "admin",
    purpose: "Harness setup profiles and scoped ingestion credential lifecycle.",
    allowedRoles: ["PlatformAdmin"],
    requiredScopeKinds: ["Organization", "TenantAdmin", "HarnessProfile"]
  },
  {
    path: "/admin/pricing",
    label: "Pricing",
    group: "admin",
    purpose: "Harness Pricing Basis review entry point.",
    allowedRoles: ["PlatformAdmin"],
    requiredScopeKinds: ["Organization", "TenantAdmin", "Pricing"]
  },
  {
    path: "/admin/budgets",
    label: "Budgets",
    group: "admin",
    purpose: "Non-punitive budget alert policy entry point.",
    allowedRoles: ["PlatformAdmin", "EngineeringLead"],
    requiredScopeKinds: ["Organization", "Team", "Repository", "Pricing"]
  },
  {
    path: "/admin/audit",
    label: "Audit",
    group: "admin",
    purpose: "Governance audit event query entry point.",
    allowedRoles: ["PlatformAdmin", "SecurityReviewer"],
    requiredScopeKinds: ["Organization", "TenantAdmin", "ContentReviewQueue", "HarnessProfile"]
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
          <OverviewShell currentPath={currentPath} setCurrentPath={setCurrentPath} />
        ) : (
          <RouteState title={activeRoute.label} detail={activeRoute.purpose} route={activeRoute} />
        )}
      </main>
    </div>
  );
}

function OverviewShell({
  currentPath,
  setCurrentPath
}: {
  currentPath: string;
  setCurrentPath: (path: string, options?: { replace?: boolean }) => void;
}) {
  const [filters, setFilters] = useState<OverviewFilters>(() => readOverviewFilters());

  useEffect(() => {
    setFilters(readOverviewFilters());
  }, [currentPath]);

  return (
    <section className="route-surface" aria-labelledby="overview-title">
      <div className="route-heading">
        <div>
          <p className="eyebrow">Overview</p>
          <h3 id="overview-title">Tenant-aware query state</h3>
        </div>
        <span className="state-chip">No Product API data loaded</span>
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
      <div className="empty-state">
        <h4>Overview data will load from Product API</h4>
        <p>
          The shell preserves filter state and waits for the authorized aggregate overview route to provide summaries.
        </p>
      </div>
    </section>
  );
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

function resolveRoute(path: string, routes: DashboardRoute[]) {
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

function isRouteVisible(route: DashboardRoute, currentUser: CurrentUser) {
  const roleAllowed =
    route.allowedRoles === "all" || route.allowedRoles.some((role) => currentUser.roles.includes(role));

  return roleAllowed && route.requiredScopeKinds.some((scopeKind) => scopeMatchesRoute(scopeKind, currentUser.scopes));
}

function scopeMatchesRoute(requiredScopeKind: ProductScopeKind, scopes: ProductScope[]) {
  return scopes.some((scope) => scope.kind === "Organization" || scope.kind === requiredScopeKind);
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
