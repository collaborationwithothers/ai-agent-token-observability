# Edge Origin Validation

## Purpose

This runbook defines the proof required before the Azure Front Door and Azure Container Apps edge design is considered implementation-ready.

The goal is to prove that users and harnesses can reach product services through Azure Front Door, while generated Azure Container Apps FQDN origins are recorded as sanitized current-path evidence. Direct-origin blocking is deferred to a later origin isolation hardening slice.

## Scope

First-release public hostnames:

```text
app.tokenobs.consultwithcloud.com
api.tokenobs.consultwithcloud.com
ingest.tokenobs.consultwithcloud.com
```

First-release edge:

```text
Azure Front Door Premium WAF
```

First-release origin isolation:

```text
Public Front Door routes to generated Azure Container Apps FQDN origins until origin network hardening is implemented.
```

First-release certificate model:

```text
Azure Front Door managed certificates for explicit product hostnames
```

## Required Proofs

### 1. DNS Delegation Proof

The validation must prove:

- `tokenobs.consultwithcloud.com` is delegated from Cloudflare to Azure DNS.
- `app`, `api`, and `ingest` records are managed inside the Azure DNS delegated zone.
- Public product hostnames resolve to the Azure Front Door endpoint path, not directly to Azure Container Apps.

Command contract:

```bash
dig NS tokenobs.consultwithcloud.com
dig CNAME app.tokenobs.consultwithcloud.com
dig CNAME api.tokenobs.consultwithcloud.com
dig CNAME ingest.tokenobs.consultwithcloud.com
```

### 2. Front Door Certificate Proof

The validation must prove:

- Azure Front Door has approved each custom domain.
- Azure Front Door has deployed a managed certificate for each first-release hostname.
- The served certificate subject alternative names include the requested hostname.
- TLS minimum policy follows the Front Door profile policy.

Command contract:

```bash
for HOSTNAME in \
  app.tokenobs.consultwithcloud.com \
  api.tokenobs.consultwithcloud.com \
  ingest.tokenobs.consultwithcloud.com
do
  openssl s_client \
    -servername "$HOSTNAME" \
    -connect "$HOSTNAME:443" \
    </dev/null 2>/dev/null \
  | openssl x509 -noout -subject -issuer -dates -ext subjectAltName
done
```

The workflow or operator output must not publish sensitive headers, cookies, tokens, or full authentication redirects.

### 3. Front Door Route Proof

The validation must prove:

- HTTPS requests to first-release product hostnames succeed through Azure Front Door.
- HTTP requests redirect to HTTPS or are rejected according to the Front Door route policy.
- Front Door WAF and route logs show the request entered through Front Door.
- Origin health is healthy through the configured generated ACA FQDN origin.

Command contract:

```bash
curl -I https://app.tokenobs.consultwithcloud.com/
curl -I https://api.tokenobs.consultwithcloud.com/health/ready
curl -I https://ingest.tokenobs.consultwithcloud.com/health/ready
```

Expected result:

- Health endpoints return the documented success status through Front Door.
- Response headers and diagnostic logs show the public path went through Front Door.

### 4. ACA Origin Evidence

The validation must prove:

- The generated Azure Container Apps FQDN for each public product service is captured as current public-origin evidence.
- Terraform app runtime output `direct_origin_validation_targets.public_network_access` is recorded as current-path evidence.
- Direct-origin blocking is deferred to a later origin isolation hardening slice.

Command contract:

```bash
curl -I --max-time 15 "https://$DASHBOARD_ACA_FQDN/"
curl -I --max-time 15 "https://$API_ACA_FQDN/health/ready"
curl -I --max-time 15 "https://$INGEST_ACA_FQDN/health/ready"
```

Expected production result:

- Direct public requests to generated ACA FQDNs are recorded as evidence for the current deployable path.
- Direct-origin blocking is not a current release gate.
- Any HTTP response served by the application records current public-origin reachability and is not a failed proof until deferred origin isolation hardening is reintroduced.

Failure rule:

- If a generated ACA FQDN returns a successful application response from the public internet, record the result as current public-origin evidence.
- If deferred origin isolation hardening is reintroduced, this runbook must add a separate blocking direct-origin proof.
- Container Apps environment public network access remains `Enabled` in the current deployable path.

### 5. Origin Host Header Proof

The validation must prove:

