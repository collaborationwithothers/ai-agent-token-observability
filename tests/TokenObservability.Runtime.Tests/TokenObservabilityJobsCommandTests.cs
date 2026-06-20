using System.Net;
using TokenObservability.Jobs;
using TokenObservability.Domain.Pricing;
using TokenObservability.Domain.Tenancy;
using TokenObservability.Infrastructure.Persistence;

namespace TokenObservability.Runtime.Tests;

public sealed class TokenObservabilityJobsCommandTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] ExpectedCommands =
    [
        "normalize-telemetry",
        "detect-hotspots",
        "generate-recommendations",
        "redact-content",
        "refresh-pricing",
        "retention-cleanup",
        "reprocess-session",
        "tenant-maintenance"
    ];

    [Fact]
    public void TokenObservabilityJobsExposeDocumentedCommandCatalog()
    {
        var actualCommands = JobCommandCatalog.Commands.Select(command => command.Name).ToArray();

        Assert.Equal(ExpectedCommands, actualCommands);
    }

    [Fact]
    public async Task TokenObservabilityJobsListCommandsEntryPointReturnsSuccess()
    {
        using var writer = new StringWriter();

        var exitCode = await TokenObservabilityJobsCommandLine.RunAsync(["--list-commands"], writer);

        Assert.Equal(0, exitCode);
        foreach (var expectedCommand in ExpectedCommands)
        {
            Assert.Contains(expectedCommand, writer.ToString());
        }
    }

    [Fact]
    public void OpenAiPricingParserCreatesTokenTypeCandidatesFromOfficialPricingShape()
    {
        const string pricingPage = """
            Model Input Cached input Output
            gpt-5 $1.25 $0.13 $10.00
            gpt-5-mini $0.25 $0.025 $2.00
            """;

        var candidates = OpenAiPricingPageParser.Parse(
            pricingPage,
            new Uri("https://developers.openai.com/api/docs/pricing"),
            new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero));

        Assert.Contains(candidates, candidate =>
            candidate.ProviderName == "openai" &&
            candidate.ModelName == "gpt-5" &&
            candidate.TokenType == PricingTokenType.Input &&
            candidate.PricePerMillionTokens == 1.25m);
        Assert.Contains(candidates, candidate =>
            candidate.ModelName == "gpt-5" &&
            candidate.TokenType == PricingTokenType.CachedInput &&
            candidate.PricePerMillionTokens == 0.13m);
        Assert.Contains(candidates, candidate =>
            candidate.ModelName == "gpt-5-mini" &&
            candidate.TokenType == PricingTokenType.Output &&
            candidate.PricePerMillionTokens == 2.00m);
        Assert.All(candidates, candidate =>
        {
            Assert.Equal("codex-cli", candidate.Harness);
            Assert.Equal("standard", candidate.BillingRoute);
            Assert.Equal("https://developers.openai.com/api/docs/pricing", candidate.SourceMetadata["source_url"]);
        });
    }

    [Fact]
    public void AzureRetailPricingParserCreatesConsumptionCandidatesWithoutInferringMissingRates()
    {
        const string json = """
            {
              "Items": [
                {
                  "productName": "Azure OpenAI",
                  "skuName": "gpt 4o 1120 Inp global",
                  "meterName": "Input Tokens",
                  "unitOfMeasure": "1K Tokens",
                  "retailPrice": 0.0025,
                  "currencyCode": "USD",
                  "armRegionName": "eastus2",
                  "meterId": "meter-input"
                },
                {
                  "productName": "Azure OpenAI",
                  "skuName": "gpt 4o 1120 Out Data Zone",
                  "meterName": "Output Tokens",
                  "unitOfMeasure": "1K Tokens",
                  "retailPrice": 0.0100,
                  "currencyCode": "USD",
                  "armRegionName": "eastus2",
                  "meterId": "meter-output"
                },
                {
                  "productName": "Azure OpenAI",
                  "skuName": "gpt 4o 1120 Requests",
                  "meterName": "Requests",
                  "unitOfMeasure": "1K Requests",
                  "retailPrice": 1.0,
                  "currencyCode": "USD",
                  "armRegionName": "eastus2"
                }
              ],
              "NextPageLink": null
            }
            """;

        var result = AzureOpenAiRetailPricingParser.Parse(
            json,
            new Uri("https://prices.azure.com/api/retail/prices"),
            new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(2, result.Candidates.Count);
        Assert.Contains(result.Candidates, candidate =>
            candidate.ProviderName == "azure_openai" &&
            candidate.ModelName == "gpt-4o-1120" &&
            candidate.TokenType == PricingTokenType.Input &&
            candidate.BillingRoute == "global_standard" &&
            candidate.PricePerMillionTokens == 2.5m);
        Assert.Contains(result.Candidates, candidate =>
            candidate.TokenType == PricingTokenType.Output &&
            candidate.BillingRoute == "data_zone" &&
            candidate.PricePerMillionTokens == 10.0m);
    }

    [Fact]
    public async Task RefreshPricingCommandCreatesTenantScopedCandidateRecords()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var organization = await store.CreateCustomerOrganizationAsync(
            new CreateCustomerOrganizationRequest(
                "contoso",
                "Contoso",
                "uksouth",
                CustomerOrganizationIsolationTier.Shared));
        using var httpClient = new HttpClient(new StubHttpMessageHandler("""
            Model Input Cached input Output
            gpt-5 $1.25 $0.13 $10.00
            """));
        var service = new ProviderPricingRefreshService(httpClient);
        using var writer = new StringWriter();

        var exitCode = await TokenObservabilityJobsCommandLine.RunAsync(
            [
                "refresh-pricing",
                "--provider",
                "openai",
                "--customer-organization-id",
                organization.CustomerOrganizationId.ToString(),
                "--correlation-id",
                "pricing-refresh-test"
            ],
            writer,
            store,
            service);

        Assert.Equal(0, exitCode);
        Assert.Contains("Created 3 tenant-scoped pricing basis candidate record(s).", writer.ToString());
        var records = await store.ListPricingBasisRecordsAsync(organization.CustomerOrganizationId);
        Assert.Equal(3, records.Count);
        Assert.All(records, record =>
        {
            Assert.Equal(PricingSourceKind.AutomatedSeed, record.SourceKind);
            Assert.Equal(PricingReviewState.Candidate, record.ReviewState);
        });
        Assert.DoesNotContain(records, record => record.ReviewState == PricingReviewState.Approved);
        var auditEvents = await store.ListGovernanceAuditEventsAsync(organization.CustomerOrganizationId);
        Assert.Equal(3, auditEvents.Count(audit => audit.EvidenceMetadata["operation"] == "pricing_seed_refresh"));
    }

    [Fact]
    public async Task RefreshPricingCommandLoadsCustomerOrganizationBeforeSeedingCandidates()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var customerOrganizationId = CustomerOrganizationId.NewId();
        using var httpClient = new HttpClient(new StubHttpMessageHandler("""
            Model Input Cached input Output
            gpt-5 $1.25 $0.13 $10.00
            """));
        var service = new ProviderPricingRefreshService(httpClient);
        using var writer = new StringWriter();

        var exitCode = await TokenObservabilityJobsCommandLine.RunAsync(
            [
                "refresh-pricing",
                "--provider",
                "openai",
                "--customer-organization-id",
                customerOrganizationId.ToString(),
                "--customer-organization-slug",
                "contoso",
                "--customer-organization-display-name",
                "Contoso",
                "--data-residency-region",
                "uksouth"
            ],
            writer,
            store,
            service);

        Assert.Equal(0, exitCode);
        var organization = await store.FindCustomerOrganizationAsync(customerOrganizationId);
        Assert.NotNull(organization);
        Assert.Equal("contoso", organization.Slug);
        Assert.Equal(3, (await store.ListPricingBasisRecordsAsync(customerOrganizationId)).Count);
    }

    [Fact]
    public async Task RefreshPricingCommandRejectsUnloadedCustomerWithoutSlug()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        using var writer = new StringWriter();

        var exitCode = await TokenObservabilityJobsCommandLine.RunAsync(
            [
                "refresh-pricing",
                "--provider",
                "openai",
                "--customer-organization-id",
                CustomerOrganizationId.NewId().ToString()
            ],
            writer,
            store,
            new ProviderPricingRefreshService(new HttpClient(new StubHttpMessageHandler(string.Empty))));

        Assert.Equal(2, exitCode);
        Assert.Contains("requires --customer-organization-slug", writer.ToString());
    }

    [Fact]
    public async Task RefreshPricingCommandReportsProviderFetchFailureWithoutActiveChanges()
    {
        var store = new InMemoryTenantMetadataStore(new StaticTenantMetadataClock(Now));
        var organization = await store.CreateCustomerOrganizationAsync(
            new CreateCustomerOrganizationRequest(
                "contoso",
                "Contoso",
                "uksouth",
                CustomerOrganizationIsolationTier.Shared));
        using var writer = new StringWriter();

        var exitCode = await TokenObservabilityJobsCommandLine.RunAsync(
            [
                "refresh-pricing",
                "--provider",
                "unsupported",
                "--customer-organization-id",
                organization.CustomerOrganizationId.ToString()
            ],
            writer,
            store);

        Assert.Equal(2, exitCode);
        Assert.Contains("No active pricing basis or cost estimate was changed.", writer.ToString());
        Assert.Empty(await store.ListPricingBasisRecordsAsync(organization.CustomerOrganizationId));
    }

    private sealed class StubHttpMessageHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }
    }
}
