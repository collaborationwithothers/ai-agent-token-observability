# Certificate Renewal

Status: Deferred. This runbook is not part of the first release after the decision to use Azure Front Door managed certificates for explicit product hostnames. It applies only if the product later adopts a Bring Your Own Certificate wildcard certificate model.

## Purpose

This document defines a deferred BYOC certificate renewal workflow for public product hostnames under `tokenobs.consultwithcloud.com`.

If the deferred BYOC option is adopted, the workflow renews the Let's Encrypt wildcard certificate, proves control through Azure DNS DNS-01 validation, imports the renewed certificate into retained per-environment Key Vault, and keeps Azure Front Door custom domains using the current certificate version.

## Decision

If BYOC is adopted later, certificate renewal runs from GitHub Actions.

The product will not introduce an Azure Container Apps Job only for certificate renewal.

The renewal workflow is an infrastructure and operations workflow, not an application runtime workload. It belongs with other guarded GitHub infrastructure workflows and must follow the public-repository workflow guardrails.

First-release rule:

- Do not create this workflow for the first release.
- Use Azure Front Door managed certificates for `app.tokenobs.consultwithcloud.com`, `api.tokenobs.consultwithcloud.com`, and `ingest.tokenobs.consultwithcloud.com`.
- Reopen the certificate decision before implementing this runbook.

## Certificate Scope

Certificate:

```text
*.tokenobs.consultwithcloud.com
```

Certificate authority:

```text
Let's Encrypt
```

Validation method:

```text
DNS-01
```

Authoritative DNS zone:

```text
tokenobs.consultwithcloud.com
```

Certificate store:

```text
Retained per-environment Azure Key Vault
```

Consumer:

```text
Azure Front Door Bring Your Own Certificate
```

## Workflow Responsibilities

The GitHub Actions renewal workflow must:

- Authenticate to Azure using OIDC.
- Use a least-privilege renewal identity.
- Create the required DNS-01 TXT records in Azure DNS.
- Wait for DNS propagation or explicitly verify TXT visibility before finalizing ACME validation.
- Remove temporary ACME TXT records after validation where the ACME client supports cleanup.
- Import the renewed certificate with private key into retained per-environment Key Vault.
- Preserve enough certificate version history for rollback.
- Ensure Azure Front Door can use the current Key Vault certificate version.
- Emit an audit trail with workflow run ID, actor, certificate name, Key Vault certificate version, and result.
- Avoid writing private key material, PFX passwords, PEM contents, or ACME account material to logs.

## Runtime Shape

Workflow file:

```text
.github/workflows/certificate-renewal.yml
```

Recommended operation modes:

- `workflow_dispatch` for manual renewal and emergency renewal.

Required `workflow_dispatch` inputs:

| Input | Values | Purpose |
| --- | --- | --- |
| `operation` | `issue_import`, `verify_edge` | `issue_import` renews and imports the certificate; `verify_edge` checks Front Door edge serving |
| `environment` | `dv`, `qa`, `pp`, `pd` | Selects guarded GitHub environment and Azure target configuration |
| `use_letsencrypt_staging` | `true`, `false` | Uses Let's Encrypt staging for non-production proof runs |

Rules:

- Certificate renewal must not use `schedule`.
- Certificate renewal must not use `push`, `pull_request`, `pull_request_target`, or `workflow_run`.
- Manual renewal cadence is an operations responsibility.

## Authentication And Permissions

GitHub permissions:

```yaml
permissions:
  contents: read
  id-token: write
```

Azure identity requirements:

- Federated credential scoped to this repository and workflow.
- Least-privilege access to write TXT records in the Azure DNS zone `tokenobs.consultwithcloud.com`.
- Least-privilege access to import or update certificates in the retained per-environment certificate Key Vault.
- No Contributor role at subscription scope.
- No Cloudflare credentials.

## Public Repository Guardrails

The workflow must validate before Azure login:

- Expected repository.
- Expected workflow file.
- Expected branch or protected environment rule.
- Expected actor for manual renewal.
- Expected event name.
- No pull request or fork execution path.
- OIDC token is available only in the renewal job.

## ACME Client Decision

The renewal workflow uses `lego` as the ACME client.

Selected provider:

```text
azuredns
```

Rationale:

- `lego` has an Azure DNS provider for DNS-01.
- `lego` is a single binary, which keeps GitHub Actions setup simpler than installing Certbot and a third-party plugin.
- `lego` can use Azure CLI shared credentials, which should align with `azure/login` using OIDC.
- `lego` can constrain Azure DNS discovery by subscription, resource group, and zone name.

Implementation requirement:

