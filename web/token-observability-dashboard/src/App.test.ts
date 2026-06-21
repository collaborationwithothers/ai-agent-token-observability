import { describe, expect, it } from "vitest";
import {
  dashboardRoutes,
  isRouteVisible,
  resolveRoute,
  scopeMatchesRoute,
  type CurrentUser,
  type DashboardPolicySummaries,
  type DashboardFeatureFlag,
  type ProductRole,
  type ProductScope
} from "./App";

function user(
  roles: ProductRole[],
  scopes: ProductScope[],
  options?: {
    featureFlags?: Partial<Record<DashboardFeatureFlag, boolean>>;
    policySummaries?: DashboardPolicySummaries;
  }
): CurrentUser {
  return {
    customerOrganization: {
      slug: "contoso",
      displayName: "Contoso",
      dataResidencyRegion: "eastus2"
    },
    productUser: {
      displayLabel: "Contoso User",
      email: null
    },
    roles,
    scopes,
    featureFlags: options?.featureFlags,
    policySummaries: options?.policySummaries,
    correlationId: "corr-test"
  };
}

function route(path: string) {
  const found = dashboardRoutes.find((candidate) => candidate.path === path);

  if (!found) {
    throw new Error(`Missing route ${path}.`);
  }

  return found;
}

describe("dashboard route authorization", () => {
  it("exposes the complete product route map required by bootstrap", () => {
    expect(dashboardRoutes.map((candidate) => candidate.path)).toEqual([
      "/overview",
      "/sessions",
      "/sessions/:sessionId",
      "/content-review",
      "/recommendations",
      "/admin/identity",
      "/admin/harness-setup",
      "/admin/pricing",
      "/admin/budgets",
      "/admin/audit",
      "/settings/me"
    ]);
  });

  it("allows organization scope to satisfy route-specific scope checks without sample data", () => {
    const platformAdmin = user(
      ["PlatformAdmin"],
      [{ kind: "Organization", scopeId: null }]
    );

    expect(isRouteVisible(route("/admin/identity"), platformAdmin)).toBe(true);
    expect(isRouteVisible(route("/admin/budgets"), platformAdmin)).toBe(true);
  });

  it("keeps budget management limited to budget-capable roles and scopes", () => {
    const leadWithPricingScope = user(
      ["EngineeringLead"],
      [{ kind: "Pricing", scopeId: "pricing" }]
    );
    const developerWithPricingScope = user(
      ["Developer"],
      [{ kind: "Pricing", scopeId: "pricing" }]
    );

    expect(isRouteVisible(route("/admin/budgets"), leadWithPricingScope)).toBe(true);
    expect(isRouteVisible(route("/admin/budgets"), developerWithPricingScope)).toBe(false);
  });

  it("keeps content review behind reviewer roles and review scope", () => {
    const securityReviewer = user(
      ["SecurityReviewer"],
      [{ kind: "ContentReviewQueue", scopeId: "default" }]
    );
    const engineeringLead = user(
      ["EngineeringLead"],
      [{ kind: "ContentReviewQueue", scopeId: "default" }]
    );

    expect(isRouteVisible(route("/content-review"), securityReviewer)).toBe(true);
    expect(isRouteVisible(route("/content-review"), engineeringLead)).toBe(false);
  });

  it("uses /api/v1/me feature flags to hide disabled route surfaces", () => {
    const reviewerWithDisabledFeature = user(
      ["SecurityReviewer"],
      [{ kind: "ContentReviewQueue", scopeId: "default" }],
      { featureFlags: { contentReview: false } }
    );
    const reviewerWithEnabledFeature = user(
      ["SecurityReviewer"],
      [{ kind: "ContentReviewQueue", scopeId: "default" }],
      { featureFlags: { contentReview: true } }
    );

    expect(isRouteVisible(route("/content-review"), reviewerWithDisabledFeature)).toBe(false);
    expect(isRouteVisible(route("/content-review"), reviewerWithEnabledFeature)).toBe(true);
  });

  it("uses /api/v1/me policy summaries to hide disabled policy surfaces", () => {
    const developerWithDisabledRecommendations = user(
      ["Developer"],
      [{ kind: "Self", scopeId: "user-1" }],
      { policySummaries: { recommendations: { enabled: false, state: "disabled" } } }
    );
    const developerWithEnabledRecommendations = user(
      ["Developer"],
      [{ kind: "Self", scopeId: "user-1" }],
      { policySummaries: { recommendations: { enabled: true, state: "deterministic_only" } } }
    );

    expect(isRouteVisible(route("/recommendations"), developerWithDisabledRecommendations)).toBe(false);
    expect(isRouteVisible(route("/recommendations"), developerWithEnabledRecommendations)).toBe(true);
  });

  it("resolves shareable session detail routes without leaking path parameters into authorization", () => {
    expect(resolveRoute("/sessions/session-123?from=2026-06-21", dashboardRoutes)?.path).toBe(
      "/sessions/:sessionId"
    );
    expect(resolveRoute("/admin/audit?from=2026-06-21", dashboardRoutes)?.path).toBe("/admin/audit");
  });

  it("does not treat unrelated scopes as authorization for a route", () => {
    expect(scopeMatchesRoute("TenantAdmin", [{ kind: "Pricing", scopeId: "pricing" }])).toBe(false);
  });
});
