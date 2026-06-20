using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TokenObservability.Domain.Ingestion;

public static class ContentRedactionLimits
{
    public const int MaxCandidateUtf8Bytes = 16 * 1024;
    public const int MaxEnvelopeUtf8Bytes = 64 * 1024;
}

public sealed record ContentRedactionRequest(
    string PolicyVersionId,
    ContentClass ContentClass,
    string RawText,
    int TotalEnvelopeContentBytes);

public sealed record ContentRedactionClock(
    TimeSpan LocalProcessingLimit,
    TimeSpan TotalProcessingLimit)
{
    public static ContentRedactionClock Default { get; } = new(
        LocalProcessingLimit: TimeSpan.FromSeconds(2),
        TotalProcessingLimit: TimeSpan.FromSeconds(6));
}

public sealed record ContentRedactionDecision(
    ContentRedactionOutcome Outcome,
    ContentCandidateEvidenceState EvidenceState,
    ContentRedactionStatus RedactionStatus,
    string PolicyVersionId,
    string PipelineVersion,
    string ProductRuleVersion,
    string DecisionReason,
    string? RedactedText,
    string? RedactedContentHash,
    int OriginalLength,
    int? RedactedLength,
    IReadOnlyList<ContentRedactionFinding> Findings);

public sealed record ContentRedactionFinding(
    string Stage,
    string Kind,
    string Category,
    double? ConfidenceScore = null,
    string? ApiVersion = null,
    string? ModelVersion = null);

public sealed record PiiDetectionResult(
    string? RedactedText,
    IReadOnlyList<PiiDetectionEntity> Entities);

public sealed record PiiDetectionEntity(
    string Category,
    double ConfidenceScore,
    string? ApiVersion,
    string? ModelVersion);

public sealed record ContentSafetyClassificationResult(
    bool PromptAttackDetected,
    bool IndirectAttackDetected,
    bool ProtectedMaterialDetected,
    int MaximumHarmSeverity,
    string? ApiVersion,
    string? ModelVersion)
{
    public static ContentSafetyClassificationResult None { get; } = new(
        PromptAttackDetected: false,
        IndirectAttackDetected: false,
        ProtectedMaterialDetected: false,
        MaximumHarmSeverity: 0,
        ApiVersion: null,
        ModelVersion: null);
}

public enum ContentRedactionOutcome
{
    Captured,
    MetadataOnly,
    ReviewRequired,
    RedactionFailed,
    Discarded
}

public interface IAzurePiiDetector
{
    Task<PiiDetectionResult> DetectAsync(string text, CancellationToken cancellationToken);
}

public interface IAzureContentSafetyClassifier
{
    Task<ContentSafetyClassificationResult> ClassifyAsync(
        ContentClass contentClass,
        string text,
        CancellationToken cancellationToken);
}

public interface IRedactedContentStore
{
    Task StoreAsync(ContentRedactionDecision decision, CancellationToken cancellationToken);
}

public sealed class ContentRedactionServiceUnavailableException(string message) : Exception(message);

