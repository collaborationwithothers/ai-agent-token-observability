using System.Net.Http.Json;
using AiAgentTokenObservability.Contracts.Status;

namespace AiAgentTokenObservability.Dashboard.Web.Status;

public sealed class DashboardStatusClient(HttpClient httpClient)
{
    public async Task<DashboardStatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        var status = await httpClient.GetFromJsonAsync<DashboardStatusResponse>("/status", cancellationToken);

        return status ?? throw new InvalidOperationException("Dashboard API returned an empty status response.");
    }
}
