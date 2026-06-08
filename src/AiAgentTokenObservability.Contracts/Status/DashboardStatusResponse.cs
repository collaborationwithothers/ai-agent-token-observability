namespace AiAgentTokenObservability.Contracts.Status;

public sealed record DashboardStatusResponse(
    string PlatformName,
    string EnvironmentName,
    string StoreName,
    string[] PipelineProjects,
    DateTimeOffset GeneratedAtUtc);