- Front Door origin host name uses the generated ACA FQDN.
- Front Door origin host header uses the generated ACA FQDN unless a later tested design changes the TLS origin contract.
- End-to-end HTTPS succeeds from Front Door to the ACA origin.

Rationale:

- Public hostnames are served by Front Door managed certificates.
- ACA does not need product custom domains or product public certificates in the first release.
- ACA origin TLS uses the generated ACA hostname and certificate.

Required audit evidence:

- Record the public direct-origin proof result without cookies, bearer tokens, request payloads, or response bodies.
Private endpoint hardening is deferred to a later issue and is not part of the current deployable Terraform path.

### 6. Authentication Callback Proof

The validation must prove:

- Entra redirect URIs use public Front Door hostnames.
- Product Dashboard and Product API interpret forwarded host and protocol correctly.
- Authentication cookies, callback paths, and logout redirects do not expose generated ACA hostnames.

Required checks:

- Sign-in starts from `https://app.tokenobs.consultwithcloud.com`.
- Redirect URI registered in Entra matches the Front Door public hostname.
- Post-login browser location remains under the Front Door public hostname.
- API calls from the Product Dashboard use the intended public or private Product API path and do not call generated ACA FQDNs.
- Terraform edge output `public_auth_callback_base_urls.dashboard` is the base URL used for dashboard callback and logout redirect configuration.
- Terraform edge output `public_auth_callback_base_urls.api` is the base URL used anywhere Product API public callback configuration is required.

### 7. Managed Certificate And DNS Record Proof

The validation must prove:

- Front Door custom domains exist for `app.tokenobs.consultwithcloud.com`, `api.tokenobs.consultwithcloud.com`, and `ingest.tokenobs.consultwithcloud.com`.
- Front Door custom domains use managed certificates, not customer-managed certificates.
- DNS TXT validation records match the values from Terraform edge output `front_door_managed_certificate_validation_records`.
- DNS CNAME records match the values from Terraform edge output `front_door_custom_domain_cname_records`.
- Azure Managed Grafana is not assigned a product vanity hostname for the first release.
- No Azure Container Apps custom domains or product certificates are required for first-release public ingress.

## Workflow Placement

The first implementation may run this proof manually from an operator machine for `dv`.

For `qa`, `pp`, and `pd`, validation should be represented as a guarded GitHub Actions workflow or workflow job using the same public-repository gates as Terraform deployment workflows.

Managed Azure VNet runners may be used for allowlisted-resource validation and Azure API checks. They do not prove deferred public-origin isolation by themselves.

Implemented workflow:

```text
.github/workflows/edge-origin-validation.yml
```

The workflow derives the Terraform workspace from `environment`, `azure_region`, and `customer_organization_slug`. The customer organization slug defaults to `internal` and can be overridden when validating a different customer scope.

The workflow reads the `app_runtime` and `edge` Terraform state outputs on the managed Azure runner, validates the public Front Door hostname and authentication callback contract, probes the public Front Door hostnames, and exports only sanitized generated ACA origin URLs to a separate GitHub-hosted runner job. That public runner job has no Azure login and records current public-origin evidence from a public-internet perspective. Workflow summaries must contain only sanitized status evidence.

## Acceptance Criteria

- DNS delegation and public hostname records are correct.
- Front Door managed certificates are deployed for `app`, `api`, and `ingest`.
- Public Front Door HTTPS health checks succeed.
- Front Door origin health succeeds through the configured generated ACA FQDN origins.
- Direct public requests to generated ACA FQDNs are recorded as current public-origin evidence.
- Auth callbacks and browser-visible URLs use public Front Door hostnames.
- ACA product custom domains and product certificates are not required for the first release.
- Validation outputs are sanitized before being stored in workflow summaries or issues.

## Verified Platform Facts

- Azure Front Door managed certificates use DNS TXT validation for custom domains: https://learn.microsoft.com/en-us/azure/frontdoor/domain
- Azure Front Door supports managed certificates and customer-managed certificates for custom domains: https://learn.microsoft.com/en-us/azure/frontdoor/standard-premium/how-to-configure-https-custom-domain
- Azure Container Apps ingress with `external` is accessible through its FQDN: https://learn.microsoft.com/en-us/azure/container-apps/ingress-overview
- Azure Front Door supports `X-Forwarded-For`, `X-Forwarded-Host`, and `X-Forwarded-Proto` headers: https://learn.microsoft.com/en-us/azure/frontdoor/front-door-faq
