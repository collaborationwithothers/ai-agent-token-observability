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
  "filePath",
  "contentReferenceId",
  "blobUri",
  "prompt",
  "commandOutput",
  "toolResult",
  "returnUrl"
]);

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

    if (containsAbsoluteUrl(value)) {
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
