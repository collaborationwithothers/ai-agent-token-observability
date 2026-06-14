# Edge Origin Validation

## Purpose

This runbook defines the proof required before the Azure Front Door and Azure Container Apps edge design is considered implementation-ready.

The goal is to prove that users and harnesses can reach product services through Azure Front Door, while generated Azure Container Apps FQDNs cannot be used as a public bypass path in production.

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
Azure Front Door Premium Private Link to Azure Container Apps
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
- Origin health is healthy through the Private Link origin.

Command contract:

```bash
curl -I https://app.tokenobs.consultwithcloud.com/healthz
curl -I https://api.tokenobs.consultwithcloud.com/healthz
curl -I https://ingest.tokenobs.consultwithcloud.com/healthz
```

Expected result:

- Health endpoints return the documented success status through Front Door.
- Response headers and diagnostic logs show the public path went through Front Door.

### 4. ACA Direct Bypass Proof

The validation must prove:

- The generated Azure Container Apps FQDN for each public product service is not reachable from the public internet in production.
- Public network access is disabled on the Azure Container Apps environment for production.
- Private endpoint connections required for the Front Door origin are approved.

Command contract:

```bash
for ACA_FQDN in \
  "$DASHBOARD_ACA_FQDN" \
  "$API_ACA_FQDN" \
  "$INGEST_ACA_FQDN"
do
  curl -I --max-time 15 "https://$ACA_FQDN/healthz"
done
```

Expected production result:

- Direct public requests to generated ACA FQDNs fail, time out, or return an explicit platform rejection.
- Direct generated ACA FQDN access must not return the application health response from the public internet.

Failure rule:

- If a generated ACA FQDN returns a successful application response from the public internet in `pp` or `pd`, the release is not production-ready.

### 5. Origin Host Header Proof

The validation must prove:

- Front Door origin host name uses the generated ACA FQDN.
- Front Door origin host header uses the generated ACA FQDN unless a later tested design changes the TLS origin contract.
- End-to-end HTTPS succeeds from Front Door to the ACA origin.

Rationale:

- Public hostnames are served by Front Door managed certificates.
- ACA does not need product custom domains or product public certificates in the first release.
- ACA origin TLS uses the generated ACA hostname and certificate.

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

## Workflow Placement

The first implementation may run this proof manually from an operator machine for `dv`.

For `qa`, `pp`, and `pd`, validation should be represented as a guarded GitHub Actions workflow or workflow job using the same public-repository gates as Terraform deployment workflows.

Managed Azure VNet runners may be used for private-resource validation and Azure API checks. They do not prove public-origin isolation by themselves. Direct-bypass checks must include a public-internet perspective or another documented equivalent.

## Acceptance Criteria

- DNS delegation and public hostname records are correct.
- Front Door managed certificates are deployed for `app`, `api`, and `ingest`.
- Public Front Door HTTPS health checks succeed.
- Front Door origin health succeeds through Private Link.
- Direct public requests to generated ACA FQDNs do not reach the application in `pp` or `pd`.
- Auth callbacks and browser-visible URLs use public Front Door hostnames.
- ACA product custom domains and product certificates are not required for the first release.
- Validation outputs are sanitized before being stored in workflow summaries or issues.

## Verified Platform Facts

- Azure Front Door managed certificates use DNS TXT validation for custom domains: https://learn.microsoft.com/en-us/azure/frontdoor/domain
- Azure Front Door supports managed certificates and customer-managed certificates for custom domains: https://learn.microsoft.com/en-us/azure/frontdoor/standard-premium/how-to-configure-https-custom-domain
- Azure Front Door Premium can connect to origins through Private Link, removing the need for origins to be publicly accessible: https://learn.microsoft.com/en-us/azure/frontdoor/private-link
- Azure Container Apps ingress with `external` is accessible through its FQDN: https://learn.microsoft.com/en-us/azure/container-apps/ingress-overview
- Azure Container Apps can be exposed through Azure Front Door Premium with public network access disabled: https://learn.microsoft.com/en-us/azure/container-apps/front-door-custom-virtual-network-private-link
- Azure Front Door supports `X-Forwarded-For`, `X-Forwarded-Host`, and `X-Forwarded-Proto` headers: https://learn.microsoft.com/en-us/azure/frontdoor/front-door-faq
