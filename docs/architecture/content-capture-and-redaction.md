# Content Capture And Redaction Architecture

## Purpose

This document defines how the product captures, redacts, stores, reviews, and uses prompt, tool, model, command, and content evidence.

Content capture is required for credible session investigation and recommendations, but it is the highest-risk data path in the product. The architecture therefore treats captured content as policy-gated, redacted before storage, role-protected, short-retained, and auditable.

## Source Documents

- [Production Target State Spec](../specs/production-target-state.md)
- [Azure Production MVP PRD](../prd/azure-production-mvp.md)
- [Azure Production Architecture](./azure-production-architecture.md)

## Core Decisions

- Content Capture Mode is supported in the Azure Production MVP for Codex only.
- Content Capture Mode is disabled by default.
- Content capture is controlled by Customer Organization Content Capture Policy.
- Captured content must pass the Pre-Storage Content Redaction Pipeline before Blob Storage.
- Store-then-redact is not allowed.
- If redaction confidence is insufficient, the Redaction Failure Gate blocks content storage.
- Redaction failure stores metadata only and marks the item as `redaction_failed` or `review_required`.
- Privileged reviewers may retry, discard, or approve a bounded excerpt.
- Captured content is stored in Azure Blob Storage only after policy and redaction allow it.
- PostgreSQL stores metadata, references, hashes, classifications, retention class, policy version, and audit context.
- The product uses Platform-Managed Encryption. Customer Managed Keys are not offered.

## Content Classes

The Content Capture Policy must distinguish content classes because each class has different risk and usefulness.

| Content class | Examples | Default MVP state |
| --- | --- | --- |
| Prompt snippet | User prompt excerpt, system or developer instruction excerpt when emitted | Disabled |
| Tool input excerpt | Tool name, selected arguments, target file path, command summary | Disabled |
| Tool output excerpt | Error summary, result summary, bounded output excerpt | Disabled |
| Model response excerpt | Response section used to explain token burn or cache behavior | Disabled |
| Command summary | Command name, exit code, bounded stderr summary | Disabled |
| File content excerpt | Small policy-approved file snippet | Disabled |
| Metadata only | Token counts, tool name, model, timing, status, hashes, IDs | Enabled where emitted |

Metadata-only telemetry remains separate from Content Capture Mode.

## Policy Model

Content Capture Policy is scoped at Customer Organization level and may be narrowed by:

- Harness.
- Developer or role.
- Team.
- Repository.
- Branch or environment.
- Content class.
- Retention class.
- Recommendation use.
- Security review requirement.

Policy defaults:

- Capture disabled.
- Raw full prompts disabled.
- Raw full tool outputs disabled.
- Raw file content disabled.
- LLM use of captured content disabled unless explicitly enabled.
- Redaction failure blocks storage.
- Short retention for captured content.
- Governance Audit Event required for policy changes.

## Pipeline Overview

```text
Codex telemetry event
  |
  v
Product Ingestion Endpoint
  |
  +--> metadata normalization
  |
  +--> content candidate extraction
          |
          v
      policy pre-check
          |
          +--> capture not allowed -> store metadata only
          |
          v
      deterministic secret scanning
          |
          v
      Azure AI Language PII detection
          |
          v
      Azure AI Content Safety checks
          |
          v
      product-specific redaction rules
          |
          v
      confidence and decision gate
          |
          +--> pass -> write Captured Content Blob and metadata reference
          |
          +--> fail -> metadata only, redaction_failed or review_required
```

## MVP Execution Tradeoff

For the Azure Production MVP, redaction runs inline in the ingestion process for eligible content candidates. This optimizes for delivery speed and preserves the no store-then-redact rule because raw content does not need to wait in a durable queue before redaction.

The tradeoff is that ingestion latency may increase for content-bearing events, and large or slow-to-process candidates may need to be rejected or routed to review rather than captured immediately.

MVP limits:

