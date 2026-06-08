using System.Net.Http.Json;
using AiAgentTokenObservability.Contracts.Sessions;
using AiAgentTokenObservability.Contracts.Insights;
using AiAgentTokenObservability.Dashboard.Api.Insights;
using AiAgentTokenObservability.Dashboard.Api.Sessions;
using AiAgentTokenObservability.Dashboard.Web.Insights;
using AiAgentTokenObservability.Dashboard.Web.Sessions;
using AiAgentTokenObservability.Storage;
using AiAgentTokenObservability.Storage.Enrichment;
using AiAgentTokenObservability.Storage.Import;
using AiAgentTokenObservability.Storage.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AiAgentTokenObservability.Tests;

public sealed class CopilotJsonlImportTests
{
    [Fact]
    public async Task ImportCopilotJsonlPersistsNormalizedSessionTurnToolAndTokenSplit()
    {
        await using var store = new InMemoryTelemetryStore();
        var importer = new CopilotJsonlImportService(store, SystemClock.Instance);
        var path = await WriteJsonlAsync();

        var result = await importer.ImportAsync(new CopilotJsonlImportRequest(path), CancellationToken.None);
        var sessions = await store.ListSessionSummariesAsync(CancellationToken.None);

        Assert.Equal("succeeded", result.ImportStatus);
        Assert.Equal(5, result.RecordCount);
        Assert.Equal(1, result.SkippedRecordCount);
        var session = Assert.Single(sessions.Sessions);
        Assert.Equal("copilot", session.Harness);
        Assert.Equal("github-copilot-vscode", session.HarnessSource);
        Assert.Equal("GitHub Copilot Chat", session.AgentName);
        Assert.Equal("observed", session.TokenSplit.TokenTotalType);
        Assert.Equal(123, session.TokenSplit.InputTokens);
        Assert.Equal(45, session.TokenSplit.OutputTokens);
        Assert.Equal(1, session.TurnCount);
        Assert.Equal(1, session.ToolCallCount);
        Assert.Equal(1, session.ModelInvocationCount);
        Assert.Equal(0, session.WorkspaceRepoCount);
    }

    [Fact]
    public async Task ReimportingSameFileContentsReplacesPreviousImport()
    {
        await using var store = new InMemoryTelemetryStore();
        var importer = new CopilotJsonlImportService(store, SystemClock.Instance);
        var path = await WriteJsonlAsync();

        var first = await importer.ImportAsync(new CopilotJsonlImportRequest(path), CancellationToken.None);
        var second = await importer.ImportAsync(new CopilotJsonlImportRequest(path), CancellationToken.None);
        var sessions = await store.ListSessionSummariesAsync(CancellationToken.None);

        Assert.NotEqual(first.TelemetryImportId, second.TelemetryImportId);
        Assert.Single(sessions.Sessions);
        Assert.Equal(1, store.TelemetryImportCount);
    }

    [Fact]
    public async Task ExplicitRepoPathCreatesHashedWorkspaceRepoWithoutRawPath()
    {
        await using var store = new InMemoryTelemetryStore();
        var importer = new CopilotJsonlImportService(store, SystemClock.Instance);
        var path = await WriteJsonlAsync();

        await importer.ImportAsync(
            new CopilotJsonlImportRequest(path, RepoPath: "/private/work/repo", RepoFriendlyName: "repo"),
            CancellationToken.None);

        var repo = Assert.Single(store.WorkspaceRepos);
        Assert.Equal("repo", repo.RepoFriendlyName);
        Assert.Null(repo.RepoPath);
        Assert.NotEqual("/private/work/repo", repo.RepoPathHash);
        Assert.Equal(64, repo.RepoPathHash.Length);
    }

    [Fact]
    public async Task DefaultImportPersistsMetadataOnlyPrivacyFields()
    {
        await using var store = new InMemoryTelemetryStore();
        var importer = new CopilotJsonlImportService(store, SystemClock.Instance);
        var path = await WriteJsonlAsync();

        await importer.ImportAsync(
            new CopilotJsonlImportRequest(
                path,
                RepoPath: "/private/work/repo",
                RepoFriendlyName: "repo",
                DeveloperIdentity: "alex@example.com"),
            CancellationToken.None);

        var session = Assert.Single(store.Sessions);
        Assert.False(session.ContentCaptured);
        Assert.NotNull(session.UserHash);
        Assert.NotEqual("alex@example.com", session.UserHash);
        Assert.Equal(64, session.UserHash.Length);

        var repo = Assert.Single(store.WorkspaceRepos);
        Assert.Equal("repo", repo.RepoFriendlyName);
        Assert.Null(repo.RepoPath);
        Assert.NotEqual("/private/work/repo", repo.RepoPathHash);
        Assert.Equal(64, repo.RepoPathHash.Length);

        var toolCall = Assert.Single(store.ToolCalls);
        Assert.False(toolCall.ArgumentsCaptured);
        Assert.False(toolCall.ResultCaptured);
    }

