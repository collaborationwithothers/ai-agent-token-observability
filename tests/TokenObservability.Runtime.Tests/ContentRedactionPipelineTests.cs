using TokenObservability.Domain.Ingestion;

namespace TokenObservability.Runtime.Tests;

public sealed class ContentRedactionPipelineTests
{
    [Fact]
    public async Task PipelineRunsDeterministicRecognizerBeforePiiAndStoresOnlyAfterGatePasses()
    {
        var calls = new List<string>();
        var pii = new RecordingPiiDetector(calls)
        {
            Result = new PiiDetectionResult(
                "hello ************ from ********",
                [
                    new PiiDetectionEntity(
                        Category: "Email",
                        ConfidenceScore: 0.91,
                        ApiVersion: "2026-05-01",
                        ModelVersion: "2021-01-15")
                ])
        };
        var safety = new RecordingContentSafetyClassifier(calls);
        var store = new RecordingRedactedContentStore(calls);
        var pipeline = new ContentRedactionPipeline(pii, safety, store);

        var decision = await pipeline.RedactAsync(new ContentRedactionRequest(
            PolicyVersionId: "policy-content-v1",
            ContentClass.PromptSnippet,
            "hello ghp_123456789012345678901234567890123456 from Ada",
            TotalEnvelopeContentBytes: 64));

        Assert.Equal(ContentRedactionOutcome.Captured, decision.Outcome);
        Assert.Equal(ContentRedactionStatus.Passed, decision.RedactionStatus);
        Assert.Equal(ContentCandidateEvidenceState.Candidate, decision.EvidenceState);
        Assert.Equal("hello ************ from ********", decision.RedactedText);
        Assert.Equal(["pii", "content_safety", "store"], calls);
        Assert.DoesNotContain("ghp_", pii.Inputs.Single(), StringComparison.Ordinal);
        Assert.Equal("Email", decision.Findings.Single(finding => finding.Stage == "azure_ai_language_pii").Category);
        Assert.Equal(0.91, decision.Findings.Single(finding => finding.Stage == "azure_ai_language_pii").ConfidenceScore);
        Assert.Equal("2021-01-15", decision.Findings.Single(finding => finding.Stage == "azure_ai_language_pii").ModelVersion);
        Assert.Equal("policy-content-v1", decision.PolicyVersionId);
        Assert.Equal("content-redaction-pipeline-v1", decision.PipelineVersion);
        Assert.Equal("product-redaction-rules-v1", decision.ProductRuleVersion);
    }

    [Fact]
    public async Task PipelineRoutesLowConfidencePiiToReviewWithoutWritingContent()
    {
        var calls = new List<string>();
        var pii = new RecordingPiiDetector(calls)
        {
            Result = new PiiDetectionResult(
                "candidate from ********",
                [
                    new PiiDetectionEntity(
                        Category: "Person",
                        ConfidenceScore: 0.65,
                        ApiVersion: "2026-05-01",
                        ModelVersion: "2021-01-15")
                ])
        };
        var store = new RecordingRedactedContentStore(calls);
        var pipeline = new ContentRedactionPipeline(pii, new RecordingContentSafetyClassifier(calls), store);

        var decision = await pipeline.RedactAsync(new ContentRedactionRequest(
            PolicyVersionId: "policy-content-v1",
            ContentClass.PromptSnippet,
            "candidate from Ada",
            TotalEnvelopeContentBytes: 64));

        Assert.Equal(ContentRedactionOutcome.ReviewRequired, decision.Outcome);
        Assert.Equal(ContentRedactionStatus.ReviewRequired, decision.RedactionStatus);
        Assert.Equal(ContentCandidateEvidenceState.ReviewRequired, decision.EvidenceState);
        Assert.Equal("pii_low_confidence", decision.DecisionReason);
        Assert.DoesNotContain("store", calls);
        Assert.Null(store.StoredContent);
    }

    [Fact]
    public async Task PipelineRoutesHighConfidencePiiWithoutRedactedTextToReviewWithoutWritingContent()
    {
        var calls = new List<string>();
        var pii = new RecordingPiiDetector(calls)
        {
            Result = new PiiDetectionResult(
                RedactedText: null,
                [
                    new PiiDetectionEntity(
                        Category: "Email",
                        ConfidenceScore: 0.93,
                        ApiVersion: "2026-05-01",
                        ModelVersion: "2021-01-15")
                ])
        };
        var store = new RecordingRedactedContentStore(calls);
        var pipeline = new ContentRedactionPipeline(pii, new RecordingContentSafetyClassifier(calls), store);

        var decision = await pipeline.RedactAsync(new ContentRedactionRequest(
            PolicyVersionId: "policy-content-v1",
            ContentClass.PromptSnippet,
            "email ada@example.test",
            TotalEnvelopeContentBytes: 64));

        Assert.Equal(ContentRedactionOutcome.ReviewRequired, decision.Outcome);
        Assert.Equal(ContentRedactionStatus.ReviewRequired, decision.RedactionStatus);
        Assert.Equal("pii_redacted_text_missing", decision.DecisionReason);
        Assert.DoesNotContain("store", calls);
        Assert.Null(store.StoredContent);
    }

