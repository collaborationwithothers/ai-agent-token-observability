using System.Net.Http.Json;
using AiAgentTokenObservability.Contracts.Insights;

namespace AiAgentTokenObservability.Dashboard.Web.Insights;

public sealed class DashboardInsightsClient(HttpClient httpClient)
{
    public async Task<DashboardInsightsResponse> GetInsightsAsync(CancellationToken cancellationToken)
    {
        return await httpClient.GetFromJsonAsync<DashboardInsightsResponse>("/insights", cancellationToken)
            ?? new DashboardInsightsResponse([], [], DateTimeOffset.UtcNow);
    }
}
