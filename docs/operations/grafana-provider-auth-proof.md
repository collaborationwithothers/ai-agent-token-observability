# Grafana Provider Authentication Proof

## Purpose

Issue #63 proves the Terraform authentication path used by issue #64 to deploy repo-versioned Azure Managed Grafana dashboard JSON.

The default path is Microsoft Entra bearer token authentication to the Azure Managed Grafana data plane. The service account token fallback path is allowed only if the Entra proof fails for Terraform provider use.

This proof is intentionally non-deploying. It uses a read-only Terraform data source, does not create dashboards, folders, service accounts, tokens, or data sources, and does not print Grafana API response bodies.

## Default Proof Path

The Azure Managed Grafana data plane accepts Microsoft Entra tokens for the audience:

```text
6f2d169c-08f3-4a4c-a982-bcaf2d038c45
```

Run the proof against a non-production Azure Managed Grafana endpoint:

```bash
scripts/prove-grafana-provider-auth.sh --mode entra --endpoint "$GRAFANA_ENDPOINT"
```

The script uses `GRAFANA_ENTRA_ACCESS_TOKEN` when supplied. Otherwise it gets a token with:

```bash
az account get-access-token --resource 6f2d169c-08f3-4a4c-a982-bcaf2d038c45 --query accessToken --output tsv --only-show-errors
```

The token is passed to the `grafana/grafana` provider through `GRAFANA_AUTH` together with `GRAFANA_URL`. The Terraform fixture at `infrastructure/azure/proofs/grafana_provider_auth` contains only:

- An empty `provider "grafana" {}` block.
- A read-only `data "grafana_folders" "auth_probe" {}` probe.
- An exact provider pin to `grafana/grafana` `4.39.0`.

The proof script clears inherited Terraform logging controls such as `TF_LOG`, `TF_LOG_PATH`, and related `TF_LOG_*` variables inside the token-bearing subprocess before Terraform runs. Do not reintroduce Terraform debug logging for this proof path.

## Service Account Token Fallback

Use this only if the Entra proof fails for Terraform provider use and the failure is recorded in sanitized evidence.

```bash
GRAFANA_SERVICE_ACCOUNT_TOKEN="<short-lived-token>" \
  scripts/prove-grafana-provider-auth.sh --mode service-account --endpoint "$GRAFANA_ENDPOINT"
```

Fallback requirements:

- Keep service accounts and API keys disabled by default until approved for this automation scenario.
- Store any approved token in Key Vault, never in GitHub repository files, Terraform variables, Terraform outputs, or remote state.
- Use the least privileged Grafana role that can deploy the dashboard and folder resources introduced by issue #64.
- Prefer expiring tokens and rotate them through an explicit manual workflow.
- Do not enable the fallback in production workflows by default.

## Failure Interpretation

If Entra mode fails, first verify:

- The endpoint is the native Azure Managed Grafana HTTPS endpoint.
- The caller has a Grafana role assignment on the Azure Managed Grafana resource.
- The token audience is `6f2d169c-08f3-4a4c-a982-bcaf2d038c45`.
- Role assignments had enough time to propagate.
- The direct API probe works with the same token:

```bash
TOKEN="$(az account get-access-token --resource 6f2d169c-08f3-4a4c-a982-bcaf2d038c45 --query accessToken --output tsv --only-show-errors)"
curl -i -H "Authorization: Bearer $TOKEN" "$GRAFANA_ENDPOINT/api/org"
```

Do not paste the `curl` response body, bearer token, service account token, headers, cookies, or Terraform output into GitHub issues, PRs, logs, or docs. Do not paste bearer tokens anywhere in repository artifacts.

## Sanitized Evidence Template

```text
Issue: #63
Workspace: <environment and region only>
Grafana endpoint host: <host only, no path or query>
Proof mode: entra | service-account
Caller identity type: user | service principal | managed identity
Direct API token audience checked: yes | no
Terraform probe: passed | failed
Fallback required: yes | no
Sanitization: no bearer tokens, service account tokens, response bodies, headers, cookies, or Terraform credential output recorded
Decision for #64: Entra GRAFANA_AUTH provider auth | Key Vault backed service account fallback
```

## References

- [Authenticate to Azure Managed Grafana data plane APIs with Microsoft Entra ID](https://learn.microsoft.com/en-us/azure/managed-grafana/how-to-authenticate-data-plane-api)
- [How to use service accounts in Azure Managed Grafana](https://learn.microsoft.com/en-us/azure/managed-grafana/how-to-service-accounts)
- [Secure Azure Managed Grafana](https://learn.microsoft.com/en-us/azure/managed-grafana/secure-azure-managed-grafana)
- [Grafana Terraform provider](https://registry.terraform.io/providers/grafana/grafana/latest/docs)