- Inline redaction accepts a maximum candidate size of 16 KiB UTF-8 text.
- Inline redaction accepts a maximum total content size of 64 KiB per telemetry envelope.
- Inline redaction has a 2 second local processing cap before Azure service calls.
- Inline redaction has a 6 second total processing cap including Azure service calls.
- Redacted Captured Content Blob payloads are capped at 16 KiB.
- Manually approved excerpts are capped at 2 KiB.
- Candidates that exceed size or time caps are stored as metadata only with `review_required`.
- Retry, review, bulk reprocessing, and later heavier redaction can use Azure Container Apps Jobs.

This decision can be revisited after MVP telemetry shows real content size, latency, failure, and review-volume behavior.

## MVP Redaction Execution Details

### Recognizer Implementation Choice

The MVP implements deterministic recognizers inside the .NET Product Ingestion Endpoint and shared jobs image. Microsoft Presidio is not a first-release runtime dependency.

Reason: the MVP needs a small, auditable, versioned recognizer set that can run inline without adding another runtime stack. Presidio-compatible recognizer concepts can be adopted later if the recognizer library becomes large enough to justify it.

### Deterministic Secret Recognizers

The MVP deterministic recognizer set is:

| Recognizer | MVP action |
| --- | --- |
| PEM or OpenSSH private key block | Redact, then `redaction_failed` if any key body remains |
| JWT-like token | Redact |
| GitHub token prefixes such as `ghp_`, `gho_`, `ghu_`, `ghs_`, `ghr_`, `github_pat_` | Redact |
| Azure SAS query string or signed URL | Redact |
| Azure Storage account key pattern | Redact |
| Azure connection strings | Redact |
| SQL, PostgreSQL, MySQL, Redis, Service Bus, Event Hubs, Cosmos DB, and MongoDB connection strings | Redact |
| AWS access key ID and secret access key patterns | Redact |
| Anthropic, OpenAI, Azure OpenAI, and GitHub API key patterns where recognizable | Redact |
| OAuth bearer token | Redact |
| Basic auth credential in URL | Redact |
| Slack, Teams, Discord, and generic webhook URLs | Redact |
| Password assignment patterns such as `password=`, `pwd=`, `client_secret=`, `secret=`, `token=`, `api_key=` | Redact |
| High-entropy unstructured token of 32 or more characters near credential context | Redact; `review_required` when context is ambiguous |
| Customer-defined regex recognizers | Redact or block according to Customer Organization policy |

If a high-risk recognizer match cannot be safely replaced while preserving enough context for a useful excerpt, the content is not stored as a Captured Content Blob.

### Azure AI Language PII Categories

The MVP uses Azure AI Language Text PII detection synchronously for eligible text candidates. It redacts returned entities and stores entity categories, confidence scores, and redaction policy version as metadata.

The MVP requests these stable categories where available:

- `Person`
- `Email`
- `PhoneNumber`
- `Address`
- `IPAddress`
- `URL`
- `Age`
- `Date`
- `Organization`
- `CreditCardNumber`
- `InternationalBankingAccountNumber`
- `SWIFTCode`
- `ABARoutingNumber`
- `USSocialSecurityNumber`
- `USDriversLicenseNumber`
- `USIndividualTaxpayerIdentification`
- `USBankAccountNumber`
- `UKNationalInsuranceNumber`
- `UKNationalHealthNumber`
- `UKDriversLicenseNumber`
- `UKUniqueTaxpayerNumber`
- `AzureDocumentDBAuthKey`
- `AzureIAASDatabaseConnectionAndSQLString`
- `AzureIoTConnectionString`
- `AzurePublishSettingPassword`
- `AzureRedisCacheString`
- `AzureSAS`
- `AzureServiceBusString`
- `AzureStorageAccountGeneric`
- `AzureStorageAccountKey`
- `SQLServerConnectionString`

Preview-only PII categories are not required for MVP acceptance. They may be enabled only after explicit policy review because preview behavior can change.

### Azure AI Content Safety Checks

The MVP uses Azure AI Content Safety for classification, not as the redaction engine.

Required MVP checks:

- Prompt Shields for direct prompt attacks where prompt-like content is captured.
- Prompt Shields for indirect attacks where content came from a document, tool output, repository excerpt, or external text.
- Harmful text categories: hate, sexual, violence, and self-harm.
- Protected material text detection when model response excerpts or generated text excerpts are captured.
- Protected material code detection when code-like model output excerpts are captured.

