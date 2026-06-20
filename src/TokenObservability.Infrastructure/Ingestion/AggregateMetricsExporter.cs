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
        var telemetryEnvelopes = await tenantMetadataStore.ListTelemetryEnvelopesAsync(customerOrganizationId);
        var sessionEnvelopeIds = session.SourceTelemetryEnvelopeIds.ToHashSet(StringComparer.Ordinal);
        var sessionEnvelopes = telemetryEnvelopes
            .Where(envelope => sessionEnvelopeIds.Contains(envelope.TelemetryEnvelopeId))
            .ToArray();
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
        var exportEnvelopes = sourceTelemetryEnvelopeId is null
            ? sessionEnvelopes
            : sessionEnvelopes
                .Where(envelope => StringComparer.Ordinal.Equals(envelope.TelemetryEnvelopeId, sourceTelemetryEnvelopeId))
                .ToArray();
        var pointExports = BuildAggregateMetricPoints(
            organization,
            session,
            exportObservations,
            exportEnvelopes,
            includeSessionStarted);
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

    public async Task<IReadOnlyList<AggregateTokenTimelineBucket>> BuildDailyTokenTimelineAsync(
        CustomerOrganizationId customerOrganizationId,
        AggregateTokenTimelineQuery query,
        CancellationToken cancellationToken = default)
    {
        ValidateTokenTimelineQuery(query);

        var organization = await tenantMetadataStore.FindCustomerOrganizationAsync(customerOrganizationId)
            ?? throw new InvalidOperationException("Customer organization was not found.");
        var sessions = await tenantMetadataStore.ListAgentSessionsAsync(customerOrganizationId);
        var calculatedAtUtc = DateTimeOffset.UtcNow;
        var observationsBySessionId = new Dictionary<string, IReadOnlyList<TokenObservationRecord>>(StringComparer.Ordinal);

        foreach (var session in sessions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            observationsBySessionId[session.AgentSessionId] = await tenantMetadataStore.ListTokenObservationsAsync(
                customerOrganizationId,
                session.AgentSessionId);
        }

        var buckets = new List<AggregateTokenTimelineBucket>();

        for (var date = query.StartDateUtc; date <= query.EndDateUtc; date = date.AddDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dayObservations = sessions
                .Where(session => SessionStartedOnDate(session, date))
                .SelectMany(session => observationsBySessionId[session.AgentSessionId])
                .ToArray();
            buckets.Add(CreateTimelineBucket(organization, query, date, dayObservations, calculatedAtUtc));
        }

        var bucketsWithMovingAverage = ApplyMovingAverage(buckets, query.MovingAverageWindowDays);
        await tenantMetadataStore.RecordAggregateTokenTimelineBucketsAsync(customerOrganizationId, bucketsWithMovingAverage);

        return bucketsWithMovingAverage;
    }

    private IReadOnlyList<AggregateMetricPointExport> BuildAggregateMetricPoints(
        CustomerOrganization organization,
        AgentSessionRecord session,
        IReadOnlyList<TokenObservationRecord> observations,
        IReadOnlyList<TelemetryEnvelopeRecord> telemetryEnvelopes,
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
        AddModelOperationPoints(points, commonLabels, session, telemetryEnvelopes, model, modelProvider, exportedAtUtc);
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

    private static void AddModelOperationPoints(
        List<AggregateMetricPointExport> points,
        IReadOnlyDictionary<string, string> commonLabels,
        AgentSessionRecord session,
        IReadOnlyList<TelemetryEnvelopeRecord> telemetryEnvelopes,
        string model,
        string modelProvider,
        DateTimeOffset exportedAtUtc)
    {
        var modelInvocationCount = telemetryEnvelopes
            .Where(static envelope => !string.IsNullOrWhiteSpace(envelope.ModelName))
            .Select(static envelope => envelope.TelemetryEnvelopeId)
            .Distinct(StringComparer.Ordinal)
            .Count();

        if (modelInvocationCount > 0)
        {
            points.Add(CreatePoint(
                session.AgentSessionId,
                "tokenobs_model_invocations_total",
                modelInvocationCount,
                "invocations",
                AddLabels(commonLabels, new Dictionary<string, string>
                {
                    ["harness"] = ToMetricHarness(session.Harness),
                    ["model_provider"] = modelProvider,
                    ["model"] = model,
                    ["result"] = "accepted"
                }),
                exportedAtUtc));
        }

        var turnCount = telemetryEnvelopes
            .Where(static envelope => !string.IsNullOrWhiteSpace(envelope.TurnIdHash))
            .Select(static envelope => envelope.TurnIdHash!)
            .Distinct(StringComparer.Ordinal)
            .Count();

        if (turnCount > 0)
        {
            points.Add(CreatePoint(
                session.AgentSessionId,
                "tokenobs_turns_total",
                turnCount,
                "turns",
                AddLabels(commonLabels, new Dictionary<string, string>
                {
                    ["harness"] = ToMetricHarness(session.Harness),
                    ["result"] = "accepted"
                }),
                exportedAtUtc));
        }
    }

    private AggregateTokenTimelineBucket CreateTimelineBucket(
        CustomerOrganization organization,
        AggregateTokenTimelineQuery query,
        DateOnly bucketDateUtc,
        IReadOnlyList<TokenObservationRecord> observations,
        DateTimeOffset calculatedAtUtc)
    {
        if (observations.Count == 0)
        {
            return new AggregateTokenTimelineBucket(
                organization.CustomerOrganizationId,
                organization.Slug,
                NormalizeEnvironment(options.Environment),
                organization.DataResidencyRegion,
                bucketDateUtc,
                Period: "day",
                TokenBurn: 0,
                TokenMetricStatus.NotApplicable,
                TokenMetricConfidence.Unavailable,
                MovingAverageTokenBurn: null,
                query.MovingAverageWindowDays,
                IsDenseZeroBurn: true,
                calculatedAtUtc);
        }

        var effectiveObservations = SelectEffectiveTokenBurnObservations(observations).ToArray();
        var numericValues = effectiveObservations
            .Where(static observation => observation.Value.HasValue)
            .Select(static observation => observation.Value!.Value)
            .ToArray();
        var tokenBurn = numericValues.Length == 0 ? (long?)null : numericValues.Sum();

        return new AggregateTokenTimelineBucket(
            organization.CustomerOrganizationId,
            organization.Slug,
            NormalizeEnvironment(options.Environment),
            organization.DataResidencyRegion,
            bucketDateUtc,
            Period: "day",
            tokenBurn,
            AggregateMetricStatus(effectiveObservations),
            AggregateMetricConfidence(effectiveObservations),
            MovingAverageTokenBurn: null,
            query.MovingAverageWindowDays,
            IsDenseZeroBurn: false,
            calculatedAtUtc);
    }

    private static IReadOnlyList<AggregateTokenTimelineBucket> ApplyMovingAverage(
        IReadOnlyList<AggregateTokenTimelineBucket> buckets,
        int movingAverageWindowDays)
    {
        var result = new List<AggregateTokenTimelineBucket>(buckets.Count);

        for (var index = 0; index < buckets.Count; index++)
        {
            var windowStart = Math.Max(0, index - movingAverageWindowDays + 1);
            var numericValues = buckets
                .Skip(windowStart)
                .Take(index - windowStart + 1)
                .Where(static bucket => bucket.TokenBurn.HasValue)
                .Select(static bucket => bucket.TokenBurn!.Value)
                .ToArray();
            var movingAverage = numericValues.Length == 0 ? (double?)null : numericValues.Average();
            result.Add(buckets[index] with { MovingAverageTokenBurn = movingAverage });
        }

        return result;
    }

    private static IEnumerable<TokenObservationRecord> SelectEffectiveTokenBurnObservations(
        IReadOnlyList<TokenObservationRecord> observations)
    {
        var componentInvocationKeys = observations
            .Where(static observation =>
                observation.Value is not null &&
                observation.MetricName is not TokenMetricName.TotalTokens)
            .Select(static observation => observation.ModelInvocationId ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var observation in observations)
        {
            if (observation.MetricName == TokenMetricName.TotalTokens &&
                componentInvocationKeys.Contains(observation.ModelInvocationId ?? string.Empty))
            {
                continue;
            }

            yield return observation;
        }
    }

    private static TokenMetricStatus AggregateMetricStatus(IReadOnlyList<TokenObservationRecord> observations)
    {
        var statuses = observations
            .Select(static observation => observation.MetricStatus)
            .Distinct()
            .ToArray();

        return statuses.Length == 1 ? statuses[0] : TokenMetricStatus.Mixed;
    }

    private static TokenMetricConfidence AggregateMetricConfidence(IReadOnlyList<TokenObservationRecord> observations)
    {
        return observations
            .Select(static observation => observation.MetricConfidence)
            .DefaultIfEmpty(TokenMetricConfidence.Unavailable)
            .MaxBy(static confidence => confidence switch
            {
                TokenMetricConfidence.Unavailable => 4,
                TokenMetricConfidence.LlmInferred => 3,
                TokenMetricConfidence.Estimated => 2,
                TokenMetricConfidence.Deterministic => 1,
                TokenMetricConfidence.Observed => 0,
                _ => 4
            });
    }

    private static bool SessionStartedOnDate(AgentSessionRecord session, DateOnly bucketDateUtc)
    {
        var startedAtUtc = (session.StartedAtUtc ?? session.CreatedAtUtc).ToUniversalTime();

        return DateOnly.FromDateTime(startedAtUtc.DateTime) == bucketDateUtc;
    }

    private static void ValidateTokenTimelineQuery(AggregateTokenTimelineQuery query)
    {
        if (query.EndDateUtc < query.StartDateUtc)
        {
            throw new ArgumentException("Aggregate token timeline end date must be on or after start date.", nameof(query));
        }

        if (query.MovingAverageWindowDays is < 1 or > 90)
        {
            throw new ArgumentException("Moving average window must be between 1 and 90 days.", nameof(query));
        }
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
