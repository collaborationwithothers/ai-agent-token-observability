using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TokenObservability.Domain.Authorization;
using TokenObservability.Domain.Pricing;
using TokenObservability.Domain.Tenancy;
using TokenObservability.Infrastructure.Persistence;

namespace TokenObservability.Jobs;

public sealed record ProviderPricingSeedCandidate(
    string Harness,
    string ProviderName,
    string ModelName,
    PricingTokenType TokenType,
    string BillingRoute,
    string Currency,
    decimal PricePerMillionTokens,
    string PricingVersion,
    IReadOnlyDictionary<string, string> SourceMetadata);

public sealed class ProviderPricingRefreshService(HttpClient httpClient)
{
    private static readonly Uri DefaultOpenAiPricingUri = new("https://developers.openai.com/api/docs/pricing");
    private static readonly Uri DefaultAzureRetailPricingUri = new("https://prices.azure.com/api/retail/prices?$filter=productName%20eq%20%27Azure%20OpenAI%27%20and%20priceType%20eq%20%27Consumption%27&api-version=2023-01-01-preview");

    public async Task<IReadOnlyList<ProviderPricingSeedCandidate>> FetchCandidatesAsync(
        string providerName,
        CancellationToken cancellationToken)
    {
        var normalizedProvider = providerName.Trim().ToLowerInvariant();
        return normalizedProvider switch
        {
            "openai" => await FetchOpenAiCandidatesAsync(DefaultOpenAiPricingUri, cancellationToken),
            "azure_openai" => await FetchAzureOpenAiCandidatesAsync(DefaultAzureRetailPricingUri, cancellationToken),
            _ => throw new ArgumentException("Pricing provider is not supported.", nameof(providerName))
        };
    }

    public async Task<IReadOnlyList<ProviderPricingSeedCandidate>> FetchOpenAiCandidatesAsync(
        Uri sourceUri,
        CancellationToken cancellationToken)
    {
        var retrievedAtUtc = DateTimeOffset.UtcNow;
        using var response = await httpClient.GetAsync(sourceUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return OpenAiPricingPageParser.Parse(content, sourceUri, retrievedAtUtc);
    }

    public async Task<IReadOnlyList<ProviderPricingSeedCandidate>> FetchAzureOpenAiCandidatesAsync(
        Uri sourceUri,
        CancellationToken cancellationToken)
    {
        var retrievedAtUtc = DateTimeOffset.UtcNow;
        var candidates = new List<ProviderPricingSeedCandidate>();
        var next = sourceUri;

        while (next is not null)
        {
            using var response = await httpClient.GetAsync(next, cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var page = AzureOpenAiRetailPricingParser.Parse(json, next, retrievedAtUtc);
            candidates.AddRange(page.Candidates);
            next = page.NextPageLink;
        }

        return candidates;
    }

    public async Task<IReadOnlyList<PricingBasisRecord>> CreateCandidateRecordsAsync(
        InMemoryTenantMetadataStore tenantMetadataStore,
        CustomerOrganizationId customerOrganizationId,
        IReadOnlyList<ProviderPricingSeedCandidate> candidates,
        string correlationId)
    {
        ArgumentNullException.ThrowIfNull(tenantMetadataStore);
        ArgumentNullException.ThrowIfNull(candidates);

        var created = new List<PricingBasisRecord>(candidates.Count);

        foreach (var candidate in candidates)
        {
            var auditEventId = $"audit-pricing-seed-{Guid.NewGuid():N}";
            await tenantMetadataStore.RecordGovernanceAuditEventAsync(
                customerOrganizationId,
                new CreateGovernanceAuditEventRequest(
                    auditEventId,
                    ActorProductUserId: null,
                    EffectiveRole: null,
                    ProductAuthorizationAction.PricingManage,
                    new ProductScope(ProductScopeKind.Pricing, ScopeId: "pricing-refresh"),
                    Decision: "created",
                    DenialReason: null,
                    correlationId,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["evidence_kind"] = "admin_operation",
                        ["operation"] = "pricing_seed_refresh",
                        ["result"] = "candidate",
                        ["pricing_basis_id"] = "pending",
                        ["pricing_version"] = candidate.PricingVersion,
                        ["provider_name"] = candidate.ProviderName,
                        ["model_name"] = candidate.ModelName,
                        ["token_type"] = InMemoryTenantMetadataStore.ToWirePricingTokenType(candidate.TokenType),
                        ["billing_route"] = candidate.BillingRoute,
                        ["source_kind"] = "automated_seed",
                        ["review_state"] = "candidate"
                    }));

            var record = await tenantMetadataStore.CreatePricingBasisRecordAsync(
                new CreatePricingBasisRecordRequest(
                    customerOrganizationId,
                    candidate.Harness,
                    candidate.ProviderName,
                    candidate.ModelName,
                    candidate.TokenType,
                    candidate.BillingRoute,
                    candidate.Currency,
                    candidate.PricePerMillionTokens,
                    candidate.PricingVersion,
                    PricingSourceKind.AutomatedSeed,
                    PricingReviewState.Candidate,
                    DateTimeOffset.UtcNow,
                    EffectiveToUtc: null,
                    auditEventId,
                    candidate.SourceMetadata));

            created.Add(record);
        }

        return created;
    }
}

public static class OpenAiPricingPageParser
{
    private static readonly Regex RowRegex = new(
        @"(?<model>gpt[-\w.]+|o\d(?:[-\w.]+)?|chat-latest)\s+\$?(?<input>\d+(?:\.\d+)?)\s+\$?(?<cached>\d+(?:\.\d+)|-)\s+\$?(?<output>\d+(?:\.\d+)?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<ProviderPricingSeedCandidate> Parse(
        string pricingPage,
        Uri sourceUri,
        DateTimeOffset retrievedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(pricingPage);
        ArgumentNullException.ThrowIfNull(sourceUri);

        var candidates = new List<ProviderPricingSeedCandidate>();
        foreach (Match match in RowRegex.Matches(pricingPage))
        {
            var model = match.Groups["model"].Value;
            var input = decimal.Parse(match.Groups["input"].Value, CultureInfo.InvariantCulture);
            var cached = match.Groups["cached"].Value;
            var output = decimal.Parse(match.Groups["output"].Value, CultureInfo.InvariantCulture);
            var metadata = CreateSourceMetadata(
                sourceUri,
                retrievedAtUtc,
                providerDocumentVersion: "openai-api-pricing",
                providerSkuName: model,
                billingRoute: "standard");

            candidates.Add(CreateOpenAiCandidate(model, PricingTokenType.Input, input, metadata));
            if (!StringComparer.Ordinal.Equals(cached, "-"))
            {
                candidates.Add(CreateOpenAiCandidate(
                    model,
                    PricingTokenType.CachedInput,
                    decimal.Parse(cached, CultureInfo.InvariantCulture),
                    metadata));
            }

            candidates.Add(CreateOpenAiCandidate(model, PricingTokenType.Output, output, metadata));
        }

        return Deduplicate(candidates);
    }

