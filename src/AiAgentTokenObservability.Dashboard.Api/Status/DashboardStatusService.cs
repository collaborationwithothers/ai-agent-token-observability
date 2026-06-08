using AiAgentTokenObservability.Contracts.Status;

namespace AiAgentTokenObservability.Dashboard.Api.Status;

public sealed class DashboardStatusService(LocalPlatformStatusOptions options, string environmentName)
{
    public DashboardStatusResponse GetStatus()
    {
        return new DashboardStatusResponse(
            options.PlatformName,
            environmentName,
            options.StoreName,
            options.PipelineProjects,
            DateTimeOffset.UtcNow);
    }
}
