# Recommendation Engine Architecture

## Purpose

This document defines how the product creates, validates, stores, explains, and displays Token Hotspots and recommendations.

The recommendation engine must improve agentic coding workflows and reduce token burn without creating unsupported findings, blame workflows, or opaque LLM advice.

## Source Documents

- [Production Target State Spec](../specs/production-target-state.md)
- [Azure Production MVP PRD](../prd/azure-production-mvp.md)
- [Azure Production Architecture](./azure-production-architecture.md)
- [Content Capture And Redaction Architecture](./content-capture-and-redaction.md)

## Core Decisions

- Token Hotspots must preserve Hotspot Attribution Type: direct, correlated, or inferred.
- LLMs must not be the sole authority for confirmed Token Hotspots or factual user-error claims.
- LLM-Inferred Candidate Hotspots are allowed only when grounded in explicit evidence and labelled as candidates.
- Deterministic Recommendations are allowed when rule triggers and evidence references are known.
- LLM-Assisted Recommendations are allowed when Recommendation Model Policy permits them and the model only uses supplied evidence.
- Recommendations are generated asynchronously after ingestion and hotspot detection.
- Authorized users can request regeneration when evidence, policy, model, or prompt-template versions change.
- Prompt Cache Breakage is a first-class hotspot type when cache evidence is observed, correlated, or responsibly inferred.
- Recommendation outputs must store evidence references, confidence, source type, model or rule metadata, prompt-template version, policy version, and validation state.

## Authority Model

The engine distinguishes observations, correlations, inferences, and LLM output.

| Output | Authority | UI state |
| --- | --- | --- |
| Observed metric | Harness or provider telemetry field | Observed |
| Deterministic hotspot | Rule over observed or normalized evidence | Direct or correlated |
| Statistical hotspot | Threshold, anomaly, or comparison over normalized evidence | Correlated |
| LLM-inferred candidate hotspot | LLM output grounded in evidence packet | Candidate |
| Deterministic recommendation | Rule output tied to a hotspot | Deterministic |
| LLM-assisted recommendation | LLM explanation or advice grounded in evidence packet | LLM-assisted |
| Unsupported claim | Missing evidence or failed validation | Not stored as finding |

LLM output can explain, rank, summarize, or propose candidates. It cannot promote itself to a confirmed hotspot.

## Recommendation Pipeline

```text
Normalized telemetry
  |
  v
Evidence preparation
  |
  +--> deterministic hotspot rules
  |
  +--> statistical hotspot detection
  |
  +--> cache diagnostics
  |
  v
Evidence packet
  |
  +--> deterministic recommendation rules
  |
  +--> LLM candidate hotspot generation, if policy allows
  |
  +--> LLM-assisted recommendation generation, if policy allows
  |
  v
Validation gates
  |
  +--> accepted recommendation
  |
  +--> candidate hotspot pending validation
  |
  +--> rejected unsupported output
  |
  v
Product Metadata Store
```

## Evidence Packet

Every recommendation or LLM candidate hotspot is generated from an evidence packet.

An evidence packet includes only policy-allowed information:

- Customer Organization.
- Session ID.
- Harness and Codex Surface.
- Developer identity scope, if the caller is authorized.
- Repository and team scope.
- Model invocation summaries.
- Token metrics and Metric Status.
- Estimated Token Cost and cost status.
- Cache Evidence Availability.
- Tool call summaries.
- Error, retry, and approval summaries.
- Hotspot candidates.
- Correlatable Repository Evidence.
- Redacted content excerpts, if Content Capture Policy and Recommendation Model Policy allow them.
- Evidence explicitly hidden by policy.
- Retention and policy versions.

The evidence packet must be immutable for the generated recommendation version. If evidence changes, regenerate rather than mutate the historical recommendation.

### Evidence Packet Schema

The MVP evidence packet is stored as versioned JSON metadata in the Product Metadata Store. Large redacted excerpts remain content references, not inline payloads.

