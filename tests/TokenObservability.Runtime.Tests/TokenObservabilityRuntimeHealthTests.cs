using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using TokenObservability.Api;
using TokenObservability.Ingestion;

namespace TokenObservability.Runtime.Tests;

public sealed class TokenObservabilityRuntimeHealthTests
{
    public static TheoryData<string, WebApplicationFactory<TokenObservabilityApiAssemblyMarker>> TokenObservabilityApiProbeFactories()
    {
        return new TheoryData<string, WebApplicationFactory<TokenObservabilityApiAssemblyMarker>>
        {
            { "/health/live", new WebApplicationFactory<TokenObservabilityApiAssemblyMarker>() },
            { "/health/ready", new WebApplicationFactory<TokenObservabilityApiAssemblyMarker>() },
            { "/healthz", new WebApplicationFactory<TokenObservabilityApiAssemblyMarker>() },
            { "/api/v1/system/health", new WebApplicationFactory<TokenObservabilityApiAssemblyMarker>() }
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
    [MemberData(nameof(TokenObservabilityApiProbeFactories))]
    public async Task TokenObservabilityApiExposesProbeEndpoint(string path, WebApplicationFactory<TokenObservabilityApiAssemblyMarker> factory)
    {
        using var disposableFactory = factory;
        using var client = disposableFactory.CreateClient();

        using var response = await client.GetAsync(path);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task TokenObservabilityApiUsesVersionedRoutePrefix()
    {
        using var factory = new WebApplicationFactory<TokenObservabilityApiAssemblyMarker>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1");

        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("v1", body.RootElement.GetProperty("apiVersion").GetString());
        Assert.Equal("token-observability-api", body.RootElement.GetProperty("service").GetString());
    }

    [Fact]
    public async Task TokenObservabilityApiRejectsUnversionedApiRoute()
    {
        using var factory = new WebApplicationFactory<TokenObservabilityApiAssemblyMarker>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TokenObservabilityApiReadinessProbeAliasCanSucceedWhenConfigured()
    {
        using var factory = CreateReadinessFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/readyz");

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("product_metadata_store", content);
        Assert.Contains("authorization_enforcement", content);
    }

    [Fact]
    public async Task TokenObservabilityApiReadinessFailsClosedWhenDependenciesAreNotConfigured()
    {
        using var factory = new WebApplicationFactory<TokenObservabilityApiAssemblyMarker>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/readyz");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("not_configured", content);
        Assert.DoesNotContain("connection", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TokenObservabilityApiReadinessCanFailWithoutLeakingConfiguration()
    {
        using var factory = CreateReadinessFactory(("ProductApi:Readiness:ProductMetadataStore", "false"));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/readyz");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("product_metadata_store", content);
        Assert.DoesNotContain("connection", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TokenObservabilityApiVersionedReadinessRejectsAnonymousCaller()
    {
        using var factory = CreateReadinessFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/system/readiness");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertProblemCodeAsync(response, "authentication_required");
    }

    [Fact]
    public async Task TokenObservabilityApiProtectedRouteRejectsMissingInvalidAuthAndTenantContext()
    {
        using var factory = new WebApplicationFactory<TokenObservabilityApiAssemblyMarker>();
        using var client = factory.CreateClient();

        using var missingAuthResponse = await client.GetAsync("/api/v1/me");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new("Bearer", "placeholder");
        using var missingTenantResponse = await client.SendAsync(request);

        using var invalidAuthRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        invalidAuthRequest.Headers.Authorization = new("Bearer", "placeholder");
        invalidAuthRequest.Headers.Add("X-Customer-Organization-Id", "org-1");
        using var invalidAuthResponse = await client.SendAsync(invalidAuthRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, missingAuthResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, missingTenantResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidAuthResponse.StatusCode);
        await AssertProblemCodeAsync(missingAuthResponse, "authentication_required");
        await AssertProblemCodeAsync(missingTenantResponse, "tenant_context_required");
        await AssertProblemCodeAsync(invalidAuthResponse, "authentication_required");
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

    private static WebApplicationFactory<TokenObservabilityApiAssemblyMarker> CreateReadinessFactory(
        params (string Key, string? Value)[] overrides)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ProductApi:Readiness:ProductMetadataStore"] = "true",
            ["ProductApi:Readiness:TelemetryBackends"] = "true",
            ["ProductApi:Readiness:ContentStore"] = "true",
            ["ProductApi:Readiness:RecommendationDependencies"] = "true",
            ["ProductApi:Readiness:AuthorizationEnforcement"] = "true"
        };

        foreach (var (key, value) in overrides)
        {
            settings[key] = value;
        }

        return new WebApplicationFactory<TokenObservabilityApiAssemblyMarker>()
            .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(settings);
            }));
    }

    private static async Task AssertProblemCodeAsync(HttpResponseMessage response, string expectedCode)
    {
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(expectedCode, body.RootElement.GetProperty("code").GetString());
    }
}