    private static ProviderPricingSeedCandidate CreateOpenAiCandidate(
        string model,
        PricingTokenType tokenType,
        decimal pricePerMillionTokens,
        IReadOnlyDictionary<string, string> sourceMetadata)
    {
        return new ProviderPricingSeedCandidate(
            Harness: "codex-cli",
            ProviderName: "openai",
            model,
            tokenType,
            BillingRoute: "standard",
            Currency: "USD",
            pricePerMillionTokens,
            PricingVersion: $"openai-{DateTimeOffset.UtcNow:yyyyMMdd}",
            sourceMetadata);
    }

    private static IReadOnlyList<ProviderPricingSeedCandidate> Deduplicate(
        IReadOnlyList<ProviderPricingSeedCandidate> candidates)
    {
        return candidates
            .GroupBy(candidate => new
            {
                candidate.ProviderName,
                candidate.ModelName,
                candidate.TokenType,
                candidate.BillingRoute
            })
            .Select(static group => group.First())
            .ToArray();
    }

    internal static IReadOnlyDictionary<string, string> CreateSourceMetadata(
        Uri sourceUri,
        DateTimeOffset retrievedAtUtc,
        string providerDocumentVersion,
        string providerSkuName,
        string billingRoute)
    {
        return new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["source_url"] = sourceUri.ToString(),
                ["source_retrieved_at_utc"] = retrievedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                ["provider_document_version"] = providerDocumentVersion,
                ["provider_sku_name"] = providerSkuName,
                ["billing_route"] = billingRoute
            });
    }
}

public sealed record AzureRetailPricingParseResult(
    IReadOnlyList<ProviderPricingSeedCandidate> Candidates,
    Uri? NextPageLink);

public static class AzureOpenAiRetailPricingParser
{
    public static AzureRetailPricingParseResult Parse(
        string json,
        Uri sourceUri,
        DateTimeOffset retrievedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(sourceUri);

        using var document = JsonDocument.Parse(json);
        var candidates = new List<ProviderPricingSeedCandidate>();
        var root = document.RootElement;

        if (root.TryGetProperty("Items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                var candidate = TryParseItem(item, sourceUri, retrievedAtUtc);
                if (candidate is not null)
                {
                    candidates.Add(candidate);
                }
            }
        }

        var nextPageLink = root.TryGetProperty("NextPageLink", out var next) &&
            next.ValueKind == JsonValueKind.String &&
            Uri.TryCreate(next.GetString(), UriKind.Absolute, out var uri)
                ? uri
                : null;

        return new AzureRetailPricingParseResult(candidates, nextPageLink);
    }