    [Theory]
    [InlineData(true, false, false, 0, "prompt_attack_detected")]
    [InlineData(false, true, false, 0, "indirect_attack_detected")]
    [InlineData(false, false, true, 0, "protected_material_detected")]
    [InlineData(false, false, false, 4, "harmful_content_detected")]
    public async Task PipelineRoutesPolicyEnabledContentSafetyFindingsToReview(
        bool promptAttack,
        bool indirectAttack,
        bool protectedMaterial,
        int maximumHarmSeverity,
        string expectedReason)
    {
        var calls = new List<string>();
        var safety = new RecordingContentSafetyClassifier(calls)
        {
            Result = new ContentSafetyClassificationResult(
                PromptAttackDetected: promptAttack,
                IndirectAttackDetected: indirectAttack,
                ProtectedMaterialDetected: protectedMaterial,
                MaximumHarmSeverity: maximumHarmSeverity,
                ApiVersion: "2024-09-01",
                ModelVersion: "content-safety-v1")
        };
        var store = new RecordingRedactedContentStore(calls);
        var pipeline = new ContentRedactionPipeline(
            new RecordingPiiDetector(calls),
            safety,
            store);

        var decision = await pipeline.RedactAsync(new ContentRedactionRequest(
            PolicyVersionId: "policy-content-v1",
            ContentClass.PromptSnippet,
            "review this content",
            TotalEnvelopeContentBytes: 64));

        Assert.Equal(ContentRedactionOutcome.ReviewRequired, decision.Outcome);
        Assert.Equal(expectedReason, decision.DecisionReason);
        Assert.DoesNotContain("store", calls);
        Assert.Contains(decision.Findings, finding => finding.Stage == "azure_ai_content_safety");
    }

    [Fact]
    public async Task PipelineRoutesSizeAndTimeoutLimitsToReviewWithoutCallingServices()
    {
        var oversizedCalls = new List<string>();
        var oversized = new ContentRedactionPipeline(
            new RecordingPiiDetector(oversizedCalls),
            new RecordingContentSafetyClassifier(oversizedCalls),
            new RecordingRedactedContentStore(oversizedCalls));

        var oversizedDecision = await oversized.RedactAsync(new ContentRedactionRequest(
            PolicyVersionId: "policy-content-v1",
            ContentClass.PromptSnippet,
            new string('a', ContentRedactionLimits.MaxCandidateUtf8Bytes + 1),
            TotalEnvelopeContentBytes: ContentRedactionLimits.MaxCandidateUtf8Bytes + 1));

        Assert.Equal(ContentRedactionOutcome.ReviewRequired, oversizedDecision.Outcome);
        Assert.Equal("candidate_size_limit_exceeded", oversizedDecision.DecisionReason);
        Assert.Empty(oversizedCalls);

        var timeoutCalls = new List<string>();
        var timeout = new ContentRedactionPipeline(
            new RecordingPiiDetector(timeoutCalls)
            {
                Exception = new TimeoutException("PII timeout")
            },
            new RecordingContentSafetyClassifier(timeoutCalls),
            new RecordingRedactedContentStore(timeoutCalls));

        var timeoutDecision = await timeout.RedactAsync(new ContentRedactionRequest(
            PolicyVersionId: "policy-content-v1",
            ContentClass.PromptSnippet,
            "hello Ada",
            TotalEnvelopeContentBytes: 64));

        Assert.Equal(ContentRedactionOutcome.ReviewRequired, timeoutDecision.Outcome);
        Assert.Equal("redaction_stage_timeout", timeoutDecision.DecisionReason);
        Assert.DoesNotContain("store", timeoutCalls);
    }

    [Fact]
    public async Task PipelineEnforcesTotalProcessingCapWhenServiceDoesNotReturn()
    {
        var calls = new List<string>();
        var pipeline = new ContentRedactionPipeline(
            new RecordingPiiDetector(calls),
            new DelayedContentSafetyClassifier(calls, TimeSpan.FromSeconds(10)),
            new RecordingRedactedContentStore(calls),
            new ContentRedactionClock(
                LocalProcessingLimit: TimeSpan.FromSeconds(2),
                TotalProcessingLimit: TimeSpan.FromMilliseconds(50)));

        var decision = await pipeline.RedactAsync(new ContentRedactionRequest(
            PolicyVersionId: "policy-content-v1",
            ContentClass.PromptSnippet,
            "hello Ada",
            TotalEnvelopeContentBytes: 64));

        Assert.Equal(ContentRedactionOutcome.ReviewRequired, decision.Outcome);
        Assert.Equal(ContentRedactionStatus.ReviewRequired, decision.RedactionStatus);
        Assert.Equal("redaction_stage_timeout", decision.DecisionReason);
        Assert.DoesNotContain("store", calls);
    }

