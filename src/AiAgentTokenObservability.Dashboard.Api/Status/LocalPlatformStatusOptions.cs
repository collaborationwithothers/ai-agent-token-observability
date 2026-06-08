namespace AiAgentTokenObservability.Dashboard.Api.Status;

public sealed class LocalPlatformStatusOptions
{
    public string PlatformName { get; init; } = "Local App Platform";

    public string StoreName { get; init; } = "tokenobservability";

    public string[] PipelineProjects { get; init; } = [];
}
