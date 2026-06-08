using System.Text.Json.Serialization;

namespace AiAgentTokenObservability.Contracts.Sessions;

public sealed record DashboardSessionsResponse(
    IReadOnlyList<DashboardSessionSummaryResponse> Sessions,
    DateTimeOffset GeneratedAtUtc);

public sealed record DashboardSessionSummaryResponse(
    Guid AgentSessionId,
    string Harness,
    string? HarnessSource,
    string? HarnessVersion,
    string? AgentName,
    DateTimeOffset? StartedAtUtc,
    TokenSplitResponse TokenSplit,
    int TurnCount,
    int ToolCallCount,
    int ModelInvocationCount,
    int WorkspaceRepoCount);

public sealed record TokenSplitResponse
{
    public TokenSplitResponse(
        string tokenTotalType,
        long? inputTokens,
        long? outputTokens)
        : this(tokenTotalType, inputTokens, outputTokens, [])
    {
    }

    [JsonConstructor]
    public TokenSplitResponse(
        string tokenTotalType,
        long? inputTokens,
        long? outputTokens,
        IReadOnlyList<TokenMetricResponse>? metrics)
    {
        TokenTotalType = tokenTotalType;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        Metrics = metrics ?? [];
    }

    public string TokenTotalType { get; init; }
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public IReadOnlyList<TokenMetricResponse> Metrics { get; init; }
}

public sealed record TokenMetricResponse(
    string MetricName,
    string MetricStatus,
    string MetricConfidence,
    long? Value);
