export const allowedGrafanaRoutes = new Set(["/", "/overview", "/sessions"]);

export const allowedGrafanaQueryParameters = new Set([
  "from",
  "to",
  "environment",
  "region",
  "harness",
  "model",
  "modelProvider",
  "hotspotType",
  "cacheBustCategory",
  "findingState",
  "signalType",
  "result",
  "rejectionReason"
]);

export const forbiddenGrafanaQueryParameters = new Set([
  "sessionId",
  "developer",
  "productUserId",
  "credentialId",
  "traceId",
  "spanId",
  "repositoryPath",
  "filePath",
  "contentReferenceId",
  "blobUri",
  "prompt",
  "commandOutput",
  "toolResult",
  "returnUrl"
]);

const boundedGrafanaQueryParameterValues: Record<string, Set<string>> = {
  environment: new Set(["dv", "qa", "pp", "pd"]),
  harness: new Set(["codex", "copilot", "claude"]),
  modelProvider: new Set(["openai", "anthropic", "github", "unknown"]),
  hotspotType: new Set([
    "prompt_cache_breakage",
    "large_context",
    "tool_loop",
    "model_retry",
    "repo_context_bloat",
    "generated_artifact_bloat",
    "expensive_model_choice",
    "error_rework",
    "unknown"
  ]),
  cacheBustCategory: new Set([
    "prompt_changed",
    "system_instruction_changed",
    "tool_context_changed",
    "repository_context_changed",
    "model_changed",
    "unknown"
  ]),
  findingState: new Set(["confirmed", "llm_inferred_candidate"]),
  signalType: new Set(["metrics", "traces", "logs"]),
  result: new Set(["accepted", "rejected", "failed", "succeeded"]),
  rejectionReason: new Set([
    "none",
    "invalid_credential",
    "out_of_scope",
    "unsupported_schema",
    "malformed_otlp",
    "payload_too_large",
    "rate_limited",
    "residency_mismatch",
    "content_classification_failed",
    "transient_failure"
  ])
};

export function sanitizeGrafanaNavigation(pathname: string, search: string) {
  const normalizedPath = pathname || "/";

  if (!allowedGrafanaRoutes.has(normalizedPath)) {
    return `${normalizedPath}${search}`;
  }

  const sanitizedParams = new URLSearchParams();
  const params = new URLSearchParams(search);

  params.forEach((value, key) => {
    if (forbiddenGrafanaQueryParameters.has(key) || !allowedGrafanaQueryParameters.has(key)) {
      return;
    }

    if (containsAbsoluteUrl(value) || containsPathSeparator(value) || !isAllowedGrafanaValue(key, value)) {
      return;
    }

    sanitizedParams.set(key, value);
  });

  const sanitizedSearch = sanitizedParams.toString();

  return sanitizedSearch ? `${normalizedPath}?${sanitizedSearch}` : normalizedPath;
}

function containsAbsoluteUrl(value: string) {
  return value.includes("://") || value.startsWith("//");
}

function containsPathSeparator(value: string) {
  return value.includes("/") || value.includes("\\");
}

function isAllowedGrafanaValue(key: string, value: string) {
  const allowedValues = boundedGrafanaQueryParameterValues[key];

  return !allowedValues || allowedValues.has(value);
}
