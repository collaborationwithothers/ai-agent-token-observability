using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TokenObservability.Domain.Ingestion;
using TokenObservability.Domain.Recommendations;
using TokenObservability.Infrastructure.Persistence;

namespace TokenObservability.Infrastructure.Recommendations;

public sealed class DeterministicRecommendationGenerator(InMemoryTenantMetadataStore tenantMetadataStore)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<RecommendationRecord>> GenerateForSessionAsync(
        GenerateRecommendationsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sessions = await tenantMetadataStore.ListAgentSessionsAsync(request.CustomerOrganizationId);
        var session = sessions.SingleOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.AgentSessionId, request.AgentSessionId));
        if (session is null)
        {
            throw new InvalidOperationException("Recommendation session does not belong to the customer organization.");
        }

        var hotspots = await tenantMetadataStore.ListTokenHotspotsAsync(
            request.CustomerOrganizationId,
            request.AgentSessionId);
        var observations = await tenantMetadataStore.ListTokenObservationsAsync(
            request.CustomerOrganizationId,
            request.AgentSessionId);

        var recommendations = new List<RecommendationRecord>();
        foreach (var hotspot in hotspots)
        {
            var rule = TryCreateRule(hotspot);
            if (rule is null)
            {
                continue;
            }

            var packet = CreateEvidencePacket(
                request,
                session,
                hotspot,
                observations,
                recommendationModelPolicyVersion: "deterministic-v1",
                promptTemplateVersion: null,
                hiddenEvidenceReasons: []);
            recommendations.Add(await tenantMetadataStore.CreateRecommendationAsync(
                new CreateRecommendationRecordRequest(
                    request.CustomerOrganizationId,
                    request.AgentSessionId,
                    hotspot.TokenHotspotId,
                    rule.RuleId,
                    RecommendationKind.Deterministic,
                    RecommendationState.Accepted,
                    RecommendationAuthorityState.Deterministic,
                    rule.Confidence,
                    RecommendationValidationState.Validated,
                    RecommendationVisibilityScope.TeamScoped,
                    packet.SchemaVersion,
                    packet.Json,
                    packet.Hash,
                    rule.Summary,
                    rule.Rationale,
                    rule.RecommendedAction,
                    rule.ExpectedBenefit,
                    ModelPolicyVersionId: null,
                    PromptTemplateVersion: null,
                    packet.EvidenceReferenceIds,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["content_capture_policy_version"] = "metadata_only",
                        ["recommendation_model_policy_version"] = "deterministic-v1",
                        ["pricing_basis_version"] = "unavailable"
                    },
                    $"audit-recommendation-generation-{Guid.NewGuid():N}",
                    request.CorrelationId,
                    GenerationKey: $"{rule.RuleId}:{hotspot.TokenHotspotId}")));
        }

        return recommendations;
    }

    private static RecommendationRule? TryCreateRule(TokenHotspotRecord hotspot)
    {
        return hotspot.HotspotType switch
        {
            TokenHotspotType.LargeContext => new RecommendationRule(
                "rec.high_input_tokens.narrow_context",
                "Reduce unnecessary context and prefer targeted files or summaries.",
                "Accepted token evidence shows high input token use for this session.",
                "Pass only the files, snippets, and prior context required for the next task step.",
                "Lower input token use and improve cache stability.",
                ToRecommendationConfidence(hotspot.Confidence)),
            TokenHotspotType.GeneratedArtifactBloat => new RecommendationRule(
                "rec.high_output_tokens.constrain_answer",
                "Constrain large generated output with narrower response or diff boundaries.",
                "Accepted token evidence shows high output token use for this session.",
                "Ask for concise diffs, split large changes, or request summaries before full output.",
                "Lower output token use and reduce review effort.",
                ToRecommendationConfidence(hotspot.Confidence)),
            TokenHotspotType.PromptCacheBreakage when hotspot.PromptCacheEvidenceState is PromptCacheEvidenceState.Unknown or PromptCacheEvidenceState.Unavailable =>
                new RecommendationRule(
                    "rec.cache_unavailable.instrumentation_gap",
                    "Treat prompt cache cause as unknown until cache evidence is available.",
                    "Cache evidence was unavailable or unknown for a high input-token session.",
                    "Improve cache telemetry before making specific cache-breakage claims.",
                    "Prevents unsupported cache explanations and keeps coaching evidence-backed.",
                    RecommendationConfidence.High),
            TokenHotspotType.PromptCacheBreakage when hotspot.PromptCacheEvidenceState == PromptCacheEvidenceState.KnownReason =>
                new RecommendationRule(
                    "rec.cache_breakage.stabilize_prefix",
                    "Keep reusable instructions and context stable at the start of the request.",
                    "Prompt cache evidence includes a known cache-breakage reason.",
                    "Move stable context before variable task content and avoid changing reusable prefixes.",
                    "Improve prompt cache reuse and reduce repeated input token cost.",
                    ToRecommendationConfidence(hotspot.Confidence)),
            TokenHotspotType.ToolLoop or TokenHotspotType.ErrorRework => new RecommendationRule(
                "rec.repeated_tool_failure.stop_retry_loop",
                "Stop repeated failed attempts and inspect the underlying command, path, permission, or dependency.",
                "Hotspot evidence indicates repeated tool failure or error rework.",
                "Pause retries, inspect the failing surface, and make a smaller targeted fix.",
                "Reduce repeated token burn from retry loops.",
                ToRecommendationConfidence(hotspot.Confidence)),
            _ => null
        };
    }

    internal static RecommendationEvidencePacket CreateEvidencePacket(
        GenerateRecommendationsRequest request,
        AgentSessionRecord session,
        TokenHotspotRecord hotspot,
        IReadOnlyList<TokenObservationRecord> observations,
        string recommendationModelPolicyVersion,
        string? promptTemplateVersion,
        IReadOnlyList<string> hiddenEvidenceReasons)
    {
        var evidenceReferenceIds = observations
            .Select(static observation => observation.TokenObservationId.ToString())
            .Append(hotspot.TokenHotspotId.ToString())
            .ToArray();
        var generatedAtUtc = DateTimeOffset.UtcNow;
        var packet = new
        {
            schemaVersion = "recommendation.evidence.v1",
            customerOrganizationId = request.CustomerOrganizationId.ToString(),
            sessionId = request.AgentSessionId,
            harness = session.Harness,
            codexSurface = "cli",
            generatedAtUtc,
            policy = new
            {
                contentCapturePolicyVersion = "metadata_only",
                recommendationModelPolicyVersion,
                pricingBasisVersion = "unavailable",
                promptTemplateVersion
            },
            metrics = observations.Select(static observation => new
            {
                tokenObservationId = observation.TokenObservationId.ToString(),
                metricName = observation.MetricName.ToString(),
                observation.Value,
                metricStatus = observation.MetricStatus.ToString(),
                metricConfidence = observation.MetricConfidence.ToString()
            }).ToArray(),
            hotspots = new[]
            {
                new
                {
                    hotspotId = hotspot.TokenHotspotId.ToString(),
                    type = hotspot.HotspotType.ToString(),
                    findingState = hotspot.FindingState.ToString(),
                    attributionType = hotspot.AttributionType.ToString(),
                    confidence = hotspot.Confidence.ToString(),
                    evidenceRefIds = hotspot.EvidenceReferenceIds
                }
            },
            hiddenEvidence = hiddenEvidenceReasons.Select(static reason => new
            {
                reason
            }).ToArray()
        };
        var json = JsonSerializer.Serialize(packet, JsonOptions);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

        return new RecommendationEvidencePacket(
            "recommendation.evidence.v1",
            request.CustomerOrganizationId,
            request.AgentSessionId,
            session.Harness,
            generatedAtUtc,
            evidenceReferenceIds,
            hiddenEvidenceReasons,
            json,
            hash);
    }

    private static RecommendationConfidence ToRecommendationConfidence(TokenHotspotConfidence confidence)
    {
        return confidence switch
        {
            TokenHotspotConfidence.High => RecommendationConfidence.High,
            TokenHotspotConfidence.Medium => RecommendationConfidence.Medium,
            _ => RecommendationConfidence.Low
        };
    }

    private sealed record RecommendationRule(
        string RuleId,
        string Summary,
        string Rationale,
        string RecommendedAction,
        string ExpectedBenefit,
        RecommendationConfidence Confidence);
}