    [Fact]
    public async Task ImportCopilotJsonlWithMissingOutputTokensStoresUnavailableMetricAsNull()
    {
        await using var store = new InMemoryTelemetryStore();
        var importer = new CopilotJsonlImportService(store, SystemClock.Instance);
        var path = await WriteJsonlWithMissingOutputTokensAsync();

        await importer.ImportAsync(new CopilotJsonlImportRequest(path), CancellationToken.None);
        var sessions = await store.ListSessionSummariesAsync(CancellationToken.None);

        var session = Assert.Single(sessions.Sessions);
        Assert.Equal("unavailable", session.TokenSplit.TokenTotalType);
        Assert.Equal(123, session.TokenSplit.InputTokens);
        Assert.Null(session.TokenSplit.OutputTokens);

        var inputMetric = Assert.Single(session.TokenSplit.Metrics, metric => metric.MetricName == "input_tokens");
        Assert.Equal("observed", inputMetric.MetricStatus);
        Assert.Equal("observed", inputMetric.MetricConfidence);
        Assert.Equal(123, inputMetric.Value);

        var outputMetric = Assert.Single(session.TokenSplit.Metrics, metric => metric.MetricName == "output_tokens");
        Assert.Equal("unavailable", outputMetric.MetricStatus);
        Assert.Equal("unavailable", outputMetric.MetricConfidence);
        Assert.Null(outputMetric.Value);
    }

    [Fact]
    public async Task DashboardSessionsClientReadsImportedSessionTokenSplit()
    {
        var response = new DashboardSessionsResponse(
            [
                new DashboardSessionSummaryResponse(
                    AgentSessionId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Harness: "copilot",
                    HarnessSource: "github-copilot-vscode",
                    HarnessVersion: "0.51.0",
                    AgentName: "GitHub Copilot Chat",
                    StartedAtUtc: new DateTimeOffset(2026, 6, 8, 10, 0, 0, TimeSpan.Zero),
                    TokenSplit: new TokenSplitResponse(
                        "unavailable",
                        123,
                        null,
                        [
                            new TokenMetricResponse("input_tokens", "observed", "observed", 123),
                            new TokenMetricResponse("output_tokens", "unavailable", "unavailable", null)
                        ]),
                    TurnCount: 1,
                    ToolCallCount: 1,
                    ModelInvocationCount: 1,
                    WorkspaceRepoCount: 0)
            ],
            GeneratedAtUtc: new DateTimeOffset(2026, 6, 8, 10, 1, 0, TimeSpan.Zero));

        using var client = new HttpClient(new DashboardStatusTests.JsonResponseHandler<DashboardSessionsResponse>(response))
        {
            BaseAddress = new Uri("https://dashboard-api")
        };
        var sessionsClient = new DashboardSessionsClient(client);

        var sessions = await sessionsClient.GetSessionsAsync(CancellationToken.None);

        var session = Assert.Single(sessions.Sessions);
        Assert.Equal("unavailable", session.TokenSplit.TokenTotalType);
        Assert.Equal(123, session.TokenSplit.InputTokens);
        Assert.Null(session.TokenSplit.OutputTokens);
        var outputMetric = Assert.Single(session.TokenSplit.Metrics, metric => metric.MetricName == "output_tokens");
        Assert.Equal("unavailable", outputMetric.MetricStatus);
        Assert.Equal("unavailable", outputMetric.MetricConfidence);
        Assert.Null(outputMetric.Value);
    }

    [Fact]
    public void DashboardTokenFormatterDoesNotRenderUnavailableMetricsAsZero()
    {
        Assert.Equal("Unavailable", DashboardTokenFormatter.FormatTokens(null));
        Assert.Equal("0", DashboardTokenFormatter.FormatTokens(0));
    }