    private static ProviderPricingSeedCandidate? TryParseItem(
        JsonElement item,
        Uri sourceUri,
        DateTimeOffset retrievedAtUtc)
    {
        var productName = ReadString(item, "productName");
        var skuName = ReadString(item, "skuName");
        var meterName = ReadString(item, "meterName");
        var unitOfMeasure = ReadString(item, "unitOfMeasure");
        var armRegionName = ReadString(item, "armRegionName") ?? "global";
        var currency = ReadString(item, "currencyCode") ?? "USD";
        var price = item.TryGetProperty("retailPrice", out var retailPrice) &&
            retailPrice.TryGetDecimal(out var parsedPrice)
                ? parsedPrice
                : 0m;

        if (!StringComparer.Ordinal.Equals(productName, "Azure OpenAI") ||
            price <= 0 ||
            string.IsNullOrWhiteSpace(skuName) ||
            string.IsNullOrWhiteSpace(unitOfMeasure))
        {
            return null;
        }

        var tokenType = TryParseTokenType(skuName, meterName);
        if (tokenType is null)
        {
            return null;
        }

        var modelName = NormalizeModelNameFromSku(skuName);
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return null;
        }

        var billingRoute = ToBillingRoute(skuName);
        var pricePerMillion = ToPricePerMillionTokens(price, unitOfMeasure);
        if (pricePerMillion is null)
        {
            return null;
        }

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["source_url"] = sourceUri.ToString(),
            ["source_retrieved_at_utc"] = retrievedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["provider_document_version"] = "azure-retail-prices-api",
            ["provider_sku_name"] = skuName,
            ["billing_route"] = billingRoute,
            ["region"] = armRegionName
        };

        if (ReadString(item, "meterId") is { Length: > 0 } meterId)
        {
            metadata["meter_id"] = meterId;
        }

        return new ProviderPricingSeedCandidate(
            Harness: "codex-cli",
            ProviderName: "azure_openai",
            modelName,
            tokenType.Value,
            billingRoute,
            currency,
            pricePerMillion.Value,
            PricingVersion: $"azure-openai-{DateTimeOffset.UtcNow:yyyyMMdd}",
            new ReadOnlyDictionary<string, string>(metadata));
    }

    private static PricingTokenType? TryParseTokenType(string skuName, string? meterName)
    {
        var text = $"{skuName} {meterName}".ToLowerInvariant();
        if (text.Contains("cached", StringComparison.Ordinal) ||
            text.Contains("cache read", StringComparison.Ordinal))
        {
            return PricingTokenType.CachedInput;
        }

        if (text.Contains("out", StringComparison.Ordinal) ||
            text.Contains("output", StringComparison.Ordinal))
        {
            return PricingTokenType.Output;
        }

        if (text.Contains("inp", StringComparison.Ordinal) ||
            text.Contains("input", StringComparison.Ordinal))
        {
            return PricingTokenType.Input;
        }

        return null;
    }

    private static string NormalizeModelNameFromSku(string skuName)
    {
        var withoutDirection = Regex.Replace(
            skuName,
            @"\s+(Inp|Input|Out|Output|Cached|Cache\s+Read).*$",
            string.Empty,
            RegexOptions.IgnoreCase);

        return Regex.Replace(withoutDirection.Trim().ToLowerInvariant(), @"\s+", "-");
    }

    private static string ToBillingRoute(string skuName)
    {
        var lower = skuName.ToLowerInvariant();
        if (lower.Contains("batch", StringComparison.Ordinal))
        {
            return "batch";
        }

        if (lower.Contains("data zone", StringComparison.Ordinal))
        {
            return "data_zone";
        }

        if (lower.Contains("global", StringComparison.Ordinal))
        {
            return "global_standard";
        }

        return "regional_standard";
    }

    private static decimal? ToPricePerMillionTokens(decimal retailPrice, string unitOfMeasure)
    {
        var normalized = unitOfMeasure.Trim().ToLowerInvariant();
        if (normalized.Contains("1k", StringComparison.Ordinal))
        {
            return retailPrice * 1_000m;
        }

        if (normalized.Contains("1m", StringComparison.Ordinal))
        {
            return retailPrice;
        }

        return null;
    }

    private static string? ReadString(JsonElement item, string propertyName)
    {
        return item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
