namespace TokenObservability.Domain.Recommendations;

public static class RecommendationStructuredOutputValidator
{
    private static readonly string[] UnsafeWords =
    [
        "blame",
        "developer ranking",
        "developer rank",
        "rank the developer",
        "leaderboard",
        "user error",
        "obvious error"
    ];

    private static readonly string[] RawContentMarkers =
    [
        "raw_prompt",
        "prompt_text",
        "code_content",
        "command_output",
        "tool_result"
    ];

    public static RecommendationStructuredOutputValidationResult Validate(
        StructuredRecommendationOutput output,
        RecommendationEvidencePacket evidencePacket)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(evidencePacket);

        var errors = new List<string>();

        if (!StringComparer.Ordinal.Equals(output.SchemaVersion, "recommendation.llm_output.v1"))
        {
            errors.Add("Unsupported schema version.");
        }

        if (string.IsNullOrWhiteSpace(output.GenerationType))
        {
            errors.Add("Generation type is required.");
        }

        if (string.IsNullOrWhiteSpace(output.RecommendationType))
        {
            errors.Add("Recommendation type is required.");
        }

        if (string.IsNullOrWhiteSpace(output.Summary))
        {
            errors.Add("Summary is required.");
        }

        if (string.IsNullOrWhiteSpace(output.RecommendedAction))
        {
            errors.Add("Recommended action is required.");
        }

        if (string.IsNullOrWhiteSpace(output.ExpectedBenefit))
        {
            errors.Add("Expected benefit is required.");
        }

        if (string.IsNullOrWhiteSpace(output.UserFacingWording))
        {
            errors.Add("User-facing wording is required.");
        }

        if (ToConfidence(output.Confidence) is null)
        {
            errors.Add("Confidence is missing or invalid.");
        }

        if (ToAuthorityState(output.AuthorityState) is null)
        {
            errors.Add("Authority state is missing or invalid.");
        }

        if (output.CandidateHotspot is null)
        {
            errors.Add("Candidate hotspot shape is required.");
        }
        else if (output.CandidateHotspot.PromotionEligible)
        {
            errors.Add("LLM output cannot promote candidate hotspots to confirmed findings.");
        }

        if (output.EvidenceReferenceIds is null || output.EvidenceReferenceIds.Count == 0)
        {
            errors.Add("At least one evidence reference is required.");
        }
        else
        {
            foreach (var evidenceReferenceId in output.EvidenceReferenceIds)
            {
                if (string.IsNullOrWhiteSpace(evidenceReferenceId))
                {
                    errors.Add("Evidence references must not be blank.");
                }
                else if (!evidencePacket.EvidenceReferenceIds.Contains(evidenceReferenceId, StringComparer.Ordinal))
                {
                    errors.Add($"Evidence reference '{evidenceReferenceId}' is not present in the evidence packet.");
                }
            }
        }

        if (output.UnsupportedEvidenceGaps is null)
        {
            errors.Add("Unsupported evidence gaps list is required.");
        }

        if (output.SafetyFlags is null)
        {
            errors.Add("Safety flags list is required.");
        }

        if (output.PolicyLimitations is null)
        {
            errors.Add("Policy limitations list is required.");
        }
        else if (evidencePacket.HiddenEvidenceReasons.Count > 0 && output.PolicyLimitations.Count == 0)
        {
            errors.Add("Policy limitations are required when evidence is hidden.");
        }

        var combinedText = string.Join(
            ' ',
            output.Summary,
            output.RecommendedAction,
            output.ExpectedBenefit,
            output.UserFacingWording,
            output.ReviewerNotes);
        if (ContainsAny(combinedText, UnsafeWords))
        {
            errors.Add("Output contains unsupported blame or ranking language.");
        }

        if (ContainsAny(combinedText, RawContentMarkers))
        {
            errors.Add("Output contains raw or blocked content markers.");
        }

        return new RecommendationStructuredOutputValidationResult(errors.Count == 0, output, errors);
    }

    private static RecommendationConfidence? ToConfidence(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "low" => RecommendationConfidence.Low,
            "medium" => RecommendationConfidence.Medium,
            "high" => RecommendationConfidence.High,
            _ => null
        };
    }

    private static RecommendationAuthorityState? ToAuthorityState(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "deterministic" => RecommendationAuthorityState.Deterministic,
            "llm_assisted" => RecommendationAuthorityState.LlmAssisted,
            "llm_inferred_candidate" => RecommendationAuthorityState.LlmInferredCandidate,
            "rejected" => RecommendationAuthorityState.Rejected,
            _ => null
        };
    }

    private static bool ContainsAny(string value, IReadOnlyList<string> needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
}
