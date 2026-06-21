import { describe, expect, it } from "vitest";
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import {
  createSessionInvestigationRequestUrls,
  dashboardRoutes,
  isRouteVisible,
  readSessionIdFromPath,
  resolveRoute,
  SessionInvestigationContent,
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

  it("builds stable Product API requests for shareable session detail routes", () => {
    expect(readSessionIdFromPath("/sessions/session-123?from=2026-06-21")).toBe("session-123");
    expect(createSessionInvestigationRequestUrls("/api/v1", "session/with/slash")).toEqual({
      summary: "/api/v1/sessions/session%2Fwith%2Fslash",
      timeline: "/api/v1/sessions/session%2Fwith%2Fslash/timeline",
      recommendations: "/api/v1/sessions/session%2Fwith%2Fslash/recommendations"
    });
  });

  it("renders session evidence states without raw failed content or punitive wording", () => {
    const markup = renderToStaticMarkup(createElement(SessionInvestigationContent, {
      data: {
        summary: {
          session: {
            agentSessionId: "session-123",
            providerSessionIdHash: "hash-123",
            startedAtUtc: "2026-06-21T10:00:00Z",
            endedAtUtc: null,
            sessionStatus: "active"
          },
          harnessContext: {
            harness: "codex-cli",
            harnessSetupProfileId: "profile-codex"
          },
          modelContext: {
            providerNames: ["openai"],
            modelNames: ["gpt-5"]
          },
          tokenSummary: {
            metricStatus: "mixed",
            metricConfidence: "estimated",
            split: [
              {
                metricName: "input_tokens",
                value: 1200,
                metricStatus: "observed",
                metricConfidence: "observed"
              },
              {
                metricName: "cached_input_tokens",
                value: null,
                metricStatus: "unavailable",
                metricConfidence: "unavailable"
              }
            ]
          },
          costSummary: {
            estimatedTotal: null,
            currency: null,
            costStatus: "unavailable"
          },
          repositoryContext: {
            evidenceState: "unavailable"
          },
          tokenHotspots: [
            {
              tokenHotspotId: "hotspot-1",
              hotspotType: "prompt_cache_breakage",
              findingState: "candidate_correlated",
              attributionType: "correlated",
              confidence: "medium",
              metricStatus: "unavailable",
              metricConfidence: "unavailable",
              promptCacheEvidenceState: "unknown",
              modelName: "gpt-5",
              evidenceSummary: "Cache cause is unknown because provider cache fields were unavailable.",
              estimatedCostImpact: null,
              routeTarget: "#hotspot-hotspot-1"
            }
          ],
          cacheDiagnostics: [
            {
              diagnosticType: "prompt_cache_evidence",
              evidenceState: "unknown",
              tokenHotspotId: "hotspot-1",
              routeTarget: "#hotspot-hotspot-1"
            }
          ],
          contentEvidence: {
            summary: "mixed",
            items: [
              {
                contentReferenceId: "content-approved",
                contentClass: "prompt_snippet",
                captureState: "approved_excerpt",
                redactionStatus: "manually_approved",
                evidenceState: "approved_excerpt",
                policyVersionId: "policy-v1",
                redactionPipelineVersion: "pipeline-v1",
                productRuleVersion: "rules-v1",
                recommendationEligible: true,
                auditEventId: "audit-approved",
                approvedExcerpt: "Approved bounded excerpt."
              },
              {
                contentReferenceId: "content-review",
                contentClass: "prompt_snippet",
                captureState: "review_required",
                redactionStatus: "review_required",
                evidenceState: "review_required",
                policyVersionId: "policy-v1",
                redactionPipelineVersion: "pipeline-v1",
                productRuleVersion: "rules-v1",
                recommendationEligible: false,
                auditEventId: "audit-review",
                approvedExcerpt: "raw review content must not render"
              },
              {
                contentReferenceId: "content-failed",
                contentClass: "command_output",
                captureState: "redaction_failed",
                redactionStatus: "failed",
                evidenceState: "redaction_failed",
                policyVersionId: "policy-v1",
                redactionPipelineVersion: "pipeline-v1",
                productRuleVersion: "rules-v1",
                recommendationEligible: false,
                auditEventId: "audit-failed",
                approvedExcerpt: "raw failed content must not render"
              }
            ]
          },
          recommendations: {
            status: "generated",
            items: [
              {
                recommendationId: "recommendation-1",
                tokenHotspotId: "hotspot-1",
                kind: "deterministic",
                state: "candidate",
                authorityState: "deterministic",
                confidence: "high",
                validationState: "validated",
                summary: "Reduce unnecessary context and prefer targeted files.",
                rationale: "Observed input token evidence exceeded the configured threshold.",
                expectedBenefit: "Lower input token use.",
                auditEventId: "audit-recommendation"
              }
            ]
          },
          auditContext: {
            correlationId: "corr-session",
            contentAuditEventIds: ["audit-approved", "audit-review", "audit-failed"],
            recommendationAuditEventIds: ["audit-recommendation"]
          }
        },
        timeline: {
          items: [
            {
              timelineItemId: "timeline-1",
              eventTimestampUtc: "2026-06-21T10:00:00Z",
              itemType: "token_observation",
              title: "Token observation recorded",
              state: "observed",
              relatedResourceId: "observation-1",
              metadata: {}
            }
          ]
        },
        recommendations: {
          items: []
        }
      }
    }));

    expect(markup).toContain("Approved bounded excerpt.");
    expect(markup).toContain("review required");
    expect(markup).toContain("redaction failed");
    expect(markup).not.toContain("raw review content must not render");
    expect(markup).not.toContain("raw failed content must not render");
    expect(markup.toLowerCase()).not.toContain("leaderboard");
    expect(markup.toLowerCase()).not.toContain("ranking");
    expect(markup.toLowerCase()).not.toContain("blame");
    expect(markup.toLowerCase()).not.toContain("wrongness");
    expect(markup.toLowerCase()).not.toContain("user-error");
  });

  it("does not treat unrelated scopes as authorization for a route", () => {
    expect(scopeMatchesRoute("TenantAdmin", [{ kind: "Pricing", scopeId: "pricing" }])).toBe(false);
  });
});