MVP behavior:

- Prompt attack detected: `review_required`.
- Indirect attack detected: `review_required`.
- Harm category severity `medium` or `high`: `review_required`.
- Harm category severity `low`: store only if all other redaction gates pass and policy allows the content class.
- Protected material detected: `review_required`.

### Confidence Thresholds

The Redaction Failure Gate uses these MVP thresholds:

| Signal | Threshold | Decision |
| --- | --- | --- |
| Deterministic high-risk secret found and fully redacted | N/A | Continue |
| Deterministic high-risk secret found but not fully redacted | N/A | `redaction_failed` |
| Ambiguous high-entropy credential-like value | N/A | `review_required` |
| Azure AI Language PII entity confidence `>= 0.80` and entity redacted | 0.80 | Continue |
| Azure AI Language PII entity confidence `>= 0.50` and `< 0.80` | 0.50 | `review_required` unless deterministic rule already redacted it |
| Azure AI Language PII service unavailable for eligible content | N/A | `review_required` |
| Content Safety prompt attack, indirect attack, protected material, or medium/high harm | N/A | `review_required` |
| Any pipeline stage times out | N/A | `review_required` |
| Redacted output still matches a high-risk recognizer | N/A | `redaction_failed` |

The product stores the decision reason, recognizer version, Azure model or API version where available, and policy version with the content reference metadata.

## Pipeline Stages

### 1. Candidate Extraction

The ingestion service identifies content candidates from Codex Agent Telemetry Signals only when the harness emits them and the setup profile allows them.

The candidate record should include:

- Customer Organization.
- Harness setup profile.
- Scoped Ingestion Credential.
- Developer identity.
- Session.
- Telemetry record reference.
- Content class.
- Original length.
- Hash of original content when allowed for duplicate detection.
- Policy version.
- Redaction status.

Original content must not be written to persistent storage before the redaction gate.

### 2. Policy Pre-Check

The product evaluates Content Capture Policy before redaction work.

If policy denies the content class, the product stores metadata only and records why content was not captured.

Policy denial is not an error. It is an expected evidence state shown to authorized users as policy-hidden evidence.

### 3. Deterministic Secret Scanning

The first redaction stage is deterministic secret scanning because PII services are not enough for credentials.

The scanner should detect and redact patterns such as:

- API keys.
- Personal access tokens.
- OAuth tokens.
- JWTs.
- Connection strings.
- SSH private keys.
- PEM private keys.
- Cloud access keys.
- Webhook signing secrets.
- Database credentials.
- Internal service URLs where policy requires masking.
- Customer-defined secret patterns.

This stage should use deterministic rules, entropy checks, provider-specific recognizers, and customer-specific patterns. Microsoft Presidio custom recognizers may be used as a framework for extensible recognizers.

### 4. Azure AI Language PII Detection

Azure AI Language PII detection is used for personal and health-related identifiers where applicable.

It should be used to identify, classify, and redact personal data in text, including categories such as names, email addresses, phone numbers, addresses, government identifiers, and financial identifiers where the service supports them.

Azure AI Language PII detection is not the sole redaction mechanism and must not be treated as a general secret scanner.

Reference:

- Azure AI Language PII overview: https://learn.microsoft.com/en-us/azure/ai-services/language-service/personally-identifiable-information/overview
- Redact text PII: https://learn.microsoft.com/en-us/azure/ai-services/language-service/personally-identifiable-information/how-to/redact-text-pii
- PII entity categories: https://learn.microsoft.com/en-us/azure/ai-services/language-service/personally-identifiable-information/concepts/entity-categories

### 5. Azure AI Content Safety Checks

Azure AI Content Safety is used for safety and policy classification, not as a full redaction engine.

Checks may include:

- Prompt Shields for prompt attack detection.
- Protected material detection where applicable.
- Harmful content categories where applicable.
- Groundedness-related checks when recommendation evidence needs validation.