```json
{
  "schemaVersion": "recommendation.evidence.v1",
  "customerOrganizationId": "co_123",
  "sessionId": "ses_123",
  "harness": "codex",
  "codexSurface": "cli",
  "generatedAtUtc": "2026-06-14T12:00:00Z",
  "policy": {
    "contentCapturePolicyVersion": "ccp_1",
    "recommendationModelPolicyVersion": "rmp_1",
    "pricingBasisVersion": "price_1"
  },
  "scope": {
    "teamId": "team_123",
    "repositoryId": "repo_123",
    "developerIdentityScope": "own_or_authorized"
  },
  "metrics": {
    "inputTokens": { "value": 120000, "status": "observed", "confidence": "high" },
    "outputTokens": { "value": 14000, "status": "observed", "confidence": "high" },
    "cachedInputTokens": { "value": null, "status": "unavailable", "confidence": "none" },
    "estimatedCost": { "value": 12.34, "currency": "USD", "status": "estimated" }
  },
  "cacheEvidence": {
    "availability": "unavailable",
    "observedFields": [],
    "correlatedSignals": []
  },
  "timelineSummary": [
    {
      "turnId": "turn_1",
      "timeUtc": "2026-06-14T12:01:00Z",
      "toolCallCount": 4,
      "errorCount": 2,
      "tokenObservationIds": ["tok_1"]
    }
  ],
  "hotspots": [
    {
      "hotspotId": "hot_1",
      "type": "repeated_tool_failure",
      "attributionType": "correlated",
      "confidence": "medium",
      "evidenceRefIds": ["ev_1", "ev_2"]
    }
  ],
  "contentEvidence": [
    {
      "contentReferenceId": "cr_1",
      "state": "redacted_summary",
      "contentClass": "tool_output_excerpt",
      "recommendationEligible": true
    }
  ],
  "hiddenEvidence": [
    {
      "reason": "policy_hidden",
      "contentClass": "prompt_snippet"
    }
  ]
}
```

Required MVP invariants:

- Every ID in LLM output must exist in the evidence packet.
- Every metric includes Metric Status and Metric Confidence where applicable.
- Hidden evidence is represented explicitly so the model and UI can say evidence is unavailable.
- Raw blocked content is never included.
- Redacted excerpts are referenced by content reference ID and included inline only when policy explicitly allows bounded excerpts.

## Hotspot Types

MVP hotspot types:

- High Token Burn session.
- High input token burn.
- High output token burn.
- Repeated tool failure.
- Retry loop or repeated failed attempt.
- Prompt Cache Breakage, where evidence exists.
- Oversized or unstable context, where evidence exists.
- Content capture unavailable or policy-hidden evidence state.

Target-state hotspot types:

- Cross-harness model cost mix anomaly.
- Repository context bloat.
- Spec or instruction bloat.
- Repeated prompt churn.
- Model mismatch.
- Tool-associated token burn.
- Workflow-level token burn patterns.
- LLM-inferred candidate hotspot types approved by product validation.

## Prompt Cache Diagnostics

Prompt Cache Breakage is a supported MVP hotspot when evidence exists.

Cache Evidence Availability values:

- `observed`: provider or harness emits cache fields such as cached token counts, cache read tokens, or cache creation tokens.
- `correlated`: token patterns and stable context signatures strongly suggest cache behavior but provider fields are missing.
- `llm_inferred`: an LLM proposes a likely cache-breakage cause from a supplied evidence packet.
- `unavailable`: telemetry or policy does not provide enough evidence.

The product can explain why cache effectiveness likely dropped only when evidence supports the claim.

Examples of cache-breakage explanations:

- Stable prefix changed.
- Reusable context moved later in the request.
- Tool schema changed before the reusable block.
- System or developer instructions changed.
- The request was below provider cache threshold.
- Concurrent requests arrived before a cache entry was available.
- Provider or harness did not emit cache evidence.
- Content policy hid the evidence needed for a stronger explanation.

