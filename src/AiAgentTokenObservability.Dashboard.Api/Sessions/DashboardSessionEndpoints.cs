using AiAgentTokenObservability.Storage;

namespace AiAgentTokenObservability.Dashboard.Api.Sessions;

public static class DashboardSessionEndpoints
{
    public static IEndpointRouteBuilder MapDashboardSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/sessions", async (ITelemetryStore store, CancellationToken cancellationToken) =>
            await store.ListSessionSummariesAsync(cancellationToken))
            .WithName("GetDashboardSessions");

        return endpoints;
    }
}