The MVP must not rely on Content Safety as the only mechanism for deciding whether content is safe to store.

Reference:

- Azure AI Content Safety overview: https://learn.microsoft.com/en-us/azure/ai-services/content-safety/overview
- Prompt Shields: https://learn.microsoft.com/en-us/azure/ai-services/content-safety/concepts/jailbreak-detection
- Content Safety what's new, including GA status for Prompt Shields and Protected Material APIs: https://learn.microsoft.com/en-us/azure/ai-services/content-safety/whats-new

### 6. Product-Specific Redaction

Product-specific rules handle data that generic services cannot reliably classify.

Examples:

- Repository paths.
- Customer names.
- Internal package feeds.
- Internal hostnames.
- Build output.
- Stack traces.
- File paths.
- Code identifiers where policy requires masking.
- Tool names or arguments that reveal sensitive infrastructure.
- Repeated content that could reconstruct a file.

These rules are versioned so later recommendation and audit records can explain which redaction rules were applied.

### 7. Confidence And Decision Gate

The Redaction Failure Gate makes the storage decision.

Possible outcomes:

- `captured`: redacted content is stored as a Captured Content Blob.
- `metadata_only`: policy or content class prevents capture.
- `review_required`: redaction may be possible but requires privileged review.
- `redaction_failed`: content could not be redacted confidently.
- `discarded`: content was intentionally discarded.

The gate must block storage for `review_required` and `redaction_failed`.

## Storage Model

### PostgreSQL Metadata

The Product Metadata Store records:

- Content reference ID.
- Customer Organization.
- Session ID.
- Telemetry record ID.
- Content class.
- Capture policy version.
- Redaction pipeline version.
- Redaction status.
- Original content length.
- Redacted content length when stored.
- Original hash where allowed.
- Redacted hash.
- Blob URI or storage key when stored.
- Retention class.
- Reviewer state.
- Recommendation eligibility.
- Audit references.

### Blob Storage

Azure Blob Storage stores Captured Content Blobs only after redaction success.

MVP container layout:

| Container | Purpose | Public access |
| --- | --- | --- |
| `captured-content` | Redacted Captured Content Blobs and approved excerpts | Disabled |
| `content-review-artifacts` | Reviewer-produced bounded excerpts and retry artifacts that passed policy gates | Disabled |

MVP blob prefix layout:

```text
captured-content/
  customer-organization-id={customerOrganizationId}/
    yyyy={yyyy}/mm={mm}/dd={dd}/
      session-id={sessionId}/
        content-reference-id={contentReferenceId}/
          redacted.txt
          redaction-metadata.json

content-review-artifacts/
  customer-organization-id={customerOrganizationId}/
    yyyy={yyyy}/mm={mm}/dd={dd}/
      content-reference-id={contentReferenceId}/
        approved-excerpt.txt
        reviewer-decision.json
```

The date prefix is based on content capture decision time in UTC. Customer Organization and session identifiers are product IDs, not names.

Blob metadata or index tags should support lifecycle and discovery where appropriate:

- Customer Organization.
- Session.
- Content class.
- Retention class.
- Redaction status.
- Policy version.
- Redaction pipeline version.
- Content capture decision time.
- Recommendation eligibility.

Blob lifecycle management enforces captured content retention.

MVP retention defaults:

| Data class | Default retention | Notes |
| --- | --- | --- |
| Captured Content Blob | 30 days | Short retention by default because content is high risk |
| Approved bounded excerpt | 30 days | Same default as captured content |
| Redaction metadata in PostgreSQL | 180 days | Keeps evidence state after content deletion |
| Review decision audit event | 1 year | Governance evidence, no raw content |
| Discarded or redaction-failed metadata | 180 days | Metadata only |
| Recommendation evidence reference | 180 days | Reference remains, content may expire earlier |
| Aggregate metrics | 13 months | No captured content |

Customer Organization policy may shorten captured content retention. Extending captured content retention beyond 30 days requires PlatformAdmin approval and Governance Audit Event recording.

Storage access requirements:

- Private endpoints where feasible.
- Public network access disabled where feasible.
- Managed identity from product services.
- Least-privilege data-plane access.
- Platform-managed encryption.

