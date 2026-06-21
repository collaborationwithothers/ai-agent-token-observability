using System.Net.Http.Json;
using System.Text.Json;
using TokenObservability.Domain.Ingestion;
using TokenObservability.Domain.Recommendations;
using TokenObservability.Infrastructure.Persistence;

namespace TokenObservability.Infrastructure.Recommendations;

public interface IRecommendationLlmClient
{
    Task<StructuredRecommendationOutput> GenerateAsync(
        RecommendationLlmGenerationInput input,
        CancellationToken cancellationToken = default);
}

public sealed record RecommendationLlmGenerationInput(
    RecommendationModelPolicyRecord Policy,
    RecommendationPromptTemplateRecord PromptTemplate,
    RecommendationEvidencePacket EvidencePacket,
    string DeploymentAlias,
    string StructuredOutputSchemaJson);

public sealed class AzureOpenAiRecommendationClient(HttpClient httpClient, Uri endpoint) : IRecommendationLlmClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<StructuredRecommendationOutput> GenerateAsync(
        RecommendationLlmGenerationInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var request = new
        {
            model = input.DeploymentAlias,
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = "Draft evidence-backed token optimization coaching. Use only supplied evidence. Do not rank or blame people. Return structured JSON only."
                },
                new
                {
                    role = "user",
                    content = input.EvidencePacket.Json
                }
            },
            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "recommendation_llm_output",
                    strict = true,
                    schema = JsonSerializer.Deserialize<JsonElement>(input.StructuredOutputSchemaJson, JsonOptions)
                }
            }
        };

        using var response = await httpClient.PostAsJsonAsync(endpoint, request, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Azure OpenAI response did not include structured recommendation content.");
        }

        return JsonSerializer.Deserialize<StructuredRecommendationOutput>(content, JsonOptions) ??
            throw new InvalidOperationException("Azure OpenAI structured recommendation content could not be parsed.");
    }
}