Provider facts used by the design:

- OpenAI reports `cached_tokens` in usage details and prompt caching is available for prompts containing at least 1024 tokens: https://developers.openai.com/api/docs/guides/prompt-caching
- Azure OpenAI prompt caching reduces latency and cost for longer prompts with identical beginning content: https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/prompt-caching
- Anthropic exposes cache creation and cache read token fields and shorter prompts may process without caching when below minimum length: https://docs.anthropic.com/es/docs/build-with-claude/prompt-caching
- OpenTelemetry GenAI event conventions say input token usage should include cached tokens: https://opentelemetry.io/docs/specs/semconv/gen-ai/gen-ai-events/

## Deterministic Recommendations

Deterministic Recommendations are generated from rules with explicit triggers.

Each rule must define:

- Rule ID.
- Trigger condition.
- Required evidence.
- Excluded evidence states.
- Recommended action.
- Expected benefit.
- Confidence.
- Evidence references.
- Applicable harnesses.
- Applicable roles.

Example deterministic recommendations:

- Reduce unstable prompt prefix when cache evidence shows repeated cache misses.
- Move stable instructions before variable content when cache evidence supports the pattern.
- Stop retrying a failed tool call and fix the underlying command or permission.
- Narrow context passed to the agent when input token burn is high and context evidence is correlated.
- Review content capture policy when recommendations are limited by hidden evidence.

### MVP Deterministic Rule List

| Rule ID | Trigger | Required evidence | Recommendation | Confidence |
| --- | --- | --- | --- | --- |
| `rec.high_input_tokens.narrow_context` | Session input tokens exceed tenant P90 or configured budget threshold | Observed or estimated input tokens, session scope, model, repository or team context | Reduce unnecessary context and prefer targeted files, summaries, or smaller task slices | Medium to high based on metric status |
| `rec.high_output_tokens.constrain_answer` | Output tokens exceed tenant P90 and user-facing answer or diff generation is large | Observed or estimated output tokens and response summary | Ask for concise diffs, narrower output, or staged changes | Medium |
| `rec.repeated_tool_failure.stop_retry_loop` | Same or equivalent tool command fails 3 or more times in one session | Tool activity summary, exit or error state, retry count | Stop retrying and fix the command, permission, path, dependency, or environment issue | High when failures are observed |
| `rec.repeated_failed_patch.reduce_scope` | Patch or edit attempts repeatedly fail for related files | Tool activity, file/path evidence, error summaries | Reduce edit scope, inspect target files, and apply smaller changes | Medium |
| `rec.cache_breakage.stabilize_prefix` | Cache evidence is observed or correlated and stable-prefix churn is detected | Cache evidence availability, prompt/context signature changes, token observations | Keep reusable instructions and context stable at the start of the request | Medium to high based on cache evidence |
| `rec.cache_unavailable.instrumentation_gap` | Cache evidence is unavailable for a session with high input tokens | High input tokens and unavailable cache fields | Treat cache cause as unknown and improve telemetry before making cache claims | High |
| `rec.policy_hidden.enable_evidence` | Recommendation quality is limited by policy-hidden evidence | Hidden evidence entries and policy version | Review Content Capture Policy for a bounded, redacted evidence class | Medium |
| `rec.model_cost.review_model_choice` | Estimated cost is high and model choice differs from team or repo baseline | Estimated cost, model, pricing basis, baseline comparison | Review whether the task needed the selected model or could use a lower-cost approved model | Medium |
| `rec.large_content_reference.review_context` | Redacted content references show repeated large tool outputs, file excerpts, or command summaries | Content reference metadata, content class, redacted length | Reduce generated or selected context and use targeted summaries | Medium |
| `rec.no_confirmed_hotspot.no_action` | No rule reaches confidence threshold | Evidence packet with insufficient support | Show no confirmed recommendation and list evidence gaps | High |

Deterministic rules are product-owned and versioned. A rule can be enabled, disabled, or threshold-adjusted by Customer Organization policy, but a disabled rule leaves an audit trail when it would otherwise have fired.

