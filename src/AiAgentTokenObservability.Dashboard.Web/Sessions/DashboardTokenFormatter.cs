namespace AiAgentTokenObservability.Dashboard.Web.Sessions;

public static class DashboardTokenFormatter
{
    public static string FormatTokens(long? value)
    {
        return value?.ToString("N0") ?? "Unavailable";
    }
}
