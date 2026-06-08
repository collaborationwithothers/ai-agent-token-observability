using AiAgentTokenObservability.Contracts.Sessions;
using AiAgentTokenObservability.Contracts.Insights;

namespace AiAgentTokenObservability.Storage;

public sealed class InMemoryTelemetryStore : ITelemetryStore, IAsyncDisposable
{
    private readonly List<TelemetryImportModel> _imports = [];
    private readonly List<ContextSourceModel> _contextSources = [];
    private readonly List<HotspotModel> _hotspots = [];
    private readonly List<RecommendationModel> _recommendations = [];

    public int TelemetryImportCount => _imports.Count;

    public IReadOnlyList<AgentSessionModel> Sessions => _imports.Select(import => import.Session).ToArray();
    public IReadOnlyList<ToolCallModel> ToolCalls => _imports.SelectMany(import => import.ToolCalls).ToArray();
    public IReadOnlyList<WorkspaceRepoModel> WorkspaceRepos => _imports.SelectMany(import => import.WorkspaceRepos).ToArray();
    public IReadOnlyList<ContextSourceModel> ContextSources => _contextSources.ToArray();
    public IReadOnlyList<HotspotModel> Hotspots => _hotspots.ToArray();
    public IReadOnlyList<RecommendationModel> Recommendations => _recommendations.ToArray();

    public Task ReplaceImportAsync(TelemetryImportModel import, CancellationToken cancellationToken)
    {
        _imports.RemoveAll(existing => existing.SourceFileHash == import.SourceFileHash);
        _imports.Add(import);

        return Task.CompletedTask;
    }

    public Task<DashboardSessionsResponse> ListSessionSummariesAsync(CancellationToken cancellationToken)
    {
        var sessions = _imports
            .Select(import =>
            {
                var session = import.Session;

                return new DashboardSessionSummaryResponse(
                    session.AgentSessionId,
                    session.Harness,
                    session.HarnessSource,
                    session.HarnessVersion,
                    session.AgentName,
                    session.StartedAtUtc,
                    new TokenSplitResponse(
                        session.TokenTotalType,
                        session.InputTokens,
                        session.OutputTokens,
                        GetTokenMetrics(import, session.AgentSessionId)),
                    import.AgentTurns.Count,
                    import.ToolCalls.Count,
                    import.ModelInvocations.Count,
                    import.WorkspaceRepos.Count);
            })
            .OrderByDescending(session => session.StartedAtUtc)
            .ToArray();

        return Task.FromResult(new DashboardSessionsResponse(sessions, DateTimeOffset.UtcNow));
    }

    public Task<IReadOnlyList<WorkspaceRepoModel>> ListWorkspaceReposAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(WorkspaceRepos);
    }

    public Task ReplaceRepoContextAsync(
        Guid workspaceRepoId,
        IReadOnlyList<ContextSourceModel> contextSources,
        IReadOnlyList<HotspotModel> hotspots,
        IReadOnlyList<RecommendationModel> recommendations,
        CancellationToken cancellationToken)
    {
        _recommendations.RemoveAll(recommendation =>
            _hotspots.Any(hotspot =>
                hotspot.WorkspaceRepoId == workspaceRepoId &&
                hotspot.HotspotId == recommendation.HotspotId));
        _hotspots.RemoveAll(hotspot => hotspot.WorkspaceRepoId == workspaceRepoId);
        _contextSources.RemoveAll(source => source.WorkspaceRepoId == workspaceRepoId);

        _contextSources.AddRange(contextSources);
        _hotspots.AddRange(hotspots);
        _recommendations.AddRange(recommendations);

        return Task.CompletedTask;
    }

    public Task<DashboardInsightsResponse> ListInsightsAsync(CancellationToken cancellationToken)
    {
        var recommendationsByHotspot = _recommendations
            .GroupBy(recommendation => recommendation.HotspotId)
            .ToDictionary(group => group.Key, group => group.Select(ToResponse).ToArray());

        var response = new DashboardInsightsResponse(
            _contextSources
                .OrderBy(source => source.FileCategory)
                .ThenBy(source => source.SpecArtifactStatus)
                .Select(ToResponse)
                .ToArray(),
            _hotspots
                .OrderByDescending(hotspot => hotspot.TokenBurnScore)
                .Select(hotspot => ToResponse(
                    hotspot,
                    recommendationsByHotspot.TryGetValue(hotspot.HotspotId, out var recommendations)
                        ? recommendations
                        : []))
                .ToArray(),
            DateTimeOffset.UtcNow);

        return Task.FromResult(response);
    }

    private static IReadOnlyList<TokenMetricResponse> GetTokenMetrics(TelemetryImportModel import, Guid agentSessionId)
    {
        var modelInvocationIds = import.ModelInvocations
            .Where(invocation => invocation.AgentSessionId == agentSessionId)
            .Select(invocation => invocation.ModelInvocationId)
            .ToHashSet();

        return import.TokenMetrics
            .Where(metric =>
                metric.AgentSessionId == agentSessionId ||
                (metric.ModelInvocationId is Guid modelInvocationId && modelInvocationIds.Contains(modelInvocationId)))
            .OrderBy(metric => metric.MetricName)
            .Select(metric => new TokenMetricResponse(
                metric.MetricName,
                metric.MetricStatus,
                metric.MetricConfidence,
                metric.Value))
            .ToArray();
    }

    public ValueTask DisposeAsync()
    {
        _imports.Clear();
        _contextSources.Clear();
        _hotspots.Clear();
        _recommendations.Clear();
        return ValueTask.CompletedTask;
    }

    private static ContextSourceResponse ToResponse(ContextSourceModel source)
    {
        return new ContextSourceResponse(
            source.ContextSourceId,
            source.WorkspaceRepoId,
            source.SourceType,
            source.PathHash,
            source.DisplayPath,
            source.FileCategory,
            source.SpecArtifactStatus,
            source.EligibleForInferredHotspot,
            source.SizeBytes,
            source.LineCount);
    }

    private static HotspotResponse ToResponse(
        HotspotModel hotspot,
        IReadOnlyList<RecommendationResponse> recommendations)
    {
        return new HotspotResponse(
            hotspot.HotspotId,
            hotspot.AgentSessionId,
            hotspot.WorkspaceRepoId,
            hotspot.SourceType,
            hotspot.SourceRef,
            hotspot.AttributionType,
            hotspot.Confidence,
            hotspot.SuspectedCause,
            hotspot.EvidenceRefsJson,
            hotspot.TokenBurnScore,
            recommendations);
    }

    private static RecommendationResponse ToResponse(RecommendationModel recommendation)
    {
        return new RecommendationResponse(
            recommendation.RecommendationId,
            recommendation.HotspotId,
            recommendation.RecommendationType,
            recommendation.RuleId,
            recommendation.TriggerCondition,
            recommendation.RecommendedAction,
            recommendation.ExpectedBenefit,
            recommendation.Confidence,
            recommendation.EvidenceRefsJson);
    }
}