## LLM-Assisted Recommendations

LLM-Assisted Recommendations use Azure OpenAI by default in the Azure Production MVP when Recommendation Model Policy allows it.

The model may:

- Summarize evidence.
- Explain why a hotspot likely occurred.
- Draft Optimization Coaching.
- Rank candidate recommendations.
- Explain cache-breakage possibilities.
- Identify LLM-Inferred Candidate Hotspots from an evidence packet.

The model must not:

- Use raw blocked content.
- Invent evidence references.
- Claim certainty without evidence.
- Promote candidate hotspots to confirmed hotspots.
- Rank people.
- Generate blame language.
- Override Content Capture Policy.
- Override Coaching Visibility Scope.

### MVP LLM Deployment Contract

The MVP uses Azure OpenAI in Microsoft Foundry through an approved deployment alias, not a hardcoded model name in product logic.

| Deployment alias | Required | Purpose |
| --- | --- | --- |
| `recommendation-writer-primary` | Yes when LLM-assisted recommendations are enabled | Draft LLM-assisted recommendation explanations and candidate hotspots from evidence packets |
| `recommendation-writer-fallback` | Optional | Same contract as primary, used only when primary is unavailable and policy allows fallback |

Deployment requirements:

- The deployed model must support structured outputs or an equivalent JSON schema enforcement mode.
- The deployment must be in the Customer Data Residency Region or an explicitly allowed processing region.
- Product services access the deployment with managed identity where supported.
- Content filtering remains enabled.
- Recommendation Model Policy records provider, deployment alias, model family or SKU, model version where exposed, region, prompt-template version, and allowed evidence classes.
- If no compliant deployment is available, the product falls back to deterministic recommendations only.

Anthropic or direct OpenAI deployments are target-state provider options. They are not required for the Azure Production MVP.

### Prompt Template Versioning

Every LLM call uses a versioned prompt template:

```text
recommendation-drafter.v1
candidate-hotspot-generator.v1
cache-breakage-explainer.v1
```

Prompt template versions are immutable. A change to wording, allowed evidence, output schema, safety instructions, or refusal behavior creates a new version.

Prompt templates must include:

- Non-punitive optimization instruction.
- Explicit ban on blame language and people ranking.
- Evidence-only constraint.
- Instruction to return `unsupported_evidence_gaps` when evidence is missing.
- Instruction not to promote candidates to confirmed findings.
- Content policy constraints and hidden-evidence markers.
- Required JSON schema.

## Prompt And Output Contract

LLM calls must use a structured output contract where supported.

The output schema should include:

- Recommendation type.
- Candidate hotspot type, if applicable.
- Summary.
- Recommended action.
- Expected benefit.
- Evidence reference IDs used.
- Unsupported evidence gaps.
- Confidence.
- Safety flags.
- Policy limitations.
- User-facing wording.
- Reviewer-facing notes, if authorized.

Structured output constrains response shape, but it does not make the response true. Validation gates are still required.

### MVP Structured Output Schema

The MVP schema is:

```json
{
  "schemaVersion": "recommendation.llm_output.v1",
  "generationType": "recommendation_explanation",
  "recommendationType": "reduce_context",
  "candidateHotspot": {
    "proposed": false,
    "type": null,
    "label": null,
    "promotionEligible": false
  },
  "summary": "Large repeated tool output appears to be increasing input token use.",
  "recommendedAction": "Limit the next run to the failing file and provide a short error summary instead of full command output.",
  "expectedBenefit": "Lower input tokens and less repeated context across retries.",
  "evidenceReferenceIds": ["ev_1", "hot_1", "cr_1"],
  "unsupportedEvidenceGaps": ["cache evidence unavailable"],
  "confidence": "medium",
  "authorityState": "llm_assisted",
  "safetyFlags": [],
  "policyLimitations": ["prompt snippet hidden by policy"],
  "userFacingWording": "This looks like a workflow efficiency issue, not a person failure.",
  "reviewerNotes": "No raw content was used."
}
```