    [Fact]
    public async Task DashboardSessionsEndpointReturnsPersistedImportTokenSplit()
    {
        await using var store = new InMemoryTelemetryStore();
        var importer = new CopilotJsonlImportService(store, SystemClock.Instance);
        var path = await WriteJsonlAsync();
        await importer.ImportAsync(new CopilotJsonlImportRequest(path), CancellationToken.None);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ITelemetryStore>(store);
        await using var app = builder.Build();
        app.MapDashboardSessionEndpoints();
        await app.StartAsync();

        var client = app.GetTestClient();
        var sessions = await client.GetFromJsonAsync<DashboardSessionsResponse>("/sessions");

        Assert.NotNull(sessions);
        var session = Assert.Single(sessions.Sessions);
        Assert.Equal("copilot", session.Harness);
        Assert.Equal("observed", session.TokenSplit.TokenTotalType);
        Assert.Equal(123, session.TokenSplit.InputTokens);
        Assert.Equal(45, session.TokenSplit.OutputTokens);
        Assert.Equal(1, session.TurnCount);
        Assert.Equal(1, session.ToolCallCount);
        Assert.Equal(1, session.ModelInvocationCount);
    }

    [Fact]
    public async Task DashboardSessionsEndpointExposesUnavailableTokenMetricStatus()
    {
        await using var store = new InMemoryTelemetryStore();
        var importer = new CopilotJsonlImportService(store, SystemClock.Instance);
        var path = await WriteJsonlWithMissingOutputTokensAsync();
        await importer.ImportAsync(new CopilotJsonlImportRequest(path), CancellationToken.None);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ITelemetryStore>(store);
        await using var app = builder.Build();
        app.MapDashboardSessionEndpoints();
        await app.StartAsync();

        var client = app.GetTestClient();
        var sessions = await client.GetFromJsonAsync<DashboardSessionsResponse>("/sessions");

        Assert.NotNull(sessions);
        var session = Assert.Single(sessions.Sessions);
        Assert.Equal("unavailable", session.TokenSplit.TokenTotalType);
        Assert.Equal(123, session.TokenSplit.InputTokens);
        Assert.Null(session.TokenSplit.OutputTokens);
        var outputMetric = Assert.Single(session.TokenSplit.Metrics, metric => metric.MetricName == "output_tokens");
        Assert.Equal("unavailable", outputMetric.MetricStatus);
        Assert.Equal("unavailable", outputMetric.MetricConfidence);
        Assert.Null(outputMetric.Value);
    }

    [Fact]
    public async Task RepoContextEnrichmentClassifiesContextSourcesAndCreatesSpecBloatRecommendation()
    {
        await using var store = new InMemoryTelemetryStore();
        var importer = new CopilotJsonlImportService(store, SystemClock.Instance);
        var repoPath = await WriteSpecBloatRepoAsync();
        var jsonlPath = await WriteJsonlAsync();
        await importer.ImportAsync(
            new CopilotJsonlImportRequest(jsonlPath, RepoPath: repoPath, RepoFriendlyName: "spec-repo"),
            CancellationToken.None);
        var enrichment = new RepoContextEnrichmentService(store);

        var result = await enrichment.EnrichAsync(new RepoContextEnrichmentRequest(repoPath), CancellationToken.None);
        var insights = await store.ListInsightsAsync(CancellationToken.None);

        Assert.Equal(Assert.Single(store.WorkspaceRepos).WorkspaceRepoId, result.WorkspaceRepoId);
        Assert.True(result.ContextSourceCount >= 6);
        var specStatuses = insights.ContextSources
            .Where(source => source.FileCategory == "spec")
            .Select(source => source.SpecArtifactStatus)
            .ToHashSet();
        Assert.Contains("active", specStatuses);
        Assert.Contains("bloat", specStatuses);
        Assert.Contains("neutral", specStatuses);

        Assert.Contains(insights.ContextSources, source => source.FileCategory == "lockfile" && !source.EligibleForInferredHotspot);
        Assert.Contains(insights.ContextSources, source => source.FileCategory == "binary" && !source.EligibleForInferredHotspot);
        Assert.Contains(insights.ContextSources, source => source.FileCategory == "build_artifact" && !source.EligibleForInferredHotspot);

        var hotspot = Assert.Single(insights.Hotspots);
        Assert.Equal("spec", hotspot.SourceType);
        Assert.Equal("inferred", hotspot.AttributionType);
        Assert.Equal("medium", hotspot.Confidence);
        Assert.Contains("context_source_ids", hotspot.EvidenceRefsJson);

        var recommendation = Assert.Single(hotspot.Recommendations);
        Assert.Equal("deterministic", recommendation.RecommendationType);
        Assert.Equal("rule-2-superseded-spec-bloat", recommendation.RuleId);
        Assert.Contains("active-specs.md", recommendation.RecommendedAction);
        Assert.Contains("specs/archive/", recommendation.RecommendedAction);
        Assert.Contains("load only active specs", recommendation.RecommendedAction);
        Assert.Equal("medium", recommendation.Confidence);
        Assert.NotEmpty(recommendation.ExpectedBenefit);
        Assert.Contains("context_source_ids", recommendation.EvidenceRefsJson);
    }

