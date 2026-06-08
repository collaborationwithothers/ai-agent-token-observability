using System.Net.Http.Json;
using AiAgentTokenObservability.Contracts.Sessions;

namespace AiAgentTokenObservability.Dashboard.Web.Sessions;

public sealed class DashboardSessionsClient(HttpClient httpClient)
{
    public async Task<DashboardSessionsResponse> GetSessionsAsync(CancellationToken cancellationToken)
    {
        var sessions = await httpClient.GetFromJsonAsync<DashboardSessionsResponse>("/sessions", cancellationToken);

        return sessions ?? throw new InvalidOperationException("Dashboard API returned an empty sessions response.");
    }
}