public interface IContentRedactionPipeline
{
    Task<ContentRedactionDecision> RedactAsync(
        ContentRedactionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ContentRedactionPipeline(
    IAzurePiiDetector piiDetector,
    IAzureContentSafetyClassifier contentSafetyClassifier,
    IRedactedContentStore redactedContentStore,
    ContentRedactionClock? clock = null) : IContentRedactionPipeline
{
    public const string PipelineVersion = "content-redaction-pipeline-v1";
    public const string ProductRuleVersion = "product-redaction-rules-v1";
    private readonly ContentRedactionClock clock = clock ?? ContentRedactionClock.Default;

    private const string RedactionMarker = "[REDACTED]";

    private static readonly Regex GithubTokenRegex = new(
        @"\b(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9_]{20,}\b|\bgithub_pat_[A-Za-z0-9_]{20,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex JwtRegex = new(
        @"\b[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BearerTokenRegex = new(
        @"(?i)\bbearer\s+[A-Za-z0-9._~+/=-]{20,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PasswordAssignmentRegex = new(
        @"(?i)\b(password|pwd|client_secret|secret|token|api_key)\s*=\s*[^;\s]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BasicAuthUrlRegex = new(
        @"(?i)\bhttps?://[^/\s:@]+:[^/\s@]+@",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SasUrlRegex = new(
        @"(?i)([?&]sig=)[^&\s]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ConnectionStringRegex = new(
        @"(?i)\b(?:AccountKey|SharedAccessKey|Password|Pwd|User ID|Endpoint|HostName|AccountEndpoint)\s*=\s*[^;\s]+(?:;[^;\r\n]+)*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PrivateKeyRegex = new(
        @"-----BEGIN (?:OPENSSH|RSA|EC|DSA)? ?PRIVATE KEY-----[\s\S]*?-----END (?:OPENSSH|RSA|EC|DSA)? ?PRIVATE KEY-----",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WebhookRegex = new(
        @"(?i)\bhttps://[^/\s]*(?:hooks\.slack\.com|discord(?:app)?\.com/api/webhooks|outlook\.office\.com/webhook|webhook)[^\s]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AmbiguousHighEntropyRegex = new(
        @"(?i)\b(?:secret|token|key|credential|password)\b.{0,24}\b[A-Za-z0-9+/=_-]{32,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<ContentRedactionDecision> RedactAsync(
        ContentRedactionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var policyVersionId = RequireText(request.PolicyVersionId, nameof(request.PolicyVersionId));
        var rawText = request.RawText ?? throw new ArgumentException("Raw text is required.", nameof(request));
        var originalUtf8Length = Encoding.UTF8.GetByteCount(rawText);

        if (clock.LocalProcessingLimit <= TimeSpan.Zero || clock.TotalProcessingLimit <= TimeSpan.Zero)
        {
            throw new ArgumentException("Redaction processing limits must be positive.", nameof(clock));
        }

        using var totalTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        totalTimeout.CancelAfter(clock.TotalProcessingLimit);

        if (originalUtf8Length > ContentRedactionLimits.MaxCandidateUtf8Bytes)
        {
            return ReviewRequired(request, policyVersionId, "candidate_size_limit_exceeded", Findings: []);
        }

        if (request.TotalEnvelopeContentBytes > ContentRedactionLimits.MaxEnvelopeUtf8Bytes)
        {
            return ReviewRequired(request, policyVersionId, "envelope_size_limit_exceeded", Findings: []);
        }

        if (AmbiguousHighEntropyRegex.IsMatch(rawText))
        {
            return ReviewRequired(
                request,
                policyVersionId,
                "ambiguous_high_entropy_secret",
                [new ContentRedactionFinding("deterministic_secret", "review_required", "high_entropy_credential")]);
        }

        var findings = new List<ContentRedactionFinding>();
        using var localTimeout = CancellationTokenSource.CreateLinkedTokenSource(totalTimeout.Token);
        localTimeout.CancelAfter(clock.LocalProcessingLimit);
        var deterministicRedacted = RedactDeterministicSecrets(rawText, findings);

        if (localTimeout.IsCancellationRequested || totalTimeout.IsCancellationRequested)
        {
            return ReviewRequired(request, policyVersionId, "redaction_stage_timeout", findings);
        }

        PiiDetectionResult piiResult;
        try
        {
            piiResult = await piiDetector.DetectAsync(deterministicRedacted, totalTimeout.Token);
        }
        catch (TimeoutException)
        {
            return ReviewRequired(request, policyVersionId, "redaction_stage_timeout", findings);
        }
        catch (OperationCanceledException) when (totalTimeout.IsCancellationRequested)
        {
            return ReviewRequired(request, policyVersionId, "redaction_stage_timeout", findings);
        }
        catch (ContentRedactionServiceUnavailableException)
        {
            return ReviewRequired(request, policyVersionId, "azure_ai_language_unavailable", findings);
        }

        foreach (var entity in piiResult.Entities)
        {
            findings.Add(new ContentRedactionFinding(
                "azure_ai_language_pii",
                "entity",
                entity.Category,
                entity.ConfidenceScore,
                entity.ApiVersion,
                entity.ModelVersion));
        }

        if (piiResult.Entities.Count > 0 && string.IsNullOrWhiteSpace(piiResult.RedactedText))
        {
            return ReviewRequired(request, policyVersionId, "pii_redacted_text_missing", findings);
        }

        var piiRedacted = piiResult.RedactedText ?? deterministicRedacted;

        if (piiResult.Entities.Any(entity => entity.ConfidenceScore >= 0.50 && entity.ConfidenceScore < 0.80))
        {
            return ReviewRequired(request, policyVersionId, "pii_low_confidence", findings);
        }

        ContentSafetyClassificationResult safetyResult;
        try
        {
            safetyResult = await contentSafetyClassifier.ClassifyAsync(
                request.ContentClass,
                piiRedacted,
                totalTimeout.Token);
        }
        catch (TimeoutException)
        {
            return ReviewRequired(request, policyVersionId, "redaction_stage_timeout", findings);
        }
        catch (OperationCanceledException) when (totalTimeout.IsCancellationRequested)
        {
            return ReviewRequired(request, policyVersionId, "redaction_stage_timeout", findings);
        }

        if (safetyResult != ContentSafetyClassificationResult.None)
        {
            findings.Add(new ContentRedactionFinding(
                "azure_ai_content_safety",
                "classification",
                ToContentSafetyCategory(safetyResult),
                null,
                safetyResult.ApiVersion,
                safetyResult.ModelVersion));
        }

        if (safetyResult.PromptAttackDetected)
        {
            return ReviewRequired(request, policyVersionId, "prompt_attack_detected", findings);
        }

        if (safetyResult.IndirectAttackDetected)
        {
            return ReviewRequired(request, policyVersionId, "indirect_attack_detected", findings);
        }

        if (safetyResult.ProtectedMaterialDetected)
        {
            return ReviewRequired(request, policyVersionId, "protected_material_detected", findings);
        }

        if (safetyResult.MaximumHarmSeverity >= 4)
        {
            return ReviewRequired(request, policyVersionId, "harmful_content_detected", findings);
        }

        var productRedacted = ApplyProductRules(piiRedacted, findings);
        var redactedUtf8Length = Encoding.UTF8.GetByteCount(productRedacted);

        if (redactedUtf8Length > ContentRedactionLimits.MaxCandidateUtf8Bytes)
        {
            return ReviewRequired(request, policyVersionId, "redacted_content_size_limit_exceeded", findings);
        }

        if (ContainsHighRiskSecret(productRedacted))
        {
            return RedactionFailed(request, policyVersionId, "high_risk_secret_remaining", findings);
        }

        var decision = new ContentRedactionDecision(
            ContentRedactionOutcome.Captured,
            ContentCandidateEvidenceState.Candidate,
            ContentRedactionStatus.Passed,
            policyVersionId,
            PipelineVersion,
            ProductRuleVersion,
            "redaction_passed",
            productRedacted,
            ComputeSha256Hex(productRedacted),
            request.RawText.Length,
            productRedacted.Length,
            findings);

        await redactedContentStore.StoreAsync(decision, cancellationToken);
        return decision;
    }

    private static string RedactDeterministicSecrets(
        string text,
        ICollection<ContentRedactionFinding> findings)
    {
        var redacted = text;
        redacted = Redact(redacted, PrivateKeyRegex, "private_key", findings);
        redacted = Redact(redacted, GithubTokenRegex, "github_token", findings);
        redacted = Redact(redacted, JwtRegex, "jwt", findings);
        redacted = Redact(redacted, SasUrlRegex, "azure_sas", findings);
        redacted = Redact(redacted, ConnectionStringRegex, "connection_string", findings);
        redacted = Redact(redacted, BearerTokenRegex, "oauth_bearer_token", findings);
        redacted = Redact(redacted, BasicAuthUrlRegex, "basic_auth_url", findings);
        redacted = Redact(redacted, WebhookRegex, "webhook_url", findings);
        redacted = Redact(redacted, PasswordAssignmentRegex, "password_assignment", findings);
        return redacted;
    }

    private static string Redact(
        string text,
        Regex regex,
        string category,
        ICollection<ContentRedactionFinding> findings)
    {
        if (!regex.IsMatch(text))
        {
            return text;
        }

        findings.Add(new ContentRedactionFinding(
            "deterministic_secret",
            "redacted",
            category,
            ApiVersion: ProductRuleVersion,
            ModelVersion: ProductRuleVersion));
        return regex.Replace(text, RedactionMarker);
    }

    private static string ApplyProductRules(
        string text,
        ICollection<ContentRedactionFinding> findings)
    {
        var redacted = text;

        if (redacted.Contains("/Users/", StringComparison.Ordinal))
        {
            redacted = Regex.Replace(
                redacted,
                @"/Users/[A-Za-z0-9._-]+",
                "/Users/[REDACTED_USER]",
                RegexOptions.CultureInvariant);
            findings.Add(new ContentRedactionFinding(
                "product_specific",
                "redacted",
                "local_user_path",
                ApiVersion: ProductRuleVersion,
                ModelVersion: ProductRuleVersion));
        }

        return redacted;
    }

    private static bool ContainsHighRiskSecret(string text)
    {
        return GithubTokenRegex.IsMatch(text) ||
            JwtRegex.IsMatch(text) ||
            BearerTokenRegex.IsMatch(text) ||
            PasswordAssignmentRegex.IsMatch(text) ||
            BasicAuthUrlRegex.IsMatch(text) ||
            SasUrlRegex.IsMatch(text) ||
            ConnectionStringRegex.IsMatch(text) ||
            PrivateKeyRegex.IsMatch(text) ||
            WebhookRegex.IsMatch(text);
    }

    private static ContentRedactionDecision ReviewRequired(
        ContentRedactionRequest request,
        string policyVersionId,
        string reason,
        IReadOnlyList<ContentRedactionFinding> Findings)
    {
        return new ContentRedactionDecision(
            ContentRedactionOutcome.ReviewRequired,
            ContentCandidateEvidenceState.ReviewRequired,
            ContentRedactionStatus.ReviewRequired,
            policyVersionId,
            PipelineVersion,
            ProductRuleVersion,
            reason,
            RedactedText: null,
            RedactedContentHash: null,
            request.RawText?.Length ?? 0,
            RedactedLength: null,
            Findings);
    }

    private static ContentRedactionDecision RedactionFailed(
        ContentRedactionRequest request,
        string policyVersionId,
        string reason,
        IReadOnlyList<ContentRedactionFinding> Findings)
    {
        return new ContentRedactionDecision(
            ContentRedactionOutcome.RedactionFailed,
            ContentCandidateEvidenceState.RedactionFailed,
            ContentRedactionStatus.Failed,
            policyVersionId,
            PipelineVersion,
            ProductRuleVersion,
            reason,
            RedactedText: null,
            RedactedContentHash: null,
            request.RawText?.Length ?? 0,
            RedactedLength: null,
            Findings);
    }

    private static string ToContentSafetyCategory(ContentSafetyClassificationResult result)
    {
        if (result.PromptAttackDetected)
        {
            return "prompt_attack";
        }

        if (result.IndirectAttackDetected)
        {
            return "indirect_attack";
        }

        if (result.ProtectedMaterialDetected)
        {
            return "protected_material";
        }

        return result.MaximumHarmSeverity >= 4
            ? "harmful_content"
            : "none";
    }

    private static string RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value must not be empty.", parameterName);
        }

        return value.Trim();
    }

    private static string ComputeSha256Hex(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}

public sealed class UnavailableAzurePiiDetector : IAzurePiiDetector
{
    public Task<PiiDetectionResult> DetectAsync(string text, CancellationToken cancellationToken)
    {
        throw new ContentRedactionServiceUnavailableException("Azure AI Language PII detection is not configured.");
    }
}

public sealed class NoopAzureContentSafetyClassifier : IAzureContentSafetyClassifier
{
    public Task<ContentSafetyClassificationResult> ClassifyAsync(
        ContentClass contentClass,
        string text,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(ContentSafetyClassificationResult.None);
    }
}

public sealed class NoopRedactedContentStore : IRedactedContentStore
{
    public Task StoreAsync(ContentRedactionDecision decision, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
