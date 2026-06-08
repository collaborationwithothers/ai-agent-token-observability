using AiAgentTokenObservability.Storage;

namespace AiAgentTokenObservability.Dashboard.Api.Insights;

public static class DashboardInsightEndpoints
{
    public static IEndpointRouteBuilder MapDashboardInsightEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/insights", async (ITelemetryStore store, CancellationToken cancellationToken) =>
            await store.ListInsightsAsync(cancellationToken))
            .WithName("GetDashboardInsights");

        return endpoints;
    }
}
