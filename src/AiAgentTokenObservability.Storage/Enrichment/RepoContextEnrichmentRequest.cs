namespace AiAgentTokenObservability.Storage.Enrichment;

public sealed record RepoContextEnrichmentRequest(string RepoPath);

public sealed record RepoContextEnrichmentResult(
    Guid WorkspaceRepoId,
    int ContextSourceCount,
    int HotspotCount,
    int RecommendationCount);
