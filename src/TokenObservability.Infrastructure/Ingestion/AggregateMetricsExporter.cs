using TokenObservability.Domain.Ingestion;
using TokenObservability.Domain.Tenancy;
using TokenObservability.Infrastructure.Persistence;

namespace TokenObservability.Infrastructure.Ingestion;

public sealed record AggregateMetricsExportOptions(string Environment);

public sealed record AggregateMetricsExportResult(
    bool Succeeded,
    int ExportedPointCount,
    string? FailureReason)
{
    public static AggregateMetricsExportResult Success(int exportedPointCount)
    {
        return new AggregateMetricsExportResult(true, exportedPointCount, FailureReason: null);
    }

    public static AggregateMetricsExportResult Failure(string failureReason)
    {
        return new AggregateMetricsExportResult(false, ExportedPointCount: 0, failureReason);
    }
}

public sealed class AggregateMetricsExporter(
    InMemoryTenantMetadataStore tenantMetadataStore,
    IAggregateMetricSink sink,
    AggregateMetricsExportOptions options)
{
    public async Task<AggregateMetricsExportResult> ExportAcceptedSessionAsync(
        CustomerOrganizationId customerOrganizationId,
        string agentSessionId,
        string correlationId,
        string? sourceTelemetryEnvelopeId = null,
        CancellationToken cancellationToken = default)
    {
        var organization = await tenantMetadataStore.FindCustomerOrganizationAsync(customerOrganizationId)
            ?? throw new InvalidOperationException("Customer organization was not found.");
        var session = (await tenantMetadataStore.ListAgentSessionsAsync(customerOrganizationId))
            .SingleOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.AgentSessionId, agentSessionId))
            ?? throw new InvalidOperationException("Agent session was not found.");
        var observations = await tenantMetadataStore.ListTokenObservationsAsync(customerOrganizationId, session.AgentSessionId);
        var exportObservations = sourceTelemetryEnvelopeId is null
            ? observations
            : observations
                .Where(observation => StringComparer.Ordinal.Equals(
                    observation.SourceTelemetryEnvelopeId,
                    sourceTelemetryEnvelopeId))
                .ToArray();
        var includeSessionStarted = sourceTelemetryEnvelopeId is null ||
            StringComparer.Ordinal.Equals(
                session.SourceTelemetryEnvelopeIds.FirstOrDefault(),
                sourceTelemetryEnvelopeId);
        var pointExports = BuildAggregateMetricPoints(organization, session, exportObservations, includeSessionStarted);
        var sinkPoints = pointExports.Select(static pointExport => pointExport.Point).ToArray();

        try
        {
            foreach (var pointExport in pointExports)
            {
                await tenantMetadataStore.ValidateAggregateMetricPointAsync(new CreateAggregateMetricPointRecordRequest(
                    customerOrganizationId,
                    pointExport.AgentSessionId,
                    pointExport.Point.Name,
                    pointExport.Point.Value,
                    pointExport.Point.Unit,
                    pointExport.Point.Labels));
            }

            await sink.ExportAsync(sinkPoints, cancellationToken);

            foreach (var pointExport in pointExports)
            {
                await tenantMetadataStore.RecordAggregateMetricPointAsync(new CreateAggregateMetricPointRecordRequest(
                    customerOrganizationId,
                    pointExport.AgentSessionId,
                    pointExport.Point.Name,
                    pointExport.Point.Value,
                    pointExport.Point.Unit,
                    pointExport.Point.Labels));
            }

            return AggregateMetricsExportResult.Success(sinkPoints.Length);
        }
        catch (ArgumentException) when (!cancellationToken.IsCancellationRequested)
        {
            const string failureReason = "invalid_metric_shape";
            await tenantMetadataStore.RecordAggregateMetricExportFailureAsync(
                new CreateAggregateMetricExportFailureRecordRequest(
                    customerOrganizationId,
                    session.AgentSessionId,
                    failureReason,
                    correlationId));

            return AggregateMetricsExportResult.Failure(failureReason);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            const string failureReason = "sink_failure";
            await tenantMetadataStore.RecordAggregateMetricExportFailureAsync(
                new CreateAggregateMetricExportFailureRecordRequest(
                    customerOrganizationId,
                    session.AgentSessionId,
                    failureReason,
                    correlationId));

            return AggregateMetricsExportResult.Failure(failureReason);
        }
    }

    private IReadOnlyList<AggregateMetricPointExport> BuildAggregateMetricPoints(
        CustomerOrganization organization,
        AgentSessionRecord session,
        IReadOnlyList<TokenObservationRecord> observations,
        bool includeSessionStarted)
    {
        var exportedAtUtc = DateTimeOffset.UtcNow;
        var commonLabels = CreateCommonLabels(organization);
        var points = new List<AggregateMetricPointExport>();

        if (includeSessionStarted)
        {
            points.Add(CreatePoint(
                session.AgentSessionId,
                "tokenobs_sessions_started_total",
                value: 1,
                "sessions",
                AddLabels(commonLabels, new Dictionary<string, string>
                {
                    ["harness"] = ToMetricHarness(session.Harness)
                }),
                exportedAtUtc));
        }

        var model = session.ModelNames.Count > 0 ? session.ModelNames[0] : "unknown";
        var modelProvider = ToModelProvider(model);
        var componentInvocationKeys = observations
            .Where(static observation =>
                observation.Value is not null &&
                observation.MetricName is not TokenMetricName.TotalTokens)
            .Select(static observation => observation.ModelInvocationId ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var observation in observations)
        {
            AddMetricStatePoint(
                points,
                commonLabels,
                session,
                observation,
                model,
                modelProvider,
                exportedAtUtc);

            if (observation.Value is null)
            {
                continue;
            }

            if (observation.MetricName == TokenMetricName.TotalTokens &&
                componentInvocationKeys.Contains(observation.ModelInvocationId ?? string.Empty))
            {
                continue;
            }

            points.Add(CreatePoint(
                session.AgentSessionId,
                "tokenobs_tokens_total",
                observation.Value.Value,
                "tokens",
                AddLabels(commonLabels, new Dictionary<string, string>
                {
                    ["harness"] = ToMetricHarness(session.Harness),
                    ["model_provider"] = modelProvider,
                    ["model"] = model,
                    ["token_type"] = ToTokenType(observation.MetricName),
                    ["metric_status"] = ToMetricStatus(observation.MetricStatus),
                    ["metric_confidence"] = ToMetricConfidence(observation.MetricConfidence)
                }),
                exportedAtUtc));
        }

        return points;
    }

    private static void AddMetricStatePoint(
        List<AggregateMetricPointExport> points,
        IReadOnlyDictionary<string, string> commonLabels,
        AgentSessionRecord session,
        TokenObservationRecord observation,
        string model,
        string modelProvider,
        DateTimeOffset exportedAtUtc)
    {
        points.Add(CreatePoint(
            session.AgentSessionId,
            "tokenobs_token_metric_states_total",
            value: 1,
            "observations",
            AddLabels(commonLabels, new Dictionary<string, string>
            {
                ["harness"] = ToMetricHarness(session.Harness),
                ["model_provider"] = modelProvider,
                ["model"] = model,
                ["token_type"] = ToTokenType(observation.MetricName),
                ["metric_status"] = ToMetricStatus(observation.MetricStatus),
                ["metric_confidence"] = ToMetricConfidence(observation.MetricConfidence)
            }),
            exportedAtUtc));
    }

    private Dictionary<string, string> CreateCommonLabels(CustomerOrganization organization)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["customer_organization_slug"] = organization.Slug,
            ["environment"] = NormalizeEnvironment(options.Environment),
            ["region"] = organization.DataResidencyRegion
        };
    }

    private static AggregateMetricPointExport CreatePoint(
        string agentSessionId,
        string name,
        double value,
        string unit,
        IReadOnlyDictionary<string, string> labels,
        DateTimeOffset exportedAtUtc)
    {
        return new AggregateMetricPointExport(
            agentSessionId,
            new AggregateMetricPoint(
                name,
                value,
                unit,
                labels,
                exportedAtUtc));
    }

    private static Dictionary<string, string> AddLabels(
        IReadOnlyDictionary<string, string> commonLabels,
        IReadOnlyDictionary<string, string> additionalLabels)
    {
        var labels = new Dictionary<string, string>(commonLabels, StringComparer.Ordinal);

        foreach (var label in additionalLabels)
        {
            labels.Add(label.Key, label.Value);
        }

        return labels;
    }

    private static string NormalizeEnvironment(string environment)
    {
        var normalized = string.IsNullOrWhiteSpace(environment)
            ? throw new ArgumentException("Aggregate metric environment is required.", nameof(environment))
            : environment.Trim().ToLowerInvariant();

        return normalized is "dv" or "qa" or "pp" or "pd"
            ? normalized
            : throw new ArgumentException("Aggregate metric environment is not supported.", nameof(environment));
    }

    private static string ToMetricHarness(string harness)
    {
        return harness switch
        {
            "codex-cli" => "codex",
            _ => "unknown"
        };
    }

    private static string ToModelProvider(string model)
    {
        return model.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase) ||
            model.StartsWith("o1", StringComparison.OrdinalIgnoreCase) ||
            model.StartsWith("o3", StringComparison.OrdinalIgnoreCase) ||
            model.StartsWith("o4", StringComparison.OrdinalIgnoreCase)
            ? "openai"
            : "unknown";
    }

    private static string ToTokenType(TokenMetricName metricName)
    {
        return metricName switch
        {
            TokenMetricName.InputTokens => "input",
            TokenMetricName.OutputTokens => "output",
            TokenMetricName.CachedInputTokens => "cached_input",
            TokenMetricName.ReasoningOutputTokens => "reasoning_output",
            TokenMetricName.TotalTokens => "total",
            _ => throw new ArgumentOutOfRangeException(nameof(metricName), metricName, null)
        };
    }

    private static string ToMetricStatus(TokenMetricStatus status)
    {
        return status switch
        {
            TokenMetricStatus.Observed => "observed",
            TokenMetricStatus.Derived => "derived",
            TokenMetricStatus.Estimated => "estimated",
            TokenMetricStatus.Unavailable => "unavailable",
            TokenMetricStatus.NotApplicable => "not_applicable",
            TokenMetricStatus.Mixed => "mixed",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
    }

    private static string ToMetricConfidence(TokenMetricConfidence confidence)
    {
        return confidence switch
        {
            TokenMetricConfidence.Observed => "observed",
            TokenMetricConfidence.Deterministic => "deterministic",
            TokenMetricConfidence.Estimated => "estimated",
            TokenMetricConfidence.LlmInferred => "llm_inferred",
            TokenMetricConfidence.Unavailable => "unavailable",
            _ => throw new ArgumentOutOfRangeException(nameof(confidence), confidence, null)
        };
    }

    private sealed record AggregateMetricPointExport(
        string AgentSessionId,
        AggregateMetricPoint Point);
}
