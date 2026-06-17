# Public DNS And Certificates

## Purpose

This document defines the public DNS and TLS certificate boundary for the Azure Production MVP.

It covers the relationship between the apex domain in Cloudflare, the delegated product subdomain in Azure DNS, Azure Front Door managed certificates, Azure Container Apps private origins, and Azure Front Door custom domains.

## Decision

The product will not move the apex domain to Azure DNS.

The apex domain is:

```text
consultwithcloud.com
```

The apex domain remains managed in Cloudflare.

The product subdomain delegated to Azure DNS is:

```text
tokenobs.consultwithcloud.com
```

Cloudflare remains the DNS provider for the apex zone. Cloudflare contains NS records that delegate `tokenobs.consultwithcloud.com` to the Azure DNS name servers assigned to the Azure public DNS zone.

Azure DNS is authoritative only for the delegated product subdomain and its records.

## Rationale

Delegating only `tokenobs.consultwithcloud.com` keeps the blast radius low.

Benefits:

- The apex domain and unrelated records stay in Cloudflare.
- Product DNS can be managed through Azure RBAC, Terraform, and Azure DNS APIs.
- Azure Front Door custom domain records and DNS validation records stay close to the Azure edge.
- The delegated product zone can be treated as shared product infrastructure that is not destroyed by environment deletion workflows.

Rejected option:

- Delegate `consultwithcloud.com` entirely to Azure DNS.

Reason rejected:

- It would make unrelated DNS depend on this product infrastructure and increase the operational blast radius.

## DNS Zone Model

Parent zone:

| Property | Value |
| --- | --- |
| Zone | `consultwithcloud.com` |
| Provider | Cloudflare |
| Product-owned | No |
| Terraform-managed by this repo | No |

Product zone:

| Property | Value |
| --- | --- |
| Zone | `tokenobs.consultwithcloud.com` |
| Provider | Azure DNS |
| Product-owned | Yes |
| Terraform-managed by this repo | Yes, after initial delegation prerequisites exist |
| Deletion workflow behavior | Retained as shared infrastructure |

Cloudflare parent zone records:

```text
tokenobs.consultwithcloud.com. NS <azure-dns-ns1>
tokenobs.consultwithcloud.com. NS <azure-dns-ns2>
tokenobs.consultwithcloud.com. NS <azure-dns-ns3>
tokenobs.consultwithcloud.com. NS <azure-dns-ns4>
```

The exact Azure DNS name server values come from the Azure DNS zone after it is created.

## Hostname Model

First-release hostnames:

| Hostname | Purpose | Edge |
| --- | --- | --- |
| `app.tokenobs.consultwithcloud.com` | Product Dashboard | Azure Front Door |
| `api.tokenobs.consultwithcloud.com` | Product API | Azure Front Door |
| `ingest.tokenobs.consultwithcloud.com` | Product Ingestion Endpoint | Azure Front Door |

Rules:

- Public product hostnames must live under `tokenobs.consultwithcloud.com`.
- The apex `consultwithcloud.com` must not be required for product deployment.
- The product must not require Cloudflare API credentials for first-release certificate issuance or renewal.
- Product DNS records are shared foundation resources unless explicitly scoped to disposable environments.
- Azure Managed Grafana does not get a first-release product vanity hostname. Authorized users reach it through the native Azure Managed Grafana endpoint linked from Product Dashboard.
- A Grafana vanity hostname can be reconsidered only if Azure Managed Grafana supports custom domains directly or if a reverse-proxy pattern is explicitly accepted as a later tradeoff.

## Certificate And Edge Model

First-release certificate authority:

```text
Azure Front Door managed certificate authority
```

Validation method:

```text
Azure Front Door custom-domain DNS validation
```

First-release custom domains:

```text
app.tokenobs.consultwithcloud.com
api.tokenobs.consultwithcloud.com
ingest.tokenobs.consultwithcloud.com
```

Public edge:

```text
Azure Front Door Premium with WAF
```

Origin path:

```text
Azure Front Door Premium Private Link to Azure Container Apps
```

Origin certificate model:

```text
Azure Container Apps default ingress certificate and default ACA FQDN as origin host header
```

First-release decisions:

- Use Azure Front Door managed certificates for explicit product hostnames.
- Do not use a wildcard certificate for the first release.
- Do not bind product custom domains or product certificates directly to Azure Container Apps for the first release.
- Configure public DNS records under `tokenobs.consultwithcloud.com` to point to the Azure Front Door endpoint.
- Configure Front Door origins with the Azure Container Apps generated FQDN as the origin host name and origin host header.
- Use Azure Front Door Premium Private Link to reach Azure Container Apps origins.
- Disable public network access on the Azure Container Apps environment in production so direct access to the generated ACA FQDN cannot bypass Front Door.
- Treat any public ACA origin path as a failed production-readiness check.

Tradeoff:

- Azure Front Door managed certificates avoid operating ACME account state, private key import, Key Vault certificate rotation, and long-running edge certificate verification in the first release.
- The cost is that certificate issuance is per explicit hostname rather than one wildcard certificate.
- Azure Front Door Premium is required for Private Link origin isolation.
- Front Door managed certificates solve public TLS only. They do not prevent origin bypass by themselves.
- Origin bypass is prevented by the Private Link and disabled-public-network-access design, not by DNS or certificate configuration.
- Managed Grafana is intentionally excluded from this certificate lifecycle for the first release because it uses the native Azure Managed Grafana endpoint.

Deferred target-state option:

