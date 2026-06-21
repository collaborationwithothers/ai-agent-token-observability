using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TokenObservability.Domain.Ingestion;
using TokenObservability.Domain.Tenancy;
using TokenObservability.Api;
using TokenObservability.Infrastructure.Ingestion;
using TokenObservability.Infrastructure.Persistence;
using TokenObservability.Ingestion;

namespace TokenObservability.Runtime.Tests;

public sealed class TokenObservabilityRuntimeHealthTests
{
    public static TheoryData<string, WebApplicationFactory<TokenObservabilityApiAssemblyMarker>> TokenObservabilityApiProbeFactories()
    {
        return new TheoryData<string, WebApplicationFactory<TokenObservabilityApiAssemblyMarker>>
        {
            { "/health/live", new WebApplicationFactory<TokenObservabilityApiAssemblyMarker>() },
            { "/healthz", new WebApplicationFactory<TokenObservabilityApiAssemblyMarker>() },
            { "/api/v1/system/health", new WebApplicationFactory<TokenObservabilityApiAssemblyMarker>() }
        };
    }

    public static TheoryData<string, WebApplicationFactory<TokenObservabilityIngestionAssemblyMarker>> TokenObservabilityIngestionFactories()
    {
        return new TheoryData<string, WebApplicationFactory<TokenObservabilityIngestionAssemblyMarker>>
        {
            { "/health/live", new WebApplicationFactory<TokenObservabilityIngestionAssemblyMarker>() }
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
        using var healthReadyResponse = await client.GetAsync("/health/ready");

        response.EnsureSuccessStatusCode();
        healthReadyResponse.EnsureSuccessStatusCode();
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
    public async Task TokenObservabilityIngestionReadinessFailsClosedWhenDependenciesAreNotConfigured()
    {
        using var factory = new WebApplicationFactory<TokenObservabilityIngestionAssemblyMarker>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("scoped_ingestion_credentials", content);
        Assert.Contains("telemetry_acceptance", content);
        Assert.Contains("not_configured", content);
        Assert.DoesNotContain("connection", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TokenObservabilityIngestionReadinessCanSucceedWhenConfigured()
    {
        using var factory = CreateIngestionReadinessFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/readyz");
        using var healthReadyResponse = await client.GetAsync("/health/ready");

        response.EnsureSuccessStatusCode();
        healthReadyResponse.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("tenant_metadata_store", content);
        Assert.Contains("aggregate_metric_sink", content);
    }

    [Fact]
    public void ProductDashboardReadinessProxiesProductApiReadinessAndFailsClosedWithoutApiOrigin()
    {
        var root = FindRepositoryRoot();
        var dashboardRoot = Path.Combine(root, "web", "token-observability-dashboard");
        var nginxConfig = File.ReadAllText(Path.Combine(dashboardRoot, "nginx.conf"));
        var dockerEntrypoint = File.ReadAllText(Path.Combine(dashboardRoot, "docker-entrypoint.sh"));
        var appRuntimeLocals = File.ReadAllText(Path.Combine(root, "infrastructure", "azure", "stages", "app_runtime", "locals.tf"));

        Assert.Contains("include /tmp/tokenobs-dashboard-readiness.conf", nginxConfig);
        Assert.Contains("location = /readyz", dockerEntrypoint);
        Assert.Contains("proxy_pass ${readiness_origin}/readyz", dockerEntrypoint);
        Assert.Contains("\"status\":\"not_ready\"", dockerEntrypoint);
        Assert.Contains("startup_path", appRuntimeLocals);
        Assert.Contains("\"/healthz\"", appRuntimeLocals);
        Assert.Contains("liveness_path", appRuntimeLocals);
        Assert.Contains("readiness_path", appRuntimeLocals);
        Assert.Contains("startup_probe", File.ReadAllText(Path.Combine(root, "infrastructure", "azure", "stages", "app_runtime", "main.tf")));
    }

    [Fact]
    public async Task TokenObservabilityApiReadinessCanFailWithoutLeakingConfiguration()
    {
        using var factory = CreateReadinessFactory(("TOKENOBSERVABILITY_STORAGE_ACCOUNT_NAME", null));
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
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL", EncodePrincipal());
        using var missingTenantResponse = await client.SendAsync(request);

        using var invalidAuthRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        invalidAuthRequest.Headers.Authorization = new("Bearer", "placeholder");
        invalidAuthRequest.Headers.Add("X-Customer-Organization-Slug", "org-1");
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
            ["CUSTOMER_ORGANIZATION_SLUG"] = "contoso",
            ["ProductMetadataStore:ConnectionString"] = "Host=postgresql.internal;Database=product_metadata;Username=readiness;Password=not-used",
            ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = "InstrumentationKey=00000000-0000-0000-0000-000000000000",
            ["TOKENOBSERVABILITY_STORAGE_ACCOUNT_NAME"] = "sttokenobservability",
            ["TOKENOBSERVABILITY_RECOMMENDATION_DEPLOYMENT_COUNT"] = "1"
        };

        foreach (var (key, value) in overrides)
        {
            settings[key] = value;
        }

        return new WebApplicationFactory<TokenObservabilityApiAssemblyMarker>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(settings);
                });
                builder.ConfigureServices(services =>
                {
                    var store = CreateReadyTenantStore();
                    services.RemoveAll<InMemoryTenantMetadataStore>();
                    services.RemoveAll<ITenantMetadataStore>();
                    services.RemoveAll<IProductApiIdempotencyStore>();
                    services.AddSingleton(store);
                    services.AddSingleton<ITenantMetadataStore>(store);
                    services.AddSingleton<IProductApiIdempotencyStore>(store);
                });
            });
    }

    private static WebApplicationFactory<TokenObservabilityIngestionAssemblyMarker> CreateIngestionReadinessFactory(
        params (string Key, string? Value)[] overrides)
    {
        var settings = new Dictionary<string, string?>
        {
            ["CUSTOMER_ORGANIZATION_SLUG"] = "contoso",
            ["TOKENOBSERVABILITY_POSTGRESQL_SERVER_FQDN"] = "postgresql.internal",
            ["TOKENOBSERVABILITY_POSTGRESQL_DATABASE_NAME"] = "product_metadata"
        };

        foreach (var (key, value) in overrides)
        {
            settings[key] = value;
        }

        return new WebApplicationFactory<TokenObservabilityIngestionAssemblyMarker>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(settings);
                });
                builder.ConfigureServices(services =>
                {
                    var store = CreateReadyTenantStore();
                    services.RemoveAll<InMemoryTenantMetadataStore>();
                    services.RemoveAll<IAggregateMetricSink>();
                    services.AddSingleton(store);
                    services.AddSingleton<IAggregateMetricSink, RecordingAggregateMetricSink>();
                });
            });
    }

    private static InMemoryTenantMetadataStore CreateReadyTenantStore()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(DateTimeOffset.Parse("2026-06-21T00:00:00Z")));
        store.CreateCustomerOrganizationAsync(new CreateCustomerOrganizationRequest(
            "contoso",
            "Contoso",
            "eastus2",
            CustomerOrganizationIsolationTier.Shared)).GetAwaiter().GetResult();
        return store;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AiAgentTokenObservability.Production.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static async Task AssertProblemCodeAsync(HttpResponseMessage response, string expectedCode)
    {
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(expectedCode, body.RootElement.GetProperty("code").GetString());
    }

    private static string EncodePrincipal()
    {
        var principal = new
        {
            claims = new[]
            {
                new { typ = "iss", val = "https://sts.windows.net/contoso-tenant/" },
                new { typ = "tid", val = "contoso-tenant" },
                new { typ = "aud", val = "api://token-observability" },
                new { typ = "sub", val = "admin-subject" },
                new { typ = "name", val = "admin-subject" }
            }
        };

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(principal)));
    }

    private sealed class RecordingAggregateMetricSink : IAggregateMetricSink
    {
        public Task ExportAsync(
            IReadOnlyList<AggregateMetricPoint> points,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
