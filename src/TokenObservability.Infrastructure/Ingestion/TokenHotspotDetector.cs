using TokenObservability.Domain.Ingestion;
using TokenObservability.Domain.Tenancy;
using TokenObservability.Infrastructure.Persistence;

namespace TokenObservability.Infrastructure.Ingestion;

public sealed class TokenHotspotDetector(InMemoryTenantMetadataStore tenantMetadataStore)
{
    public async Task<IReadOnlyList<TokenHotspotRecord>> DetectSessionHotspotsAsync(
        DetectTokenHotspotsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Thresholds.HighInputTokenThreshold is null &&
            request.Thresholds.HighOutputTokenThreshold is null)
        {
            throw new ArgumentException("Token hotspot detection requires at least one configured token threshold.", nameof(request));
        }

        var sessions = await tenantMetadataStore.ListAgentSessionsAsync(request.CustomerOrganizationId);
        var session = sessions.SingleOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.AgentSessionId, request.AgentSessionId));
        if (session is null)
        {
            throw new InvalidOperationException("Token hotspot session does not belong to the customer organization.");
        }

        var observations = await tenantMetadataStore.ListTokenObservationsAsync(
            request.CustomerOrganizationId,
            request.AgentSessionId);
        var detectedHotspots = new List<TokenHotspotRecord>();

        var inputObservation = SelectLatestObservation(observations, TokenMetricName.InputTokens);
        if (inputObservation is not null &&
            CanCreateConfirmedHotspot(inputObservation) &&
            request.Thresholds.HighInputTokenThreshold is { } inputThreshold &&
            inputObservation.Value >= inputThreshold)
        {
            detectedHotspots.Add(await tenantMetadataStore.RecordTokenHotspotAsync(new CreateTokenHotspotRecordRequest(
                request.CustomerOrganizationId,
                request.AgentSessionId,
                TokenHotspotType.LargeContext,
                TokenHotspotFindingState.Confirmed,
                TokenHotspotAttributionType.Direct,
                ToHotspotConfidence(inputObservation.MetricStatus),
                inputObservation.MetricStatus,
                inputObservation.MetricConfidence,
                PromptCacheEvidenceState.NotApplicable,
                FirstModelName(session),
                "Input tokens exceeded the configured threshold using accepted token evidence.",
                [inputObservation.TokenObservationId.ToString()],
                TokenBurnScore: null,
                EstimatedCostImpact: null,
                DetectionKey: CreateDetectionKey("large-context", inputObservation))));
        }

        var outputObservation = SelectLatestObservation(observations, TokenMetricName.OutputTokens);
        if (outputObservation is not null &&
            CanCreateConfirmedHotspot(outputObservation) &&
            request.Thresholds.HighOutputTokenThreshold is { } outputThreshold &&
            outputObservation.Value >= outputThreshold)
        {
            detectedHotspots.Add(await tenantMetadataStore.RecordTokenHotspotAsync(new CreateTokenHotspotRecordRequest(
                request.CustomerOrganizationId,
                request.AgentSessionId,
                TokenHotspotType.GeneratedArtifactBloat,
                TokenHotspotFindingState.Confirmed,
                TokenHotspotAttributionType.Direct,
                ToHotspotConfidence(outputObservation.MetricStatus),
                outputObservation.MetricStatus,
                outputObservation.MetricConfidence,
                PromptCacheEvidenceState.NotApplicable,
                FirstModelName(session),
                "Output tokens exceeded the configured threshold using accepted token evidence.",
                [outputObservation.TokenObservationId.ToString()],
                TokenBurnScore: null,
                EstimatedCostImpact: null,
                DetectionKey: CreateDetectionKey("generated-artifact-bloat", outputObservation))));
        }

        if (request.DetectPromptCacheDiagnostics &&
            inputObservation is not null &&
            request.Thresholds.HighInputTokenThreshold is { } cacheInputThreshold &&
            inputObservation.Value >= cacheInputThreshold)
        {
            var cacheObservation = SelectLatestObservation(observations, TokenMetricName.CachedInputTokens);
            if (cacheObservation is null)
            {
                detectedHotspots.Add(await RecordUnknownCacheDiagnosticAsync(
                    request,
                    session,
                    inputObservation,
                    PromptCacheEvidenceState.Unknown,
                    "Cache cause is unknown because no provider cache token evidence was available."));
            }
            else if (cacheObservation.MetricStatus is TokenMetricStatus.Unavailable or TokenMetricStatus.NotApplicable)
            {
                detectedHotspots.Add(await RecordUnknownCacheDiagnosticAsync(
                    request,
                    session,
                    inputObservation,
                    PromptCacheEvidenceState.Unavailable,
                    "Cache cause is unknown because provider cache token evidence was unavailable."));
            }
        }

        return detectedHotspots;
    }

    private Task<TokenHotspotRecord> RecordUnknownCacheDiagnosticAsync(
        DetectTokenHotspotsRequest request,
        AgentSessionRecord session,
        TokenObservationRecord inputObservation,
        PromptCacheEvidenceState promptCacheEvidenceState,
        string evidenceSummary)
    {
        return tenantMetadataStore.RecordTokenHotspotAsync(new CreateTokenHotspotRecordRequest(
            request.CustomerOrganizationId,
            request.AgentSessionId,
            TokenHotspotType.PromptCacheBreakage,
            TokenHotspotFindingState.CandidateCorrelated,
            TokenHotspotAttributionType.Correlated,
            TokenHotspotConfidence.Medium,
            inputObservation.MetricStatus,
            inputObservation.MetricConfidence,
            promptCacheEvidenceState,
            FirstModelName(session),
            evidenceSummary,
            [inputObservation.TokenObservationId.ToString()],
            TokenBurnScore: null,
            EstimatedCostImpact: null,
            DetectionKey: CreateDetectionKey($"prompt-cache-breakage:{ToDetectionKeyFragment(promptCacheEvidenceState)}", inputObservation)));
    }

    private static TokenObservationRecord? SelectLatestObservation(
        IReadOnlyList<TokenObservationRecord> observations,
        TokenMetricName metricName)
    {
        return observations
            .Where(observation => observation.MetricName == metricName)
            .OrderByDescending(observation => observation.CreatedAtUtc)
            .ThenByDescending(observation => observation.TokenObservationId.ToString(), StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static bool CanCreateConfirmedHotspot(TokenObservationRecord observation)
    {
        return observation.MetricConfidence is not (TokenMetricConfidence.LlmInferred or TokenMetricConfidence.Unavailable);
    }

    private static TokenHotspotConfidence ToHotspotConfidence(TokenMetricStatus metricStatus)
    {
        return metricStatus switch
        {
            TokenMetricStatus.Observed or TokenMetricStatus.Derived => TokenHotspotConfidence.High,
            TokenMetricStatus.Estimated or TokenMetricStatus.Mixed => TokenHotspotConfidence.Medium,
            _ => TokenHotspotConfidence.Unavailable
        };
    }

    private static string? FirstModelName(AgentSessionRecord session)
    {
        return session.ModelNames.FirstOrDefault();
    }

    private static string CreateDetectionKey(string ruleKey, TokenObservationRecord observation)
    {
        return $"{ruleKey}:{observation.TokenObservationId}";
    }

    private static string ToDetectionKeyFragment(PromptCacheEvidenceState promptCacheEvidenceState)
    {
        return promptCacheEvidenceState switch
        {
            PromptCacheEvidenceState.KnownReason => "known-reason",
            PromptCacheEvidenceState.InferredCandidate => "inferred-candidate",
            PromptCacheEvidenceState.Unknown => "unknown",
            PromptCacheEvidenceState.Unavailable => "unavailable",
            PromptCacheEvidenceState.NotApplicable => "not-applicable",
            _ => throw new ArgumentOutOfRangeException(nameof(promptCacheEvidenceState), promptCacheEvidenceState, null)
        };
    }
}

public sealed record DetectTokenHotspotsRequest(
    CustomerOrganizationId CustomerOrganizationId,
    string AgentSessionId,
    TokenHotspotDetectionThresholds Thresholds,
    bool DetectPromptCacheDiagnostics = false);

public sealed record TokenHotspotDetectionThresholds(
    long? HighInputTokenThreshold,
    long? HighOutputTokenThreshold);