Allowed `authorityState` values:

- `deterministic`
- `llm_assisted`
- `llm_inferred_candidate`
- `rejected`

Allowed confidence values:

- `low`
- `medium`
- `high`

LLM output is rejected if it returns an evidence reference not present in the evidence packet, omits evidence for a factual claim, uses blame wording, or includes raw blocked content.

Reference:

- Azure OpenAI structured outputs: https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/structured-outputs

## Validation Gates

Before storing or showing an LLM output as a recommendation, the product validates:

- Schema is valid.
- Required evidence references exist.
- Evidence references are in the evidence packet.
- No raw blocked content appears in output.
- No unsupported factual claim is present.
- User-error wording is framed as Optimization Coaching.
- Confidence is present.
- Policy version is recorded.
- Model, provider, deployment, and prompt-template version are recorded.
- Content Safety or groundedness checks run where policy requires them.

Invalid outputs are rejected and stored only as failed generation metadata, not as recommendations.

### MVP Validation Rules

| Validation rule | Failure behavior |
| --- | --- |
| JSON schema validation fails | Reject generation |
| Evidence reference is absent from evidence packet | Reject generation |
| Output contains raw blocked content marker or unapproved content text | Reject generation and audit |
| Output claims user error without evidence and non-punitive wording | Reject generation |
| Output ranks developers or implies blame | Reject generation |
| Output promotes candidate hotspot to confirmed | Reject generation |
| Confidence missing or outside allowed values | Reject generation |
| Policy limitation omitted when evidence is hidden | Reject generation |
| Provider, deployment, model, or prompt-template metadata missing | Reject generation |
| Content Safety blocks prompt or completion | Reject generation and store failed generation metadata |
| Groundedness or evaluator check fails where required by policy | Mark `review_required` or reject according to policy |

Rejected output is never shown to normal dashboard users. Authorized operators may see failed generation metadata without raw prompt or completion content.

Azure AI services used for validation may include:

- Azure AI Content Safety groundedness detection where source material is available.
- Azure AI Foundry evaluations for offline and production quality monitoring.
- Custom deterministic validators for evidence references and banned claim patterns.

References:

- Azure AI Content Safety groundedness detection: https://learn.microsoft.com/en-us/azure/ai-services/content-safety/concepts/groundedness
- Azure AI Foundry evaluations: https://learn.microsoft.com/en-us/azure/foundry/how-to/evaluate-generative-ai-app
- Foundry observability and evaluators: https://learn.microsoft.com/en-us/azure/foundry/concepts/observability
- Responsible AI in Azure workloads: https://learn.microsoft.com/en-us/azure/well-architected/ai/responsible-ai

## Storage Model

The Product Metadata Store records recommendation state.

Recommendation records include:

- Recommendation ID.
- Customer Organization.
- Session ID.
- Hotspot ID or candidate hotspot ID.
- Recommendation type: deterministic, LLM-assisted, or mixed.
- Rule ID, if deterministic.
- Provider and model, if LLM-assisted.
- Prompt-template version, if LLM-assisted.
- Recommendation Model Policy version.
- Content Capture Policy version.
- Evidence packet version.
- Evidence references.
- Confidence.
- Generated time.
- Validation state.
- Reviewer state.
- Supersession state.

Historical recommendations are immutable. Regeneration creates a new version.

## Candidate Hotspot Review Workflow

LLM-Inferred Candidate Hotspots follow this workflow:

1. Generated from an immutable evidence packet.
2. Validated by schema, evidence, policy, and safety gates.
3. Stored as `candidate_pending_review`.
4. Displayed only to authorized roles as LLM-inferred.
5. Reviewed by PlatformAdmin, EngineeringLead for scoped areas, or another configured product validator role.
6. Promoted only if a deterministic validation rule, new direct evidence, or explicit product validation supports the hotspot.
7. Rejected when evidence is insufficient, stale, policy-hidden, or contradicted.