    [Fact]
    public async Task DashboardInsightsEndpointReturnsContextSourcesHotspotsAndRecommendations()
    {
        await using var store = new InMemoryTelemetryStore();
        var importer = new CopilotJsonlImportService(store, SystemClock.Instance);
        var repoPath = await WriteSpecBloatRepoAsync();
        var jsonlPath = await WriteJsonlAsync();
        await importer.ImportAsync(
            new CopilotJsonlImportRequest(jsonlPath, RepoPath: repoPath, RepoFriendlyName: "spec-repo"),
            CancellationToken.None);
        var enrichment = new RepoContextEnrichmentService(store);
        await enrichment.EnrichAsync(new RepoContextEnrichmentRequest(repoPath), CancellationToken.None);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ITelemetryStore>(store);
        await using var app = builder.Build();
        app.MapDashboardInsightEndpoints();
        await app.StartAsync();

        var client = app.GetTestClient();
        var insights = await client.GetFromJsonAsync<DashboardInsightsResponse>("/insights");

        Assert.NotNull(insights);
        Assert.NotEmpty(insights.ContextSources);
        var hotspot = Assert.Single(insights.Hotspots);
        var recommendation = Assert.Single(hotspot.Recommendations);
        Assert.Equal("rule-2-superseded-spec-bloat", recommendation.RuleId);
    }

    [Fact]
    public async Task DashboardInsightsClientReadsHotspotRecommendation()
    {
        var response = new DashboardInsightsResponse(
            [
                new ContextSourceResponse(
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    "spec",
                    "path-hash",
                    null,
                    "spec",
                    "bloat",
                    true,
                    100,
                    5)
            ],
            [
                new HotspotResponse(
                    Guid.Parse("44444444-4444-4444-4444-444444444444"),
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    "spec",
                    "stale spec artifact set",
                    "inferred",
                    "medium",
                    "Superseded specs remain visible.",
                    "{\"context_source_ids\":[]}",
                    100,
                    [
                        new RecommendationResponse(
                            Guid.Parse("55555555-5555-5555-5555-555555555555"),
                            Guid.Parse("44444444-4444-4444-4444-444444444444"),
                            "deterministic",
                            "rule-2-superseded-spec-bloat",
                            "Spec bloat exists.",
                            "Create active-specs.md, move superseded specs under specs/archive/, and configure agent instructions to load only active specs by default.",
                            "Reduces stale spec context.",
                            "medium",
                            "{\"context_source_ids\":[]}")
                    ])
            ],
            GeneratedAtUtc: new DateTimeOffset(2026, 6, 8, 10, 2, 0, TimeSpan.Zero));

        using var client = new HttpClient(new DashboardStatusTests.JsonResponseHandler<DashboardInsightsResponse>(response))
        {
            BaseAddress = new Uri("https://dashboard-api")
        };
        var insightsClient = new DashboardInsightsClient(client);

        var insights = await insightsClient.GetInsightsAsync(CancellationToken.None);

        var hotspot = Assert.Single(insights.Hotspots);
        Assert.Equal("inferred", hotspot.AttributionType);
        Assert.Equal("rule-2-superseded-spec-bloat", Assert.Single(hotspot.Recommendations).RuleId);
    }

