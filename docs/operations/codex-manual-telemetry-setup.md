# Codex Manual Telemetry Setup

## Purpose

This runbook documents the first-release Codex CLI setup profile and the manual telemetry configuration path for developers.

The setup is intentionally explicit. Manual setup per developer and per harness remains the production rule for the first release. The product may generate profile values and validation guidance, but each developer configures their own Codex CLI telemetry export.

## Scope

In scope:

- Codex CLI as the first-release harness.
- OTLP/HTTP export to the Product Ingestion Endpoint.
- A developer-specific Scoped Ingestion Credential.
- A tenant-owned harness setup profile.
- Manual validation that telemetry reaches the ingestion boundary.

Out of scope:

- Automatic workstation discovery.
- Silent enrollment.
- Managed device rollout.
- Any other harness configuration path.
- Credential issuance UI behavior.
- Ingestion endpoint implementation behavior.

## Setup Profile Values

A PlatformAdmin prepares a harness setup profile for the developer. The profile is metadata, not a secret.

Required profile values:

| Value | Source | Secret | Purpose |
| --- | --- | --- | --- |
| Customer Organization slug or ID | Product metadata | No | Identifies the tenant that owns the setup profile |
| Product Ingestion Endpoint host | Product deployment output | No | Public HTTPS ingestion host, for example `https://ingest.tokenobs.consultwithcloud.com` |
| Environment label | Product deployment output | No | Environment such as `dv`, `qa`, `pp`, or `pd` |
| Region | Product deployment output | No | Product data residency or deployment region |
| Harness | Setup profile | No | `codex-cli` |
| Harness setup profile ID | Product metadata | No | Binds telemetry to the configured profile |
| Schema version | Product ingestion contract | No | Version expected by the ingestion endpoint |
| Content capture setting | Product policy | No | Indicates whether content candidates are disabled, metadata-only, or policy-enabled |
| Scoped Ingestion Credential | Credential issuance path | Yes | Bearer credential used to authenticate telemetry upload |

Do not commit the Scoped Ingestion Credential, shell exports, generated local Codex configuration, or copied bearer values to this repository.

## Developer Configuration Inputs

The developer needs these values before editing their local Codex CLI configuration:

```text
AITO_INGESTION_TOKEN=<secret value shown only through the approved credential issuance path>
AITO_INGESTION_HOST=https://ingest.tokenobs.consultwithcloud.com
AITO_ENVIRONMENT=pd
AITO_REGION=eastus2
AITO_HARNESS=codex-cli
AITO_SETUP_PROFILE_ID=profile_codex_cli_example
AITO_SCHEMA_VERSION=2026-06-01
```

Store `AITO_INGESTION_TOKEN` in the developer's local secret mechanism, such as a shell secret store, local password manager, or ephemeral terminal session. Do not write it into tracked files.

## Codex Configuration Location

Codex telemetry routing is machine-local configuration.

Use the developer's user-level Codex configuration, typically `~/.codex/config.toml`, for `[otel]` settings. Do not place product ingestion telemetry routing in a repository `.codex/config.toml` file. Current Codex configuration rules treat project-scoped config as untrusted for telemetry routing keys, and the product must not depend on repository-distributed telemetry enrollment.

## Representative Codex CLI Configuration

The product setup profile provides values to place into the developer's user-level Codex telemetry configuration.

Use environment-variable references for the credential:

```toml
[otel]
environment = "pd"
log_user_prompt = false

exporter = { otlp-http = {
  endpoint = "https://ingest.tokenobs.consultwithcloud.com/v1/logs",
  protocol = "binary",
  headers = {
    "authorization" = "Bearer ${AITO_INGESTION_TOKEN}",
    "x-aito-harness" = "codex-cli",
    "x-aito-setup-profile-id" = "profile_codex_cli_example",
    "x-aito-schema-version" = "2026-06-01"
  }
}}

trace_exporter = { otlp-http = {
  endpoint = "https://ingest.tokenobs.consultwithcloud.com/v1/traces",
  protocol = "binary",
  headers = {
    "authorization" = "Bearer ${AITO_INGESTION_TOKEN}",
    "x-aito-harness" = "codex-cli",
    "x-aito-setup-profile-id" = "profile_codex_cli_example",
    "x-aito-schema-version" = "2026-06-01"
  }
}}

metrics_exporter = { otlp-http = {
  endpoint = "https://ingest.tokenobs.consultwithcloud.com/v1/metrics",
  protocol = "binary",
  headers = {
    "authorization" = "Bearer ${AITO_INGESTION_TOKEN}",
    "x-aito-harness" = "codex-cli",
    "x-aito-setup-profile-id" = "profile_codex_cli_example",
    "x-aito-schema-version" = "2026-06-01"
  }
}}
```