    [Fact]
    public async Task PipelineTestsServiceUnavailableAmbiguousEntropyAndRemainingHighRiskSecret()
    {
        var unavailableCalls = new List<string>();
        var unavailable = new ContentRedactionPipeline(
            new RecordingPiiDetector(unavailableCalls)
            {
                Exception = new ContentRedactionServiceUnavailableException("PII unavailable")
            },
            new RecordingContentSafetyClassifier(unavailableCalls),
            new RecordingRedactedContentStore(unavailableCalls));

        var unavailableDecision = await unavailable.RedactAsync(new ContentRedactionRequest(
            PolicyVersionId: "policy-content-v1",
            ContentClass.PromptSnippet,
            "hello Ada",
            TotalEnvelopeContentBytes: 64));

        Assert.Equal(ContentRedactionOutcome.ReviewRequired, unavailableDecision.Outcome);
        Assert.Equal("azure_ai_language_unavailable", unavailableDecision.DecisionReason);

        var ambiguous = await new ContentRedactionPipeline(
                new RecordingPiiDetector([]),
                new RecordingContentSafetyClassifier([]),
                new RecordingRedactedContentStore([]))
            .RedactAsync(new ContentRedactionRequest(
                PolicyVersionId: "policy-content-v1",
                ContentClass.PromptSnippet,
                "secret maybe 0123456789abcdef0123456789abcdef",
                TotalEnvelopeContentBytes: 64));

        Assert.Equal(ContentRedactionOutcome.ReviewRequired, ambiguous.Outcome);
        Assert.Equal("ambiguous_high_entropy_secret", ambiguous.DecisionReason);

        var remainingCalls = new List<string>();
        var remaining = new ContentRedactionPipeline(
            new RecordingPiiDetector(remainingCalls)
            {
                Result = new PiiDetectionResult(
                    "still has ghp_123456789012345678901234567890123456",
                    [])
            },
            new RecordingContentSafetyClassifier(remainingCalls),
            new RecordingRedactedContentStore(remainingCalls));

        var remainingDecision = await remaining.RedactAsync(new ContentRedactionRequest(
            PolicyVersionId: "policy-content-v1",
            ContentClass.PromptSnippet,
            "clean before service",
            TotalEnvelopeContentBytes: 64));

        Assert.Equal(ContentRedactionOutcome.RedactionFailed, remainingDecision.Outcome);
        Assert.Equal("high_risk_secret_remaining", remainingDecision.DecisionReason);
        Assert.DoesNotContain("store", remainingCalls);
    }

    private sealed class RecordingPiiDetector(List<string> calls) : IAzurePiiDetector
    {
        public List<string> Inputs { get; } = [];

        public PiiDetectionResult Result { get; init; } = new(RedactedText: null, Entities: []);

        public Exception? Exception { get; init; }

        public Task<PiiDetectionResult> DetectAsync(string text, CancellationToken cancellationToken)
        {
            calls.Add("pii");
            Inputs.Add(text);

            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(Result);
        }
    }

    private sealed class RecordingContentSafetyClassifier(List<string> calls) : IAzureContentSafetyClassifier
    {
        public ContentSafetyClassificationResult Result { get; init; } = ContentSafetyClassificationResult.None;

        public Task<ContentSafetyClassificationResult> ClassifyAsync(
            ContentClass contentClass,
            string text,
            CancellationToken cancellationToken)
        {
            calls.Add("content_safety");
            return Task.FromResult(Result);
        }
    }

    private sealed class DelayedContentSafetyClassifier(List<string> calls, TimeSpan delay) : IAzureContentSafetyClassifier
    {
        public async Task<ContentSafetyClassificationResult> ClassifyAsync(
            ContentClass contentClass,
            string text,
            CancellationToken cancellationToken)
        {
            calls.Add("content_safety");
            await Task.Delay(delay, cancellationToken);
            return ContentSafetyClassificationResult.None;
        }
    }

    private sealed class RecordingRedactedContentStore(List<string> calls) : IRedactedContentStore
    {
        public string? StoredContent { get; private set; }

        public Task<RedactedContentStorageResult> StoreAsync(ContentRedactionDecision decision, CancellationToken cancellationToken)
        {
            calls.Add("store");
            StoredContent = decision.RedactedText;
            return Task.FromResult(new RedactedContentStorageResult(Stored: true));
        }
    }
}