    private static async Task<string> WriteJsonlAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jsonl");
        const string jsonl = """
            {}
            {"resource":{"_rawAttributes":[["deployment.environment","local"],["service.name","github-copilot-vscode"],["service.version","0.51.0"],["session.id","session-1"],["team","team-1"]]}}
            {"_body":"copilot_chat.session.start","hrTime":[1800000000,0],"attributes":{"event.name":"copilot_chat.session.start","session.id":"session-1"},"instrumentationScope":{"name":"github-copilot-vscode","version":"0.51.0"}}
            {"_body":"copilot_chat.agent.turn: 0","hrTime":[1800000001,0],"attributes":{"event.name":"copilot_chat.agent.turn","session.id":"session-1","gen_ai.agent.name":"GitHub Copilot Chat","turn.index":0,"tool_call_count":1,"success":true},"spanContext":{"traceId":"trace-1","spanId":"span-1","traceFlags":1}}
            {"_body":"copilot_chat.tool.call: read_file","hrTime":[1800000002,0],"attributes":{"event.name":"copilot_chat.tool.call","session.id":"session-1","gen_ai.tool.name":"read_file","duration_ms":17,"success":true}}
            {"_body":"GenAI inference: gpt-5.4","hrTime":[1800000003,0],"attributes":{"event.name":"gen_ai.client.inference.operation.details","session.id":"session-1","gen_ai.agent.name":"GitHub Copilot Chat","gen_ai.operation.name":"chat","gen_ai.request.model":"gpt-5.4","gen_ai.response.model":"gpt-5.4-2026-03-05","gen_ai.response.id":"response-1","gen_ai.response.finish_reasons":["stop"],"gen_ai.usage.input_tokens":123,"gen_ai.usage.output_tokens":45}}
            """;

        await File.WriteAllTextAsync(path, jsonl);

        return path;
    }

    private static async Task<string> WriteJsonlWithMissingOutputTokensAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jsonl");
        const string jsonl = """
            {"resource":{"_rawAttributes":[["deployment.environment","local"],["service.name","github-copilot-vscode"],["service.version","0.51.0"],["session.id","session-1"],["team","team-1"]]}}
            {"_body":"copilot_chat.session.start","hrTime":[1800000000,0],"attributes":{"event.name":"copilot_chat.session.start","session.id":"session-1"},"instrumentationScope":{"name":"github-copilot-vscode","version":"0.51.0"}}
            {"_body":"copilot_chat.agent.turn: 0","hrTime":[1800000001,0],"attributes":{"event.name":"copilot_chat.agent.turn","session.id":"session-1","gen_ai.agent.name":"GitHub Copilot Chat","turn.index":0,"tool_call_count":0,"success":true},"spanContext":{"traceId":"trace-1","spanId":"span-1","traceFlags":1}}
            {"_body":"GenAI inference: gpt-5.4","hrTime":[1800000002,0],"attributes":{"event.name":"gen_ai.client.inference.operation.details","session.id":"session-1","gen_ai.agent.name":"GitHub Copilot Chat","gen_ai.operation.name":"chat","gen_ai.request.model":"gpt-5.4","gen_ai.response.model":"gpt-5.4-2026-03-05","gen_ai.response.id":"response-1","gen_ai.response.finish_reasons":["stop"],"gen_ai.usage.input_tokens":123}}
            """;

        await File.WriteAllTextAsync(path, jsonl);

        return path;
    }

    private static async Task<string> WriteSpecBloatRepoAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"spec-bloat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "specs", "active"));
        Directory.CreateDirectory(Path.Combine(root, "specs", "archive"));
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, "bin", "Debug"));

        await File.WriteAllTextAsync(Path.Combine(root, "active-specs.md"), "# Active specs");
        await File.WriteAllTextAsync(Path.Combine(root, "specs", "active", "current-feature.md"), "# Current feature");
        await File.WriteAllTextAsync(Path.Combine(root, "specs", "archive", "superseded-feature.md"), "# Superseded feature");
        await File.WriteAllTextAsync(Path.Combine(root, "specs", "old-deployment-approval.md"), "# Old deployment approval");
        await File.WriteAllTextAsync(Path.Combine(root, "specs", "neutral-background.md"), "# Neutral background");
        await File.WriteAllTextAsync(Path.Combine(root, "src", "Program.cs"), "Console.WriteLine(\"hello\");");
        await File.WriteAllTextAsync(Path.Combine(root, "package-lock.json"), "{}");
        await File.WriteAllBytesAsync(Path.Combine(root, "diagram.png"), [1, 2, 3]);
        await File.WriteAllTextAsync(Path.Combine(root, "bin", "Debug", "build-output.txt"), "artifact");

        return root;
    }
}