Use the real setup profile values provided by the product. Do not paste a real bearer credential into this example or any public repository file.

## Manual Setup Steps

1. PlatformAdmin creates or selects the Customer Organization, Codex CLI harness setup profile, environment label, region, schema version, and content capture policy.
2. PlatformAdmin issues a developer-scoped Scoped Ingestion Credential for the Codex CLI harness setup profile.
3. Developer stores the credential locally as `AITO_INGESTION_TOKEN` outside repository files.
4. Developer adds the setup profile values to their local Codex CLI telemetry configuration.
5. Developer runs a small Codex CLI session or product-approved test event.
6. PlatformAdmin or Developer checks the Product Dashboard or ingestion validation surface for the setup profile and credential status.
7. If telemetry is rejected, rotate or fix the credential, endpoint, tenant, setup profile ID, or schema version before retrying.

## Safe Failure Behavior

The Product Ingestion Endpoint fails closed when setup values do not validate.

Expected safe failures:

| Failure | Product behavior | Developer action |
| --- | --- | --- |
| Missing credential | Reject before parsing telemetry content | Set `AITO_INGESTION_TOKEN` from the approved credential path |
| Unknown credential | Reject and create tenant-safe rejection metadata when derivable | Request a new credential |
| Expired, disabled, or revoked credential | Reject and preserve auditable rejection metadata | Rotate or reissue the credential |
| Credential for a different Customer Organization | Reject without changing tenant ownership from headers or payload fields | Use the profile and credential issued for the correct tenant |
| Wrong harness setup profile ID | Reject or mark out of scope | Use the profile ID from the active setup profile |
| Wrong harness value | Reject or mark unsupported | Use `codex-cli` for the first release |
| Wrong endpoint path | Reject at the edge or ingestion route | Use `/v1/logs`, `/v1/traces`, and `/v1/metrics` over HTTPS |
| Unsupported schema version | Reject with auditable metadata | Update to the schema version in the active setup profile |
| Content capture not allowed | Accept metadata only or reject content-bearing fields according to policy | Keep `log_user_prompt = false` unless policy explicitly enables content capture |

Tenant headers, harness-emitted identity, and payload fields are evidence only. They must not override credential-derived tenant, developer, harness, or setup profile identity.

## Secret Handling Rules

- Do not commit credentials, local shell exports, copied bearer tokens, or personal Codex configuration into repository files.
- Do not paste real credentials into issues, pull requests, docs, examples, or screenshots.
- Do not send credentials through chat, ticket comments, or public logs.
- Use local environment variables or a local secret manager for `AITO_INGESTION_TOKEN`.
- Rotate the Scoped Ingestion Credential if it is exposed, lost, copied to the wrong place, or no longer needed.
- Treat setup profile IDs as non-secret but authorization-sensitive metadata.

## Validation Checklist

- The Product Ingestion Endpoint uses HTTPS.
- The `[otel]` settings are in user-level Codex configuration, not a repository config file.
- The profile uses `codex-cli` as the harness.
- The environment label and region match the active setup profile.
- The schema version matches the Codex Production Ingestion Contract.
- The credential belongs to the developer and setup profile.
- The credential is active and not expired, disabled, or revoked.
- The local configuration references `AITO_INGESTION_TOKEN` instead of embedding the secret value.
- Content capture is disabled unless the Customer Organization policy explicitly enables it.
- The first validation event is visible only to authorized product users.

## References

- Codex Advanced Configuration: https://developers.openai.com/codex/config-advanced
- Codex Configuration Reference: https://developers.openai.com/codex/config-reference
- Codex Agent Approvals And Security: https://developers.openai.com/codex/agent-approvals-security
