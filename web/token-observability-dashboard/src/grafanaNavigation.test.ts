import { describe, expect, it } from "vitest";
import { sanitizeGrafanaNavigation } from "./grafanaNavigation";

describe("sanitizeGrafanaNavigation", () => {
  it("keeps documented Grafana filters on overview links", () => {
    const sanitized = sanitizeGrafanaNavigation(
      "/overview",
      "?from=2026-06-10&to=2026-06-12&environment=dv&region=eastus2&harness=codex&model=gpt-5&modelProvider=openai&hotspotType=cache&cacheBustCategory=prefix&findingState=confirmed&signalType=metrics&result=accepted&rejectionReason=invalid_metric_shape"
    );

    expect(sanitized).toBe(
      "/overview?from=2026-06-10&to=2026-06-12&environment=dv&region=eastus2&harness=codex&model=gpt-5&modelProvider=openai&hotspotType=cache&cacheBustCategory=prefix&findingState=confirmed&signalType=metrics&result=accepted&rejectionReason=invalid_metric_shape"
    );
  });

  it("drops unknown and forbidden parameters from Grafana-supported routes", () => {
    const sanitized = sanitizeGrafanaNavigation(
      "/sessions",
      "?from=2026-06-10&workspaceId=workspace-001&sessionId=session-001&developer=dev-001&credentialId=credential-001&traceId=trace-001&filePath=/tmp/source.cs&contentReferenceId=content-001&blobUri=https://storage.example/blob&prompt=raw&commandOutput=raw&toolResult=raw&returnUrl=/admin/audit"
    );

    expect(sanitized).toBe("/sessions?from=2026-06-10");
  });

  it("drops absolute URL values from allowed Grafana parameters", () => {
    const sanitized = sanitizeGrafanaNavigation(
      "/overview",
      "?from=2026-06-10&model=https://example.test/model&region=//example.test/eastus2&harness=codex"
    );

    expect(sanitized).toBe("/overview?from=2026-06-10&harness=codex");
  });

  it("preserves query strings for non-Grafana dashboard routes", () => {
    const sanitized = sanitizeGrafanaNavigation("/admin/audit", "?from=2026-06-10&tab=events");

    expect(sanitized).toBe("/admin/audit?from=2026-06-10&tab=events");
  });
});