References:

- Azure Storage encryption: https://learn.microsoft.com/en-us/azure/storage/common/storage-service-encryption
- Blob lifecycle management: https://learn.microsoft.com/en-us/azure/storage/blobs/lifecycle-management-overview
- Storage private endpoints: https://learn.microsoft.com/en-us/azure/storage/common/storage-private-endpoints
- Azure Blob Storage well-architected guidance: https://learn.microsoft.com/en-us/azure/well-architected/service-guides/azure-blob-storage

## Reviewer Workflow

SecurityReviewer or another explicitly authorized role can act on content items with `review_required` or `redaction_failed`.

Allowed actions:

- Retry redaction with updated policy or recognizer versions.
- Discard the content candidate.
- Approve a bounded manually redacted excerpt.
- Mark the item as ineligible for recommendation use.

Reviewer actions create Governance Audit Events.

PlatformAdmin can configure policy but should not automatically receive sensitive content review access unless also assigned reviewer scope.

## Recommendation Use

Captured content can be used for recommendations only when:

- Content Capture Policy allows the content class.
- Redaction succeeded.
- Recommendation Model Policy allows the evidence class.
- The user or job has authorization for that evidence.
- The recommendation record stores evidence references, model or rule source, confidence, prompt-template version, and policy version.

If content is hidden by policy, unavailable, or blocked by redaction failure, the recommendation must state that the evidence is unavailable rather than infer unsupported facts.

LLM-assisted recommendations may use redacted excerpts. They must not use raw blocked content.

## Session Investigation View Behavior

The Session Investigation View should display content evidence according to role and policy:

- Developers see their own allowed redacted evidence.
- EngineeringLeads see evidence for assigned teams or repositories only when policy allows.
- SecurityReviewers see sensitive review workflows where explicitly scoped.
- ReadOnlyViewers do not see captured content.

When evidence is hidden, the UI should show a clear evidence state:

- `metadata_only`
- `policy_hidden`
- `review_required`
- `redaction_failed`
- `unavailable`

The UI must not imply that missing content means no issue existed.

## Audit Events

Governance Audit Events are required for:

- Content Capture Policy changes.
- Recommendation Model Policy changes affecting captured content.
- Redaction pipeline version changes.
- Redaction failure decisions.
- Reviewer access to captured content.
- Retry, discard, and manual excerpt approval actions.
- Captured content deletion.
- Captured content export.
- Recommendation generation using captured content.

## MVP Acceptance

- Content Capture Mode is disabled by default.
- Codex setup profile clearly indicates whether content capture is enabled.
- The ingestion path refuses to persist captured content before the redaction gate.
- The deterministic secret scanner runs before Azure AI Language PII detection.
- Azure AI Language PII detection is used for personal-data redaction when enabled.
- Azure AI Content Safety checks are available for safety and prompt-attack classification where policy requires them.
- Redaction failure stores metadata only.
- Captured content is stored in Azure Blob Storage only after redaction success.
- PostgreSQL stores content references and redaction metadata.
- Session Investigation View distinguishes captured, hidden, unavailable, failed, and review-required evidence.
- LLM-assisted recommendations do not use blocked raw content.
- Every content capture, review, storage, deletion, and recommendation-use decision is auditable.
- MVP inline content candidate size, time, blob, excerpt, recognizer, threshold, storage prefix, and retention defaults are explicitly defined.

## Deferred Target-State Questions

- Whether preview-only Azure AI Language PII categories should be enabled after production validation.
- Whether Microsoft Presidio should be adopted later if the custom recognizer set grows beyond MVP scope.
- Whether dedicated redaction worker images are needed after MVP telemetry shows real review and latency volume.

Resolved for MVP: content redaction runs inline in the ingestion process for delivery speed, with strict size and time caps. Retry, review, bulk reprocessing, and later heavier redaction can use Azure Container Apps Jobs.

Resolved for MVP: secret recognizer list, PII category set, Content Safety behavior, confidence thresholds, Blob Storage layout, and retention defaults are defined in this document.