- The first workflow implementation must prove that `lego` can authenticate through Azure CLI shared credentials after `azure/login` OIDC in GitHub Actions.
- If that path fails, do not fall back to long-lived service principal secrets. Re-open this decision and choose a secretless or short-lived-token approach.

Required `lego` Azure DNS settings:

- `AZURE_SUBSCRIPTION_ID`
- `AZURE_RESOURCE_GROUP`
- `AZURE_ZONE_NAME=tokenobs.consultwithcloud.com`
- `AZURE_AUTH_METHOD=cli`
- `AZURE_TTL`
- `AZURE_POLLING_INTERVAL`
- `AZURE_PROPAGATION_TIMEOUT`

Required workflow variables:

| Variable | Value |
| --- | --- |
| `LEGO_PATH` | `$RUNNER_TEMP/lego` |
| `LEGO_CERTIFICATE_DOMAIN` | `*.tokenobs.consultwithcloud.com` |
| `LEGO_CERTIFICATE_BASENAME` | `_.tokenobs.consultwithcloud.com` |
| `KEY_VAULT_CERTIFICATE_NAME` | `tokenobs-wildcard` |
| `ACME_ACCOUNT_SECRET_NAME` | `letsencrypt-lego-account-archive` |
| `FRONTDOOR_CERT_VERIFY_TIMEOUT_SECONDS` | `259200` for production, equal to 72 hours |
| `FRONTDOOR_CERT_VERIFY_INTERVAL_SECONDS` | `3600` for production |

`lego` command contract:

```bash
export AZURE_AUTH_METHOD="cli"
export AZURE_SUBSCRIPTION_ID="$AZURE_SUBSCRIPTION_ID"
export AZURE_RESOURCE_GROUP="$AZURE_DNS_RESOURCE_GROUP"
export AZURE_ZONE_NAME="tokenobs.consultwithcloud.com"
export AZURE_TTL="${AZURE_TTL:-60}"
export AZURE_POLLING_INTERVAL="${AZURE_POLLING_INTERVAL:-30}"
export AZURE_PROPAGATION_TIMEOUT="${AZURE_PROPAGATION_TIMEOUT:-600}"

lego run \
  --accept-tos \
  --email "$LETSENCRYPT_ACCOUNT_EMAIL" \
  --path "$LEGO_PATH" \
  --server "$ACME_DIRECTORY_URL" \
  --dns azuredns \
  -d "$LEGO_CERTIFICATE_DOMAIN"
```

Rules:

- The implementation must pin a `lego` major version before adding the workflow.
- A future BYOC implementation should use current `lego` command syntax where `lego run` handles obtain or renew behavior.
- `ACME_DIRECTORY_URL` must point to Let's Encrypt staging when `use_letsencrypt_staging = true`.
- `ACME_DIRECTORY_URL` must point to Let's Encrypt production only for approved production renewal.
- The workflow must use Azure CLI shared credentials produced by `azure/login` with OIDC.
- If `AZURE_AUTH_METHOD=cli` fails in GitHub Actions after `azure/login`, do not add long-lived service principal secrets. Re-open this decision.

ACME account material:

- ACME account material is stored in retained per-environment Key Vault as a secret named `letsencrypt-lego-account-archive`.
- The secret contains a base64-encoded archive of the `lego` account directory only.
- The workflow restores the archive into `$LEGO_PATH` before running `lego`.
- After successful ACME issuance, the workflow updates the Key Vault secret with the new account archive.
- The ACME account archive must not be stored in GitHub secrets, workflow artifacts, logs, or the repository.
- If the account secret is missing in a non-production proof run, the workflow may create it.
- If the account secret is missing in production, the workflow must fail before issuing a new production certificate unless an explicit break-glass input is added later.

Rejected option:

- Certbot with `certbot-dns-azure`.

Reason rejected:

- The Azure plugin is not part of Certbot core and its docs center on service principal secrets or VM managed identity. That is a poorer fit for this repo's GitHub OIDC requirement.

The ACME client must support DNS-01 automation against Azure DNS without Cloudflare API credentials.

## Key Vault Import

The workflow imports the wildcard certificate into the retained per-environment Key Vault as the product TLS certificate.

Terraform ownership boundary:

- Terraform may own the retained Key Vault, RBAC assignments, and non-secret resource identifiers.
- Terraform must not import or model the wildcard certificate contents.
- Certificate private key material, PEM, PFX, ACME account state, and imported certificate contents must not be passed through Terraform variables, Terraform resources, Terraform outputs, plan artifacts, or state.
- The renewal workflow owns certificate import and rotation if the deferred BYOC option is adopted later.

Rules:

