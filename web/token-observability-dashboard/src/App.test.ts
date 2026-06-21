import { describe, expect, it, vi } from "vitest";
import {
  dashboardRoutes,
  fetchContentReviewItem,
  fetchContentReviewItems,
  getUtf8ByteCount,
  isRouteVisible,
  resolveRoute,
  sanitizeContentReviewItem,
  scopeMatchesRoute,
  submitContentReviewDecision,
  type ContentReviewItem,
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

const enabledContentCaptureReviewPolicy: DashboardPolicySummaries = {
  contentCapture: { reviewQueueEnabled: true, state: "review_required" }
};

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
      [{ kind: "ContentReviewQueue", scopeId: "default" }],
      { policySummaries: enabledContentCaptureReviewPolicy }
    );
    const engineeringLead = user(
      ["EngineeringLead"],
      [{ kind: "ContentReviewQueue", scopeId: "default" }],
      { policySummaries: enabledContentCaptureReviewPolicy }
    );

    expect(isRouteVisible(route("/content-review"), securityReviewer)).toBe(true);
    expect(isRouteVisible(route("/content-review"), engineeringLead)).toBe(false);
  });

  it("does not grant PlatformAdmin content review access without reviewer scope", () => {
    const platformAdminWithOrganizationScope = user(
      ["PlatformAdmin"],
      [{ kind: "Organization", scopeId: null }],
      { policySummaries: enabledContentCaptureReviewPolicy }
    );
    const platformAdminWithReviewScope = user(
      ["PlatformAdmin"],
      [{ kind: "ContentReviewQueue", scopeId: "default" }],
      { policySummaries: enabledContentCaptureReviewPolicy }
    );

    expect(isRouteVisible(route("/content-review"), platformAdminWithOrganizationScope)).toBe(false);
    expect(isRouteVisible(route("/content-review"), platformAdminWithReviewScope)).toBe(true);
  });

  it("uses /api/v1/me feature flags to hide disabled route surfaces", () => {
    const reviewerWithDisabledFeature = user(
      ["SecurityReviewer"],
      [{ kind: "ContentReviewQueue", scopeId: "default" }],
      { featureFlags: { contentReview: false }, policySummaries: enabledContentCaptureReviewPolicy }
    );
    const reviewerWithEnabledFeature = user(
      ["SecurityReviewer"],
      [{ kind: "ContentReviewQueue", scopeId: "default" }],
      { featureFlags: { contentReview: true }, policySummaries: enabledContentCaptureReviewPolicy }
    );

    expect(isRouteVisible(route("/content-review"), reviewerWithDisabledFeature)).toBe(false);
    expect(isRouteVisible(route("/content-review"), reviewerWithEnabledFeature)).toBe(true);
  });

  it("uses /api/v1/me policy summaries to hide disabled policy surfaces", () => {
    const reviewerWithMissingContentCapturePolicy = user(
      ["SecurityReviewer"],
      [{ kind: "ContentReviewQueue", scopeId: "default" }]
    );
    const reviewerWithDisabledContentCapturePolicy = user(
      ["SecurityReviewer"],
      [{ kind: "ContentReviewQueue", scopeId: "default" }],
      { policySummaries: { contentCapture: { reviewQueueEnabled: false, state: "disabled" } } }
    );
    const reviewerWithEnabledContentCapturePolicy = user(
      ["SecurityReviewer"],
      [{ kind: "ContentReviewQueue", scopeId: "default" }],
      { policySummaries: enabledContentCaptureReviewPolicy }
    );
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

    expect(isRouteVisible(route("/content-review"), reviewerWithMissingContentCapturePolicy)).toBe(false);
    expect(isRouteVisible(route("/content-review"), reviewerWithDisabledContentCapturePolicy)).toBe(false);
    expect(isRouteVisible(route("/content-review"), reviewerWithEnabledContentCapturePolicy)).toBe(true);
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

describe("content review Product API helpers", () => {
  it("lists supported review states through the content review items route", async () => {
    const fetchMock = vi.fn(async () =>
      jsonResponse({
        items: [
          contentReviewItem("review_required"),
          contentReviewItem("redaction_failed"),
          contentReviewItem("discarded"),
          contentReviewItem("approved_excerpt", { approvedExcerpt: "approved safe excerpt" })
        ],
        nextCursor: null,
        totalEstimate: 4
      })
    );

    const response = await fetchContentReviewItems("/api/v1", "review_required", fetchMock);

    expect(fetchMock).toHaveBeenCalledWith("/api/v1/content-review/items?state=review_required", {
      credentials: "include",
      headers: { Accept: "application/json" }
    });
    expect(response.items.map((item) => item.captureState)).toEqual([
      "review_required",
      "redaction_failed",
      "discarded",
      "approved_excerpt"
    ]);
  });

  it("loads item detail while blocking raw failed content and direct blob links", async () => {
    const fetchMock = vi.fn(async () =>
      jsonResponse({
        ...contentReviewItem("redaction_failed"),
        rawFailedContent: "do not render this content",
        promptText: "do not render this prompt",
        blob: {
          container: "review-artifacts",
          blobName: "content.json",
          blobUri: "azblob://review-artifacts/content.json",
          blobVersion: null
        }
      })
    );

    const item = await fetchContentReviewItem("/api/v1", "content-1", fetchMock);
    const renderedPayload = JSON.stringify(item);

    expect(fetchMock).toHaveBeenCalledWith("/api/v1/content-review/items/content-1", {
      credentials: "include",
      headers: { Accept: "application/json" }
    });
    expect(renderedPayload).not.toContain("do not render this content");
    expect(renderedPayload).not.toContain("do not render this prompt");
    expect(renderedPayload).not.toContain("azblob://review-artifacts/content.json");
    expect(item.blob).toEqual({
      container: "review-artifacts",
      blobName: "content.json",
      blobVersion: null
    });
  });

  it("adds Idempotency-Key to retry, discard, approve excerpt, and recommendation-ineligible decisions", async () => {
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) =>
      jsonResponse({
        redactionReviewId: "review-1",
        auditEventId: "audit-1",
        decision: "retry",
        decidedAtUtc: "2026-06-21T12:00:00Z",
        contentReference: contentReviewItem("review_required")
      })
    );

    await submitContentReviewDecision("/api/v1", "content-1", "retry-redaction", { decisionReason: "retry" }, fetchMock);
    await submitContentReviewDecision("/api/v1", "content-1", "discard", { decisionReason: "discard" }, fetchMock);
    await submitContentReviewDecision(
      "/api/v1",
      "content-1",
      "approve-excerpt",
      { decisionReason: "approve", approvedExcerpt: "approved safe excerpt" },
      fetchMock
    );
    await submitContentReviewDecision(
      "/api/v1",
      "content-1",
      "mark-recommendation-ineligible",
      { decisionReason: "recommendation ineligible" },
      fetchMock
    );

    expect(fetchMock).toHaveBeenCalledTimes(4);
    for (const call of fetchMock.mock.calls as Array<[RequestInfo | URL, RequestInit]>) {
      const [, init] = call;
      expect(init?.method).toBe("POST");
      expect((init?.headers as Record<string, string>)["Idempotency-Key"]).toMatch(/content-1/);
    }
  });

  it("enforces the approved excerpt byte limit before calling Product API", async () => {
    const fetchMock = vi.fn();
    const oversizedExcerpt = "x".repeat(2049);

    await expect(
      submitContentReviewDecision(
        "/api/v1",
        "content-1",
        "approve-excerpt",
        { decisionReason: "approve", approvedExcerpt: oversizedExcerpt },
        fetchMock
      )
    ).rejects.toThrow("Approved excerpt exceeds the bounded excerpt size limit.");
    expect(getUtf8ByteCount(oversizedExcerpt)).toBe(2049);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("returns audit status and resulting content reference state from successful decisions", async () => {
    const fetchMock = vi.fn(async () =>
      jsonResponse({
        redactionReviewId: "review-1",
        auditEventId: "audit-1",
        decision: "approve_excerpt",
        decidedAtUtc: "2026-06-21T12:00:00Z",
        contentReference: contentReviewItem("approved_excerpt", { approvedExcerpt: "approved safe excerpt" })
      })
    );

    const response = await submitContentReviewDecision(
      "/api/v1",
      "content-1",
      "approve-excerpt",
      { decisionReason: "approve", approvedExcerpt: "approved safe excerpt" },
      fetchMock
    );

    expect(response.auditEventId).toBe("audit-1");
    expect(response.decision).toBe("approve_excerpt");
    expect(response.contentReference?.captureState).toBe("approved_excerpt");
    expect(response.contentReference?.approvedExcerpt).toBe("approved safe excerpt");
  });

  it("surfaces Product API ProblemDetails for unauthorized and idempotency errors", async () => {
    const unauthorizedFetch = vi.fn(async () =>
      jsonResponse(
        {
          title: "Content review access denied.",
          status: 403,
          code: "content_review_forbidden",
          correlationId: "corr-denied"
        },
        403
      )
    );
    const idempotencyFetch = vi.fn(async () =>
      jsonResponse(
        {
          title: "Idempotency key required.",
          status: 400,
          code: "idempotency_key_required",
          correlationId: "corr-idempotency"
        },
        400
      )
    );

    await expect(fetchContentReviewItems("/api/v1", "review_required", unauthorizedFetch)).rejects.toMatchObject({
      problem: { status: 403, code: "content_review_forbidden", correlationId: "corr-denied" }
    });
    await expect(
      submitContentReviewDecision("/api/v1", "content-1", "discard", { decisionReason: "discard" }, idempotencyFetch)
    ).rejects.toMatchObject({
      problem: { status: 400, code: "idempotency_key_required", correlationId: "corr-idempotency" }
    });
  });

  it("sanitizes unexpected raw content fields from content review records", () => {
    const sanitized = sanitizeContentReviewItem({
      ...contentReviewItem("review_required"),
      rawFailedContent: "secret",
      commandOutput: "tool output"
    } as never);

    expect(JSON.stringify(sanitized)).not.toContain("secret");
    expect(JSON.stringify(sanitized)).not.toContain("tool output");
  });
});

function contentReviewItem(
  captureState: string,
  overrides: Partial<ContentReviewItem> = {}
) {
  return {
    ...contentReviewItemBase(captureState),
    ...overrides
  };
}

function contentReviewItemBase(captureState: string): ContentReviewItem {
  return {
    contentReferenceId: "content-1",
    customerOrganizationId: "customer-1",
    agentSessionId: "session-1",
    telemetryEnvelopeId: "envelope-1",
    contentClass: "tool_output",
    captureState,
    redactionStatus: captureState === "approved_excerpt" ? "manually_approved" : "review_required",
    contentHash: null,
    blob: null,
    policyVersionId: "policy-1",
    redactionPipelineVersion: "pipeline-1",
    productRuleVersion: "rules-1",
    retentionClass: "standard",
    expiresAtUtc: null,
    recommendationEligible: captureState === "approved_excerpt",
    auditEventId: "audit-original",
    approvedExcerpt: null,
    createdAtUtc: "2026-06-21T11:00:00Z",
    updatedAtUtc: "2026-06-21T11:00:00Z"
  };
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" }
  });
}
