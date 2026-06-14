using Microsoft.AspNetCore.Mvc.Testing;
using TokenObservability.Api;
using TokenObservability.Ingestion;

namespace TokenObservability.Runtime.Tests;

public sealed class TokenObservabilityRuntimeHealthTests
{
    public static TheoryData<string, WebApplicationFactory<TokenObservabilityApiAssemblyMarker>> TokenObservabilityApiFactories()
    {
        return new TheoryData<string, WebApplicationFactory<TokenObservabilityApiAssemblyMarker>>
        {
            { "/health/live", new WebApplicationFactory<TokenObservabilityApiAssemblyMarker>() },
            { "/health/ready", new WebApplicationFactory<TokenObservabilityApiAssemblyMarker>() }
        };
    }

    public static TheoryData<string, WebApplicationFactory<TokenObservabilityIngestionAssemblyMarker>> TokenObservabilityIngestionFactories()
    {
        return new TheoryData<string, WebApplicationFactory<TokenObservabilityIngestionAssemblyMarker>>
        {
            { "/health/live", new WebApplicationFactory<TokenObservabilityIngestionAssemblyMarker>() },
            { "/health/ready", new WebApplicationFactory<TokenObservabilityIngestionAssemblyMarker>() }
        };
    }

    [Theory]
    [MemberData(nameof(TokenObservabilityApiFactories))]
    public async Task TokenObservabilityApiExposesProbeEndpoint(string path, WebApplicationFactory<TokenObservabilityApiAssemblyMarker> factory)
    {
        using var disposableFactory = factory;
        using var client = disposableFactory.CreateClient();

        using var response = await client.GetAsync(path);

        response.EnsureSuccessStatusCode();
    }

    [Theory]
    [MemberData(nameof(TokenObservabilityIngestionFactories))]
    public async Task TokenObservabilityIngestionExposesProbeEndpoint(string path, WebApplicationFactory<TokenObservabilityIngestionAssemblyMarker> factory)
    {
        using var disposableFactory = factory;
        using var client = disposableFactory.CreateClient();

        using var response = await client.GetAsync(path);

        response.EnsureSuccessStatusCode();
    }
}
