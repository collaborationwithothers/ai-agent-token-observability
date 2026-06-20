using TokenObservability.Domain.Ingestion;
using TokenObservability.Domain.Tenancy;

namespace TokenObservability.Domain.Pricing;

public sealed record PricingBasisRecord(
    string PricingBasisId,
    CustomerOrganizationId CustomerOrganizationId,
    string Harness,
    string ProviderName,
    string ModelName,
    PricingTokenType TokenType,
    string BillingRoute,
    string Currency,
    decimal PricePerMillionTokens,
    string PricingVersion,
    PricingSourceKind SourceKind,
    PricingReviewState ReviewState,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    string AuditEventId,
    IReadOnlyDictionary<string, string> SourceMetadata,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreatePricingBasisRecordRequest(
    CustomerOrganizationId CustomerOrganizationId,
    string Harness,
    string ProviderName,
    string ModelName,
    PricingTokenType TokenType,
    string BillingRoute,
    string Currency,
    decimal PricePerMillionTokens,
    string PricingVersion,
    PricingSourceKind SourceKind,
    PricingReviewState ReviewState,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    string AuditEventId,
    IReadOnlyDictionary<string, string> SourceMetadata);

public sealed record PricingBasisReviewRequest(
    CustomerOrganizationId CustomerOrganizationId,
    string PricingBasisId,
    string AuditEventId,
    string CorrelationId,
    string DecisionReason);

public sealed record CostEstimateRecord(
    string CostEstimateId,
    CustomerOrganizationId CustomerOrganizationId,
    string AgentSessionId,
    string? ModelInvocationId,
    string? PricingBasisId,
    string? PricingVersion,
    string Currency,
    decimal? EstimatedCost,
    CostEstimateStatus CostStatus,
    CostEstimateSourceKind SourceKind,
    TokenMetricStatus TokenMetricStatus,
    TokenMetricConfidence TokenMetricConfidence,
    string ProviderName,
    string ModelName,
    string BillingRoute,
    PricingTokenType TokenType,
    DateTimeOffset CreatedAtUtc);

public sealed record CreateCostEstimateRecordRequest(
    CustomerOrganizationId CustomerOrganizationId,
    string AgentSessionId,
    string? ModelInvocationId,
    string? PricingBasisId,
    string? PricingVersion,
    string Currency,
    decimal? EstimatedCost,
    CostEstimateStatus CostStatus,
    CostEstimateSourceKind SourceKind,
    TokenMetricStatus TokenMetricStatus,
    TokenMetricConfidence TokenMetricConfidence,
    string ProviderName,
    string ModelName,
    string BillingRoute,
    PricingTokenType TokenType);

public sealed record EstimateTokenObservationCostRequest(
    CustomerOrganizationId CustomerOrganizationId,
    string AgentSessionId,
    string? ModelInvocationId,
    TokenMetricName MetricName,
    long? TokenCount,
    TokenMetricStatus TokenMetricStatus,
    TokenMetricConfidence TokenMetricConfidence,
    string ProviderName,
    string ModelName,
    string BillingRoute,
    DateTimeOffset ObservedAtUtc);

public sealed record CostMixBucket(
    string ProviderName,
    string ModelName,
    string BillingRoute,
    PricingTokenType TokenType,
    CostEstimateStatus CostStatus,
    string Currency,
    decimal? EstimatedCost,
    int EstimateCount,
    TokenMetricStatus TokenMetricStatus);

public enum PricingTokenType
{
    Input,
    Output,
    CachedInput,
    ReasoningOutput
}

public enum PricingSourceKind
{
    AutomatedSeed,
    AdminOverride,
    ProviderDocs,
    EnterpriseContract
}

public enum PricingReviewState
{
    Candidate,
    Approved,
    Rejected,
    Superseded
}

public enum CostEstimateStatus
{
    Estimated,
    Unavailable,
    NotApplicable,
    Mixed
}

public enum CostEstimateSourceKind
{
    DerivedFromObservedTokens,
    DerivedFromEstimatedTokens,
    ManualOverride,
    Unavailable
}

public static class PricingCostCalculator
{
    public static CreateCostEstimateRecordRequest EstimateTokenObservationCost(
        EstimateTokenObservationCostRequest request,
        IReadOnlyList<PricingBasisRecord> pricingBasisRecords)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(pricingBasisRecords);

        var tokenType = ToPricingTokenType(request.MetricName);
        if (tokenType is null || request.TokenMetricStatus == TokenMetricStatus.NotApplicable)
        {
            return CreateUnavailableEstimate(request, tokenType ?? PricingTokenType.Input, CostEstimateStatus.NotApplicable);
        }

        if (request.TokenMetricStatus == TokenMetricStatus.Unavailable || request.TokenCount is null)
        {
            return CreateUnavailableEstimate(request, tokenType.Value, CostEstimateStatus.Unavailable);
        }

        var matchedBasis = pricingBasisRecords
            .Where(basis =>
                basis.CustomerOrganizationId == request.CustomerOrganizationId &&
                basis.ReviewState == PricingReviewState.Approved &&
                StringComparer.Ordinal.Equals(basis.ProviderName, request.ProviderName.Trim()) &&
                StringComparer.Ordinal.Equals(basis.ModelName, request.ModelName.Trim()) &&
                StringComparer.Ordinal.Equals(basis.BillingRoute, request.BillingRoute.Trim()) &&
                basis.TokenType == tokenType.Value &&
                basis.EffectiveFromUtc <= request.ObservedAtUtc.ToUniversalTime() &&
                (basis.EffectiveToUtc is null || basis.EffectiveToUtc > request.ObservedAtUtc.ToUniversalTime()))
            .OrderByDescending(static basis => basis.SourceKind == PricingSourceKind.AdminOverride)
            .ThenByDescending(static basis => basis.EffectiveFromUtc)
            .ThenBy(static basis => basis.PricingBasisId, StringComparer.Ordinal)
            .FirstOrDefault();

        if (matchedBasis is null)
        {
            return CreateUnavailableEstimate(request, tokenType.Value, CostEstimateStatus.Unavailable);
        }

        var status = request.TokenMetricStatus == TokenMetricStatus.Mixed
            ? CostEstimateStatus.Mixed
            : CostEstimateStatus.Estimated;
        var sourceKind = request.TokenMetricStatus == TokenMetricStatus.Observed || request.TokenMetricStatus == TokenMetricStatus.Derived
            ? CostEstimateSourceKind.DerivedFromObservedTokens
            : CostEstimateSourceKind.DerivedFromEstimatedTokens;

        return new CreateCostEstimateRecordRequest(
            request.CustomerOrganizationId,
            request.AgentSessionId,
            request.ModelInvocationId,
            matchedBasis.PricingBasisId,
            matchedBasis.PricingVersion,
            matchedBasis.Currency,
            decimal.Round(request.TokenCount.Value * matchedBasis.PricePerMillionTokens / 1_000_000m, 12),
            status,
            sourceKind,
            request.TokenMetricStatus,
            request.TokenMetricConfidence,
            request.ProviderName,
            request.ModelName,
            request.BillingRoute,
            tokenType.Value);
    }

    private static CreateCostEstimateRecordRequest CreateUnavailableEstimate(
        EstimateTokenObservationCostRequest request,
        PricingTokenType tokenType,
        CostEstimateStatus status)
    {
        return new CreateCostEstimateRecordRequest(
            request.CustomerOrganizationId,
            request.AgentSessionId,
            request.ModelInvocationId,
            PricingBasisId: null,
            PricingVersion: null,
            Currency: "USD",
            EstimatedCost: null,
            status,
            CostEstimateSourceKind.Unavailable,
            request.TokenMetricStatus,
            request.TokenMetricConfidence,
            request.ProviderName,
            request.ModelName,
            request.BillingRoute,
            tokenType);
    }

    public static PricingTokenType? ToPricingTokenType(TokenMetricName metricName)
    {
        return metricName switch
        {
            TokenMetricName.InputTokens => PricingTokenType.Input,
            TokenMetricName.OutputTokens => PricingTokenType.Output,
            TokenMetricName.CachedInputTokens => PricingTokenType.CachedInput,
            TokenMetricName.ReasoningOutputTokens => PricingTokenType.ReasoningOutput,
            TokenMetricName.TotalTokens => null,
            _ => throw new ArgumentOutOfRangeException(nameof(metricName), metricName, null)
        };
    }
}