- Bring Your Own Certificate with a wildcard certificate in retained per-environment Key Vaults can be reconsidered if multi-tenant hostname growth makes explicit managed certificates operationally expensive.
- The deferred BYOC renewal workflow is captured in [../operations/certificate-renewal.md](../operations/certificate-renewal.md) and is not a first-release requirement.

## Terraform Ownership

Terraform should manage:

- Azure DNS zone `tokenobs.consultwithcloud.com`.
- Azure DNS records inside `tokenobs.consultwithcloud.com`.
- Azure Front Door custom domain bindings.
- Azure Front Door managed certificate settings for explicit product hostnames.
- Azure Front Door Premium Private Link origin configuration.
- Azure Container Apps origin settings required to prevent direct public bypass.

Terraform should not manage:

- Cloudflare account.
- Cloudflare apex zone.
- Cloudflare apex-zone records unrelated to delegation.
- The initial Cloudflare NS delegation if the product repo is not granted Cloudflare API access.

Terraform stage ownership:

- The retained `public_dns` stage owns the Azure DNS zone `tokenobs.consultwithcloud.com` from the single owner workspace `pd_eastus2_internal` and outputs the Azure name servers that Cloudflare must delegate from the apex zone.
- The retained `.github/workflows/terraform-public-dns.yml` workflow is the only repository workflow that may plan and apply the `public_dns` stage. It derives the owner workspace as `pd_eastus2_internal`, applies only the saved public DNS plan artifact, and emits the manual Cloudflare NS records as sanitized workflow-summary evidence.
- The retained public DNS workflow also provides `verify_delegation`, which compares public `dig NS tokenobs.consultwithcloud.com` results with the Terraform output `product_dns_zone_name_servers` after the manual Cloudflare parent-zone change.
- The disposable `edge` stage owns Front Door custom domains plus the `_dnsauth`, `app`, `api`, and `ingest` records inside the delegated Azure DNS zone when its `azure_dns_zone.manage_records` input is true.
- The initial Cloudflare NS delegation remains outside this repo's Terraform state unless a later issue explicitly grants and governs Cloudflare API access.

## Shared Resource Behavior

The following resources are shared foundation resources and must be retained by infrastructure deletion workflows:

- Azure DNS zone `tokenobs.consultwithcloud.com`.
- Azure Container Registry.
- Shared deployment identities.
- Terraform remote state storage.
- Per-environment retained foundation Key Vaults if used for application secrets or future BYOC certificates.

Environment deletion workflows may remove environment-specific DNS records only if the record is clearly owned by the deleted environment and is not part of the shared product hostname set.

If the deferred wildcard BYOC option is adopted later, Terraform may own the retained Key Vault and RBAC boundary, but certificate private key material, PEM, PFX, ACME account state, and imported certificate contents must stay outside Terraform state.

## Operational Requirements

First-release certificate operations rely on Azure Front Door managed certificate issuance and renewal for explicit hostnames.

The first implementation must prove in a non-production environment:

- Azure Front Door serves managed certificates for `app`, `api`, and `ingest`.
- Public DNS points product hostnames to the Azure Front Door endpoint.
- Public delegation for `tokenobs.consultwithcloud.com` has been verified after the manual Cloudflare NS record change.
- Front Door origin health succeeds over Private Link.
- Direct HTTPS access to the generated Azure Container Apps FQDN fails or is unreachable from the public internet after public network access is disabled.
- Product authentication and redirect URI behavior uses the public Front Door hostnames, not generated ACA hostnames.
- WAF, rate limits, tenant validation, and application authentication are reached only through the Front Door path.

The proof procedure is defined in [../operations/edge-origin-validation.md](../operations/edge-origin-validation.md).

Deferred BYOC certificate renewal is defined in [../operations/certificate-renewal.md](../operations/certificate-renewal.md). It is not part of the first-release implementation unless the Front Door managed certificate decision is reopened.

## Verified Platform Facts

- Azure DNS delegation uses NS records in the parent zone to delegate a child zone to Azure DNS authoritative name servers: https://learn.microsoft.com/en-us/azure/dns/dns-domain-delegation
- Cloudflare supports subdomain delegation by creating NS records for a subdomain in the parent zone: https://developers.cloudflare.com/dns/zone-setups/subdomain-setup/setup/
- Azure Front Door managed certificates use DNS TXT validation for custom domains: https://learn.microsoft.com/en-us/azure/frontdoor/domain
- Azure Front Door supports managed certificates and customer-managed certificates for custom domains: https://learn.microsoft.com/en-us/azure/frontdoor/standard-premium/how-to-configure-https-custom-domain
- Azure Front Door Premium can connect to origins through Private Link, removing the need for origins to be publicly accessible: https://learn.microsoft.com/en-us/azure/frontdoor/private-link
- Azure Container Apps ingress with `external` is reachable by FQDN from outside the environment: https://learn.microsoft.com/en-us/azure/container-apps/ingress-overview
- Azure Container Apps can be exposed securely through Azure Front Door Premium with private endpoints and public network access disabled: https://learn.microsoft.com/en-us/azure/container-apps/front-door-custom-virtual-network-private-link
- Azure Managed Grafana workspaces expose a native Endpoint URL and use Microsoft Entra single sign-on: https://learn.microsoft.com/en-us/azure/managed-grafana/quickstart-managed-grafana-portal
- Azure Managed Grafana roles can be assigned to Microsoft Entra users, groups, service principals, and managed identities: https://learn.microsoft.com/en-us/azure/managed-grafana/how-to-manage-access-permissions-users-identities