- Certificate import must include the private key.
- Primary import path: import a PEM bundle directly into Key Vault when the workflow can produce a PEM that includes the certificate chain and private key in a format accepted by Key Vault.
- Fallback import path: convert the `lego` certificate output and private key to PFX in ephemeral workflow storage only if the PEM path is incompatible.
- The first implementation issue must prove the selected import path with a non-production Key Vault dry run before production certificate use.
- If PFX conversion is required, the workflow must generate a per-run password, mask it, avoid logging it, and discard it after import.
- Imported certificate versions must not be immediately deleted.
- Private key material must be held only in ephemeral workflow storage and must be masked where possible.
- Temporary certificate, private key, chain, and PFX files must be deleted before the workflow job ends.
- The retained per-environment Key Vault must be a Retained Shared Resource.
- The deletion workflow must not delete this Key Vault or certificate versions.

PEM import command contract:

```bash
LEGO_CERT_PATH="$LEGO_PATH/certificates/$LEGO_CERTIFICATE_BASENAME.crt"
LEGO_KEY_PATH="$LEGO_PATH/certificates/$LEGO_CERTIFICATE_BASENAME.key"
KEY_VAULT_IMPORT_PEM="$RUNNER_TEMP/tokenobs-wildcard.pem"

cat "$LEGO_CERT_PATH" "$LEGO_KEY_PATH" > "$KEY_VAULT_IMPORT_PEM"

az keyvault certificate import \
  --vault-name "$KEY_VAULT_NAME" \
  --name "$KEY_VAULT_CERTIFICATE_NAME" \
  --file "$KEY_VAULT_IMPORT_PEM"
```

PFX fallback command contract:

```bash
PFX_PASSWORD="$(openssl rand -base64 32)"
echo "::add-mask::$PFX_PASSWORD"
KEY_VAULT_IMPORT_PFX="$RUNNER_TEMP/tokenobs-wildcard.pfx"

openssl pkcs12 -export \
  -in "$LEGO_CERT_PATH" \
  -inkey "$LEGO_KEY_PATH" \
  -out "$KEY_VAULT_IMPORT_PFX" \
  -passout "pass:$PFX_PASSWORD"

az keyvault certificate import \
  --vault-name "$KEY_VAULT_NAME" \
  --name "$KEY_VAULT_CERTIFICATE_NAME" \
  --file "$KEY_VAULT_IMPORT_PFX" \
  --password "$PFX_PASSWORD"
```

Non-production proof:

- The first implementation must run `operation=issue_import` with `use_letsencrypt_staging=true` against a non-production Key Vault.
- The proof must import a staging certificate into `KEY_VAULT_CERTIFICATE_NAME`.
- The proof must show the Key Vault certificate version ID and certificate thumbprint in the sanitized job summary.
- The proof must not publish PEM, PFX, private key, PFX password, or ACME account material.
- Production renewal is blocked until the non-production import proof has passed.

## Azure Front Door Rotation

Azure Front Door custom domains consume the Key Vault certificate.

Decision:

- The first implementation should configure Azure Front Door to reference the latest Key Vault certificate version where Terraform and Azure Front Door support that behavior.
- The renewal workflow must not treat Key Vault import as sufficient proof of successful renewal.
- After import, the workflow must verify the certificate served by each first-release Front Door product hostname: `app.tokenobs.consultwithcloud.com`, `api.tokenobs.consultwithcloud.com`, and `ingest.tokenobs.consultwithcloud.com`.
- The workflow must compare the served certificate fingerprint or serial number against the renewed certificate imported into Key Vault.
- If the served certificate is still the previous version, the workflow must keep polling until the documented propagation window or configured timeout is reached.
- If Front Door does not pick up the new version within the allowed window, the renewal is failed operationally even though the Key Vault import succeeded.
- If testing proves that an explicit Front Door secret or custom-domain update is required for reliable production behavior, the workflow must perform that update and then verify the served edge certificate.
- The previous known-good Key Vault certificate version must remain available for rollback.

GitHub workflow constraint:

- GitHub Actions jobs and steps have a maximum configured timeout of 360 minutes.
- Azure Front Door customer-managed certificate rotation can take up to 72 hours when the Front Door secret version is set to `Latest`.
- Therefore, the first-release workflow must not hold one GitHub job open for the full Front Door propagation window.
- `operation=issue_import` must perform an immediate best-effort edge verification after Key Vault import.
- `operation=verify_edge` must be a separate manual verification operation that operators can run until all hostnames serve the renewed certificate or the 72-hour window is exhausted.

Served certificate verification command contract:

