using System.Text.Json;
using TokenObservability.Domain.Tenancy;

namespace TokenObservability.Domain.Pricing;

public sealed record BudgetPolicyRecord(
    string BudgetPolicyId,
    CustomerOrganizationId CustomerOrganizationId,
    BudgetPolicyScopeKind ScopeKind,
    string? ScopeId,
    BudgetMetricKind MetricKind,
    string ThresholdJson,
    BudgetPolicyStatus Status,
    string AuditEventId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreateBudgetPolicyRequest(
    CustomerOrganizationId CustomerOrganizationId,
    BudgetPolicyScopeKind ScopeKind,
    string? ScopeId,
    BudgetMetricKind MetricKind,
    string ThresholdJson,
    BudgetPolicyStatus Status,
    string AuditEventId,
    string CorrelationId);

public sealed record UpdateBudgetPolicyRequest(
    CustomerOrganizationId CustomerOrganizationId,
    string BudgetPolicyId,
    BudgetPolicyScopeKind? ScopeKind,
    string? ScopeId,
    BudgetMetricKind? MetricKind,
    string? ThresholdJson,
    BudgetPolicyStatus? Status,
    string AuditEventId,
    string CorrelationId);

public enum BudgetPolicyScopeKind
{
    CustomerOrganization,
    Team,
    Repository,
    Workflow,
    Harness,
    Model
}

public enum BudgetMetricKind
{
    Tokens,
    EstimatedCost,
    CacheMissRate,
    ErrorRework
}

public enum BudgetPolicyStatus
{
    Active,
    Disabled
}

public static class BudgetPolicyValidator
{
    private static readonly string[] ForbiddenThresholdFragments =
    [
        "developer",
        "productuserid",
        "product_user",
        "user_id",
        "email",
        "leaderboard",
        "ranking",
        "blame",
        "waste",
        "wrongness"
    ];

    public static string NormalizeThresholdJson(BudgetMetricKind metricKind, string thresholdJson)
    {
        if (string.IsNullOrWhiteSpace(thresholdJson))
        {
            throw new ArgumentException("Budget threshold JSON is required.", nameof(thresholdJson));
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(thresholdJson);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Budget threshold JSON must be valid JSON.", nameof(thresholdJson), ex);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Budget threshold JSON must be an object.", nameof(thresholdJson));
            }

            ValidateThresholdShape(metricKind, document.RootElement);

            var normalized = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var lowercase = normalized.ToLowerInvariant();

            if (ForbiddenThresholdFragments.Any(lowercase.Contains))
            {
                throw new ArgumentException("Budget threshold JSON must not contain individual developer ranking or blame fields.", nameof(thresholdJson));
            }

            return normalized;
        }
    }

    private static void ValidateThresholdShape(BudgetMetricKind metricKind, JsonElement threshold)
    {
        RequireAllowedPeriod(threshold);

        var allowedProperties = metricKind switch
        {
            BudgetMetricKind.Tokens => new[] { "amount", "period" },
            BudgetMetricKind.EstimatedCost => new[] { "amount", "currency", "period" },
            BudgetMetricKind.CacheMissRate or BudgetMetricKind.ErrorRework => new[] { "rate", "period" },
            _ => throw new ArgumentOutOfRangeException(nameof(metricKind), metricKind, null)
        };

        foreach (var property in threshold.EnumerateObject())
        {
            if (!allowedProperties.Contains(property.Name, StringComparer.Ordinal))
            {
                throw new ArgumentException("Budget threshold JSON contains an unsupported field.", nameof(threshold));
            }
        }

        if (metricKind is BudgetMetricKind.Tokens or BudgetMetricKind.EstimatedCost)
        {
            var amount = RequirePositiveNumber(threshold, "amount");
            if (metricKind == BudgetMetricKind.Tokens && amount % 1 != 0)
            {
                throw new ArgumentException("Token budget thresholds must be whole numbers.", nameof(threshold));
            }

            if (metricKind == BudgetMetricKind.EstimatedCost)
            {
                if (!threshold.TryGetProperty("currency", out var currency) ||
                    currency.ValueKind != JsonValueKind.String ||
                    currency.GetString() is not "USD")
                {
                    throw new ArgumentException("Estimated cost budget thresholds require USD currency.", nameof(threshold));
                }
            }

            return;
        }

        var rate = RequirePositiveNumber(threshold, "rate");
        if (rate > 1)
        {
            throw new ArgumentException("Rate budget thresholds must be between 0 and 1.", nameof(threshold));
        }
    }

    private static void RequireAllowedPeriod(JsonElement threshold)
    {
        if (!threshold.TryGetProperty("period", out var period) ||
            period.ValueKind != JsonValueKind.String ||
            period.GetString() is not ("daily" or "weekly" or "monthly"))
        {
            throw new ArgumentException("Budget threshold JSON requires a supported period.", nameof(threshold));
        }
    }

    private static decimal RequirePositiveNumber(JsonElement threshold, string propertyName)
    {
        if (!threshold.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetDecimal(out var value) ||
            value <= 0)
        {
            throw new ArgumentException("Budget threshold JSON requires a positive numeric threshold.", nameof(threshold));
        }

        return value;
    }
}
