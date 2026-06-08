using System.Net;
using System.Net.Http.Json;
using AiAgentTokenObservability.Contracts.Status;
using AiAgentTokenObservability.Dashboard.Api.Status;
using AiAgentTokenObservability.Dashboard.Web.Status;
using Microsoft.Extensions.Configuration;

namespace AiAgentTokenObservability.Tests;

public sealed class DashboardStatusTests
{
    [Fact]
    public async Task DashboardStatusClientReadsApiStatus()
    {
        var response = new DashboardStatusResponse(
            PlatformName: "Local App Platform",
            EnvironmentName: "Development",
            StoreName: "tokenobservability",
            PipelineProjects:
            [
                "ingestion-worker",
                "dashboard-api",
                "dashboard-web"
            ],
            GeneratedAtUtc: new DateTimeOffset(2026, 6, 8, 10, 0, 0, TimeSpan.Zero));

        using var client = new HttpClient(new JsonResponseHandler<DashboardStatusResponse>(response))
        {
            BaseAddress = new Uri("https://dashboard-api")
        };
        var statusClient = new DashboardStatusClient(client);

        var status = await statusClient.GetStatusAsync(CancellationToken.None);

        Assert.Equal("Local App Platform", status.PlatformName);
        Assert.Equal("Development", status.EnvironmentName);
        Assert.Equal("tokenobservability", status.StoreName);
        Assert.Equal(["ingestion-worker", "dashboard-api", "dashboard-web"], status.PipelineProjects);
    }

    [Fact]
    public void DashboardStatusServiceUsesConfiguration()
    {
        var options = new LocalPlatformStatusOptions
        {
            PlatformName = "Local App Platform",
            StoreName = "tokenobservability",
            PipelineProjects =
            [
                "ingestion-worker",
                "dashboard-api",
                "dashboard-web"
            ]
        };
        var service = new DashboardStatusService(options, "Development");

        var status = service.GetStatus();

        Assert.Equal("Local App Platform", status.PlatformName);
        Assert.Equal("Development", status.EnvironmentName);
        Assert.Equal("tokenobservability", status.StoreName);
        Assert.Equal(["ingestion-worker", "dashboard-api", "dashboard-web"], status.PipelineProjects);
    }

    [Fact]
    public void LocalPlatformConfigurationDoesNotDuplicatePipelineProjects()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LocalPlatform:PlatformName"] = "Local App Platform",
                ["LocalPlatform:StoreName"] = "tokenobservability",
                ["LocalPlatform:PipelineProjects:0"] = "ingestion-worker",
                ["LocalPlatform:PipelineProjects:1"] = "dashboard-api",
                ["LocalPlatform:PipelineProjects:2"] = "dashboard-web"
            })
            .Build();
        var options = new LocalPlatformStatusOptions();

        configuration.GetSection("LocalPlatform").Bind(options);

        Assert.Equal(["ingestion-worker", "dashboard-api", "dashboard-web"], options.PipelineProjects);
    }

    internal sealed class JsonResponseHandler<T>(T response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var message = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(response)
            };

            return Task.FromResult(message);
        }
    }
}