```bash
EXPECTED_FINGERPRINT="$(
  openssl x509 \
    -in "$LEGO_CERT_PATH" \
    -noout \
    -fingerprint \
    -sha256 \
  | cut -d= -f2
)"

for HOSTNAME in \
  app.tokenobs.consultwithcloud.com \
  api.tokenobs.consultwithcloud.com \
  ingest.tokenobs.consultwithcloud.com
do
  ACTUAL_FINGERPRINT="$(
    openssl s_client \
      -servername "$HOSTNAME" \
      -connect "$HOSTNAME:443" \
      </dev/null 2>/dev/null \
    | openssl x509 \
      -noout \
      -fingerprint \
      -sha256 \
    | cut -d= -f2
  )"

  test "$ACTUAL_FINGERPRINT" = "$EXPECTED_FINGERPRINT"
done
```

`operation=verify_edge` must derive the expected fingerprint from the current Key Vault certificate when the local `lego` certificate file is not available:

```bash
KEY_VAULT_CERT_DER="$RUNNER_TEMP/keyvault-cert.der"

az keyvault certificate show \
  --vault-name "$KEY_VAULT_NAME" \
  --name "$KEY_VAULT_CERTIFICATE_NAME" \
  --query "cer" \
  --output tsv \
| base64 -d > "$KEY_VAULT_CERT_DER"

EXPECTED_FINGERPRINT="$(
  openssl x509 \
    -inform DER \
    -in "$KEY_VAULT_CERT_DER" \
    -noout \
    -fingerprint \
    -sha256 \
  | cut -d= -f2
)"
```

Verification cadence:

- Production edge verification window is 72 hours after Key Vault import.
- `operation=issue_import` performs an immediate best-effort verification with a short timeout of 60 minutes and 5-minute polling.
- `operation=verify_edge` performs a bounded verification with a 60-minute job timeout and 5-minute polling.
- Operators repeat `operation=verify_edge` manually until all hostnames match or the 72-hour window is exceeded.
- If all hostnames still do not serve the renewed certificate after 72 hours, the renewal is failed operationally and the previous known-good Key Vault certificate version remains the rollback target.

Implementation issue creation needs:

- Exact Terraform shape for referencing the latest Key Vault certificate version.
- Rollback process if the new certificate import succeeds but edge serving fails.

## Failure Behavior

Renewal failure must:

- Preserve the existing certificate.
- Avoid deleting the previous known-good Key Vault certificate version.
- Fail the GitHub Actions workflow.
- Write a sanitized GitHub Actions job summary.
- Record the failed workflow run ID, actor, environment, hostname set, certificate name, and failure phase.
- Avoid writing private key material, PEM contents, PFX passwords, ACME account material, raw Azure access tokens, or full command output that may contain secrets to the job summary or logs.
- Not create a public GitHub issue with operational details.
- Leave ACME TXT records clean where safe.
- Require an explicit manual retry after the operator fixes the failing condition.

Failure phases:

| Phase | Examples | Required behavior |
| --- | --- | --- |
| Preflight | Repository, actor, branch, event, or workflow guard fails | Stop before Azure login |
| Azure authentication | OIDC login or Azure authorization fails | Stop without writing DNS or certificate material |
| DNS-01 | TXT record create, propagation, validation, or cleanup fails | Stop, clean temporary TXT records where safe, keep existing certificate |
| ACME issuance | Let's Encrypt order or finalize fails | Stop, keep existing certificate, do not import partial output |
| Key Vault import | PEM or PFX import fails | Stop, delete temporary files, keep existing certificate |
| Front Door verification | Key Vault import succeeds but edge hostnames do not serve the renewed certificate within timeout | Fail operationally, keep previous known-good Key Vault certificate version available |
| Cleanup | Temporary file cleanup or DNS cleanup partially fails | Fail or warn based on severity, but never hide a certificate renewal or verification failure |

First-release alerting:

- The failed GitHub workflow run and sanitized job summary are the first-release operational alert.
- No private notification sink is required for the first release.
- If a private notification channel is added in a later operations maturity phase, it must receive only sanitized metadata: workflow run ID, failure phase, environment, certificate name, hostname set, and links to the guarded run.
- Public issue creation is forbidden for renewal failures because a public issue can expose operational timing, hostnames, failure modes, or copied log details.

Retry and rollback:

- The workflow must not retry automatically after a failed renewal.
- Operators must fix the cause and run `workflow_dispatch` again.
- If Front Door is still serving the previous certificate, the previous served state is the rollback state.
- If a later implementation performs an explicit Front Door secret or custom-domain update and then verification fails, the workflow must stop and require an operator rollback to the previous known-good Key Vault certificate version.
- The workflow must not delete the newly imported Key Vault certificate version automatically after a verification failure; deletion is a separate privileged operation.

