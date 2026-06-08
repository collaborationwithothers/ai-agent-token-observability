using AiAgentTokenObservability.Storage.Infrastructure;
using AiAgentTokenObservability.Storage.Enrichment;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AiAgentTokenObservability.Storage;

public static class TelemetryStorageServiceCollectionExtensions
{
    public static IServiceCollection AddTelemetryStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ISystemClock>(SystemClock.Instance);
        services.AddSingleton(_ =>
        {
            var connectionString = configuration.GetConnectionString("tokenobservability");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("Connection string 'tokenobservability' is not configured.");
            }

            return NpgsqlDataSource.Create(connectionString);
        });
        services.AddSingleton<ITelemetryStore, PostgresTelemetryStore>();
        services.AddSingleton<RepoContextEnrichmentService>();

        return services;
    }
}