public sealed class LlmAssistedRecommendationGenerator(
    InMemoryTenantMetadataStore tenantMetadataStore,
    IRecommendationLlmClient? llmClient = null)
{
    private const string StructuredOutputSchemaVersion = "recommendation.llm_output.v1";
    private static readonly RecommendationEvidenceClass[] RequiredEvidenceClasses =
    [
        RecommendationEvidenceClass.CustomerOrganization,
        RecommendationEvidenceClass.SessionMetadata,
        RecommendationEvidenceClass.HarnessMetadata,
        RecommendationEvidenceClass.TokenObservation,
        RecommendationEvidenceClass.TokenHotspot,
        RecommendationEvidenceClass.HiddenEvidenceMarker,
        RecommendationEvidenceClass.PolicyMetadata
    ];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<RecommendationRecord>> GenerateForSessionAsync(
        GenerateRecommendationsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var policy = await tenantMetadataStore.GetActiveRecommendationModelPolicyAsync(request.CustomerOrganizationId);
        if (policy is null || !policy.LlmAssistedEnabled || llmClient is null)
        {
            return await new DeterministicRecommendationGenerator(tenantMetadataStore).GenerateForSessionAsync(request);
        }

        var promptTemplate = await tenantMetadataStore.FindRecommendationPromptTemplateAsync(
            request.CustomerOrganizationId,
            policy.PromptTemplateVersion);
        if (promptTemplate is null || promptTemplate.State != RecommendationPromptTemplateState.Active)
        {
            return await new DeterministicRecommendationGenerator(tenantMetadataStore).GenerateForSessionAsync(request);
        }

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

        if (!AllowsRequiredEvidenceClasses(policy))
        {
            foreach (var hotspot in hotspots)
            {
                var packet = DeterministicRecommendationGenerator.CreateEvidencePacket(
                    request,
                    session,
                    hotspot,
                    observations,
                    policy.PolicyVersionId,
                    promptTemplate.PromptTemplateVersion,
                    hiddenEvidenceReasons: ["content_policy_limited"]);
                await RecordFailureAsync(request, hotspot, policy, promptTemplate, packet, "evidence_class_not_allowed");
            }

            return await new DeterministicRecommendationGenerator(tenantMetadataStore).GenerateForSessionAsync(request);
        }

        var generated = new List<RecommendationRecord>();
        foreach (var hotspot in hotspots)
        {
            var packet = DeterministicRecommendationGenerator.CreateEvidencePacket(
                request,
                session,
                hotspot,
                observations,
                policy.PolicyVersionId,
                promptTemplate.PromptTemplateVersion,
                hiddenEvidenceReasons: ["content_policy_limited"]);
            try
            {
                var output = await llmClient.GenerateAsync(
                    new RecommendationLlmGenerationInput(
                        policy,
                        promptTemplate,
                        packet,
                        policy.PrimaryDeploymentAlias,
                        StructuredOutputSchemaJson()),
                    cancellationToken);
                var validation = RecommendationStructuredOutputValidator.Validate(
                    output,
                    packet,
                    new RecommendationStructuredOutputValidationContext(
                        InMemoryTenantMetadataStore.ToWireRecommendationModelProvider(policy.Provider),
                        policy.PrimaryDeploymentAlias,
                        policy.ModelFamilyOrSku,
                        policy.ModelVersion,
                        policy.PolicyVersionId,
                        promptTemplate.PromptTemplateVersion));
                if (!validation.IsValid)
                {
                    await RecordFailureAsync(request, hotspot, policy, promptTemplate, packet, "validation_failed");
                    continue;
                }

                if (StringComparer.Ordinal.Equals(output.AuthorityState, "rejected"))
                {
                    await RecordFailureAsync(request, hotspot, policy, promptTemplate, packet, "llm_output_rejected");
                    continue;
                }

                var recommendationHotspotId = await CreateLlmCandidateHotspotIfNeededAsync(
                    request,
                    hotspot,
                    policy,
                    output);

                generated.Add(await tenantMetadataStore.CreateRecommendationAsync(
                    new CreateRecommendationRecordRequest(
                        request.CustomerOrganizationId,
                        request.AgentSessionId,
                        recommendationHotspotId ?? hotspot.TokenHotspotId,
                        RuleId: null,
                        RecommendationKind.LlmAssisted,
                        RecommendationState.Candidate,
                        ToAuthorityState(output.AuthorityState),
                        ToConfidence(output.Confidence),
                        RecommendationValidationState.Pending,
                        RecommendationVisibilityScope.TeamScoped,
                        packet.SchemaVersion,
                        packet.Json,
                        packet.Hash,
                        output.Summary,
                        output.UserFacingWording,
                        output.RecommendedAction,
                        output.ExpectedBenefit,
                        policy.PolicyVersionId,
                        promptTemplate.PromptTemplateVersion,
                        output.EvidenceReferenceIds,
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["content_capture_policy_version"] = "metadata_only",
                            ["recommendation_model_policy_version"] = policy.PolicyVersionId,
                            ["pricing_basis_version"] = "unavailable",
                            ["provider_name"] = InMemoryTenantMetadataStore.ToWireRecommendationModelProvider(policy.Provider),
                            ["model_name"] = policy.ModelFamilyOrSku,
                            ["deployment_alias"] = policy.PrimaryDeploymentAlias,
                            ["prompt_template_version"] = promptTemplate.PromptTemplateVersion,
                            ["structured_output_schema_version"] = policy.StructuredOutputSchemaVersion
                        },
                        $"audit-recommendation-generation-{Guid.NewGuid():N}",
                        request.CorrelationId,
                        GenerationKey: $"llm:{policy.PolicyVersionId}:{promptTemplate.PromptTemplateVersion}:{recommendationHotspotId ?? hotspot.TokenHotspotId}")));
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
            {
                await RecordFailureAsync(request, hotspot, policy, promptTemplate, packet, "llm_generation_failed");
            }
        }

        if (generated.Count == 0)
        {
            return await new DeterministicRecommendationGenerator(tenantMetadataStore).GenerateForSessionAsync(request);
        }

        return generated;
    }

    private static bool AllowsRequiredEvidenceClasses(RecommendationModelPolicyRecord policy)
    {
        var allowed = policy.AllowedEvidenceClasses.ToHashSet();
        return RequiredEvidenceClasses.All(allowed.Contains);
    }

    private async Task<TokenHotspotId?> CreateLlmCandidateHotspotIfNeededAsync(
        GenerateRecommendationsRequest request,
        TokenHotspotRecord sourceHotspot,
        RecommendationModelPolicyRecord policy,
        StructuredRecommendationOutput output)
    {
        if (!StringComparer.Ordinal.Equals(output.AuthorityState, "llm_inferred_candidate") ||
            !output.CandidateHotspot.Proposed ||
            !TryToHotspotType(output.CandidateHotspot.Type, out var hotspotType))
        {
            return null;
        }

        var candidate = await tenantMetadataStore.RecordTokenHotspotAsync(
            new CreateTokenHotspotRecordRequest(
                request.CustomerOrganizationId,
                request.AgentSessionId,
                hotspotType,
                TokenHotspotFindingState.CandidateLlmInferred,
                TokenHotspotAttributionType.LlmInferred,
                ToHotspotConfidence(output.Confidence),
                TokenMetricStatus.Unavailable,
                TokenMetricConfidence.Unavailable,
                hotspotType == TokenHotspotType.PromptCacheBreakage
                    ? PromptCacheEvidenceState.InferredCandidate
                    : PromptCacheEvidenceState.NotApplicable,
                policy.ModelFamilyOrSku,
                output.Summary,
                output.EvidenceReferenceIds,
                TokenBurnScore: null,
                EstimatedCostImpact: null,
                DetectionKey: $"llm-candidate:{policy.PolicyVersionId}:{sourceHotspot.TokenHotspotId}:{hotspotType}"));

        return candidate.TokenHotspotId;
    }

    private async Task RecordFailureAsync(
        GenerateRecommendationsRequest request,
        TokenHotspotRecord hotspot,
        RecommendationModelPolicyRecord policy,
        RecommendationPromptTemplateRecord promptTemplate,
        RecommendationEvidencePacket packet,
        string failureCode)
    {
        await tenantMetadataStore.RecordRecommendationLlmGenerationFailureAsync(
            new CreateRecommendationLlmGenerationFailureRequest(
                request.CustomerOrganizationId,
                request.AgentSessionId,
                hotspot.TokenHotspotId,
                failureCode,
                InMemoryTenantMetadataStore.ToWireRecommendationModelProvider(policy.Provider),
                policy.PrimaryDeploymentAlias,
                policy.PolicyVersionId,
                promptTemplate.PromptTemplateVersion,
                packet.Hash,
                policy.StructuredOutputSchemaVersion,
                $"audit-recommendation-llm-failure-{Guid.NewGuid():N}",
                request.CorrelationId));
    }

    private static RecommendationConfidence ToConfidence(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "high" => RecommendationConfidence.High,
            "medium" => RecommendationConfidence.Medium,
            _ => RecommendationConfidence.Low
        };
    }

    private static RecommendationAuthorityState ToAuthorityState(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "llm_inferred_candidate" => RecommendationAuthorityState.LlmInferredCandidate,
            "rejected" => RecommendationAuthorityState.Rejected,
            _ => RecommendationAuthorityState.LlmAssisted
        };
    }

    private static TokenHotspotConfidence ToHotspotConfidence(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "high" => TokenHotspotConfidence.High,
            "medium" => TokenHotspotConfidence.Medium,
            "low" => TokenHotspotConfidence.Low,
            _ => TokenHotspotConfidence.Unavailable
        };
    }

    private static bool TryToHotspotType(string? value, out TokenHotspotType hotspotType)
    {
        hotspotType = TokenHotspotType.Unknown;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();
        return normalized switch
        {
            "prompt_cache_breakage" => Assign(TokenHotspotType.PromptCacheBreakage, out hotspotType),
            "large_context" => Assign(TokenHotspotType.LargeContext, out hotspotType),
            "tool_loop" => Assign(TokenHotspotType.ToolLoop, out hotspotType),
            "model_retry" => Assign(TokenHotspotType.ModelRetry, out hotspotType),
            "repo_context_bloat" => Assign(TokenHotspotType.RepoContextBloat, out hotspotType),
            "generated_artifact_bloat" => Assign(TokenHotspotType.GeneratedArtifactBloat, out hotspotType),
            "expensive_model_choice" => Assign(TokenHotspotType.ExpensiveModelChoice, out hotspotType),
            "error_rework" => Assign(TokenHotspotType.ErrorRework, out hotspotType),
            "unknown" => Assign(TokenHotspotType.Unknown, out hotspotType),
            _ => false
        };

        static bool Assign(TokenHotspotType value, out TokenHotspotType target)
        {
            target = value;
            return true;
        }
    }

    private static string StructuredOutputSchemaJson()
    {
        return JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["schemaVersion"] = new { type = "string" },
                ["generationType"] = new { type = "string" },
                ["recommendationType"] = new { type = "string" },
                ["candidateHotspot"] = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["proposed"] = new { type = "boolean" },
                        ["type"] = new { type = new[] { "string", "null" } },
                        ["label"] = new { type = new[] { "string", "null" } },
                        ["promotionEligible"] = new { type = "boolean" }
                    },
                    required = new[] { "proposed", "type", "label", "promotionEligible" },
                    additionalProperties = false
                },
                ["summary"] = new { type = "string" },
                ["recommendedAction"] = new { type = "string" },
                ["expectedBenefit"] = new { type = "string" },
                ["evidenceReferenceIds"] = new { type = "array", items = new { type = "string" } },
                ["unsupportedEvidenceGaps"] = new { type = "array", items = new { type = "string" } },
                ["confidence"] = new { type = "string", @enum = new[] { "low", "medium", "high" } },
                ["authorityState"] = new { type = "string", @enum = new[] { "llm_assisted", "llm_inferred_candidate", "rejected" } },
                ["safetyFlags"] = new { type = "array", items = new { type = "string" } },
                ["policyLimitations"] = new { type = "array", items = new { type = "string" } },
                ["userFacingWording"] = new { type = "string" },
                ["reviewerNotes"] = new { type = "string" }
            },
            required = new[]
            {
                "schemaVersion",
                "generationType",
                "recommendationType",
                "candidateHotspot",
                "summary",
                "recommendedAction",
                "expectedBenefit",
                "evidenceReferenceIds",
                "unsupportedEvidenceGaps",
                "confidence",
                "authorityState",
                "safetyFlags",
                "policyLimitations",
                "userFacingWording",
                "reviewerNotes"
            },
            additionalProperties = false
        }, JsonOptions);
    }
}