## Deferred Acceptance Criteria

These criteria apply only if the BYOC wildcard certificate option is adopted later.

- Renewal is implemented as a GitHub Actions workflow, not an Azure Container Apps Job.
- Renewal authenticates to Azure using OIDC.
- Renewal does not require Cloudflare API credentials.
- Renewal writes DNS-01 TXT records only in Azure DNS for `tokenobs.consultwithcloud.com`.
- Renewal uses `lego` with the Azure DNS provider.
- Renewal uses the documented `lego run` command contract and restores ACME account state from Key Vault.
- Renewal imports a wildcard certificate for `*.tokenobs.consultwithcloud.com` into retained per-environment Key Vault.
- Renewal proves direct PEM import first, with PFX conversion allowed only as a documented fallback.
- Renewal proves non-production Key Vault import with Let's Encrypt staging before production renewal.
- PFX fallback, if used, generates an ephemeral masked password and cleans up generated files before job end.
- Renewal provides separate manual `issue_import` and `verify_edge` operations because Front Door certificate propagation can exceed a single GitHub job.
- Renewal verifies the certificate served by `app`, `api`, and `ingest` against the renewed Key Vault certificate fingerprint.
- Renewal fails operationally if Azure Front Door does not serve the renewed certificate within the allowed propagation window.
- Renewal failure fails the GitHub workflow and writes a sanitized job summary.
- Renewal failure does not create a public GitHub issue with operational details.
- Renewal failure requires explicit manual retry through `workflow_dispatch`.
- Renewal does not expose private key material in logs.
- Renewal preserves previous certificate versions for rollback.
- Renewal workflow is covered by public-repository guardrail validation.
- Renewal is manual-only through `workflow_dispatch`.

## Verified Platform Facts

- GitHub Actions supports manually triggered workflows with `workflow_dispatch` inputs: https://docs.github.com/actions/using-workflows/workflow-syntax-for-github-actions#onworkflow_dispatch
- GitHub and Microsoft document OIDC authentication from GitHub Actions to Azure without long-lived cloud credentials: https://docs.github.com/actions/deployment/security-hardening-your-deployments/configuring-openid-connect-in-azure and https://learn.microsoft.com/en-us/azure/developer/github/connect-from-azure-openid-connect
- Azure CLI supports importing certificates into Key Vault with `az keyvault certificate import`: https://learn.microsoft.com/en-us/cli/azure/keyvault/certificate
- Azure Key Vault certificate import requires certificate material that includes the private key: https://learn.microsoft.com/en-us/azure/key-vault/certificates/tutorial-import-certificate
- Azure Key Vault secrets can store file-backed multiline secret values through Azure CLI: https://learn.microsoft.com/en-us/azure/key-vault/secrets/multiline-secrets
- Azure Front Door supports customer-managed certificates from Azure Key Vault for custom domains: https://learn.microsoft.com/en-us/azure/frontdoor/standard-premium/how-to-configure-https-custom-domain
- Azure Front Door TLS documentation says setting the secret version to `Latest` enables automatic rotation to a newer Key Vault certificate version: https://learn.microsoft.com/en-us/azure/frontdoor/end-to-end-tls
- Azure Front Door domain documentation says customer-managed certificate updates with `Latest` can take up to 72 hours to deploy: https://learn.microsoft.com/en-us/azure/frontdoor/domain?pivot=front-door-standard-premium#renew-customer-managed-tls-certificates
- Let's Encrypt wildcard issuance must use DNS-01 validation: https://letsencrypt.org/docs/faq/
- `lego` documents an Azure DNS provider for DNS-01 and supports settings such as `AZURE_SUBSCRIPTION_ID`, `AZURE_RESOURCE_GROUP`, `AZURE_ZONE_NAME`, Azure CLI shared credentials, and `AZURE_AUTH_METHOD=cli`: https://go-acme.github.io/lego/dns/azuredns/
- `lego` documents `lego run --dns <provider> -d <domain>` command syntax for obtaining or renewing certificates, including wildcard certificates: https://go-acme.github.io/lego/obtain/
- GitHub Actions workflow syntax documents a maximum configurable step timeout of 360 minutes: https://docs.github.com/actions/using-workflows/workflow-syntax-for-github-actions#jobsjob_idstepsstepstimeout-minutes
- `certbot-dns-azure` automates DNS-01 through Azure DNS, but its docs require a plugin configuration file and describe service principal or VM managed identity authentication: https://docs.certbot-dns-azure.co.uk/
