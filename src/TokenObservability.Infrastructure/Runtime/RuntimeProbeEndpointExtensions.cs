using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace TokenObservability.Infrastructure.Runtime;

public static class RuntimeProbeEndpointExtensions
{
    public static void AddRuntimeProbeServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks();
    }

    public static void MapRuntimeProbeEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health/live");
        app.MapHealthChecks("/health/ready");
    }
}