Promotion creates a new confirmed Token Hotspot record with attribution type `correlated` or `direct`. It must not keep `llm_inferred` as the confirmed attribution type.

Review outcomes:

- `candidate_pending_review`
- `candidate_promoted`
- `candidate_rejected`
- `candidate_expired`
- `candidate_superseded`

Candidate hotspots expire after 30 days unless promoted or refreshed from newer evidence.

## Evaluator Set

The MVP evaluator set is:

| Evaluator | Purpose | Mode |
| --- | --- | --- |
| Schema validator | Ensure structured output shape | Inline |
| Evidence reference validator | Ensure all references exist in packet | Inline |
| Banned claim validator | Block blame, ranking, unsupported certainty, and hidden evidence claims | Inline |
| Content policy validator | Ensure content states and policy limitations are respected | Inline |
| Groundedness evaluator | Check whether recommendation text is grounded in supplied evidence where policy requires | Offline and optional inline |
| Coherence evaluator | Monitor readability of recommendation wording | Offline |
| Relevance evaluator | Monitor whether advice addresses the hotspot | Offline |
| Safety evaluator | Monitor hate, sexual, violence, self-harm, protected material, and prompt-attack issues | Offline and inline where service blocks |

Offline evaluator failures create product quality work items or disable a prompt-template version when thresholds are breached. They do not silently rewrite historical recommendations.

## Session Investigation View Behavior

The Session Investigation View shows:

- Confirmed hotspots before candidate hotspots.
- Deterministic recommendations before LLM-assisted explanation text.
- Cache diagnostics with Cache Evidence Availability.
- Evidence gaps and policy-hidden evidence.
- Recommendation confidence and expected benefit.
- Regeneration option only for authorized users.

The UI must distinguish:

- `observed`
- `correlated`
- `inferred`
- `llm_inferred_candidate`
- `unavailable`

## Audit Events

Governance Audit Events are required for:

- Recommendation generation.
- Recommendation regeneration.
- Recommendation rejection.
- Candidate hotspot promotion or rejection.
- Recommendation Model Policy changes.
- Prompt-template changes.
- Model provider or deployment changes.
- Recommendation viewing when captured content was used.
- Reviewer edits or approvals.

## MVP Acceptance

- Recommendations are generated asynchronously after ingestion and hotspot detection.
- Deterministic Recommendations include rule ID, trigger condition, evidence refs, confidence, and expected benefit.
- MVP deterministic rule list is defined and versioned.
- Evidence packet schema is versioned and immutable per generation.
- Structured LLM output schema is defined.
- Validation gates are explicit and reject unsupported outputs.
- Prompt templates are versioned and immutable.
- LLM deployment aliases and fallback behavior are defined.
- Candidate hotspot review workflow is defined.
- Evaluator set is defined.
- LLM-Assisted Recommendations use only supplied evidence packets.
- LLM-Inferred Candidate Hotspots remain labelled as candidates.
- LLM outputs cannot become confirmed Token Hotspots without product validation.
- Prompt Cache Breakage uses observed, correlated, LLM-inferred, or unavailable evidence states.
- Unsupported cache explanations are shown as unavailable, not guessed.
- Every recommendation stores policy versions, evidence refs, generation metadata, and validation state.
- The Session Investigation View distinguishes confirmed findings from candidate findings.
- The product does not rank developers or use blame wording.

## Deferred Target-State Questions

- Exact cache evidence fields emitted by Codex CLI.
- Exact Azure OpenAI model SKU selected for each deployment alias after regional capacity and structured-output support are validated.
- Whether Anthropic or direct OpenAI should be enabled as target-state provider options after Azure Production MVP.

Resolved for MVP: deterministic rules, evidence packet schema, structured output schema, validation gates, prompt-template versioning, deployment aliases, fallback behavior, evaluator set, and candidate hotspot review workflow are defined in this document.
