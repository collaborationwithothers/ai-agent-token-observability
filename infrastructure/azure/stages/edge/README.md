# Edge Stage

Responsibility: Azure Front Door Premium, WAF policy, service endpoints, service routes, origin groups, generated Container Apps FQDN origins, managed custom domains, Front Door managed certificates, DNS validation workflow, diagnostic settings, and rate-limit rules.

AVM check for issue #56:

- The local AVM checkout at `/Users/harisubramaniam/source/BHS/avm-terraform-modules` does not contain a CDN or Front Door profile module.
- The public Terraform Registry has `Azure/avm-res-cdn-profile/azurerm` version `0.1.9`.
- This implementation uses a repo-local wrapper at `../../modules/front_door_edge` with AzureRM resources because the checked-in AVM source used by this repo is unavailable for Front Door, and this issue needs WAF policy, security policy association, diagnostics, and rate-limit rules in one reviewable slice.

Backend key: `edge.tfstate`

Remote backend example: `backend.azurerm.tf.example`

Local validation:

```bash
cd infrastructure/azure
tfswitch 1.14.7
terraform fmt -recursive
cd ../..
scripts/terraform-stage-check.sh edge
```

Guarded plan workflow path:

1. Initialize the `edge` stage with the non-production remote backend config.
2. Select the workspace named `{environment}_{azureRegion}_{customerOrganizationSlug}`.
3. Pass `container_app_fqdns`, `container_app_environment_id`, and `container_app_environment_public_network_access` from the same-workspace `app_runtime` stage output.
4. Pass `diagnostic_destinations` from the same-workspace `observability_foundation` stage output.
5. Pass `azure_dns_zone` from the retained `public_dns` stage output `product_dns_zone` in the owner workspace `pd_eastus2_internal`. Set `manage_records=true` to let Terraform create Front Door TXT validation records and CNAME records for `app`, `api`, and `ingest`.
6. Run `terraform validate` before `terraform plan`.

Workflow summaries for this stage must stay sanitized. They can include the stage, workspace, commit SHA, and result, but must not print Container Apps FQDNs, private endpoint details, secrets, raw Terraform output, or diagnostic resource IDs.

Managed certificate and DNS workflow:

- `front_door_custom_domain_hostnames` must contain `app.tokenobs.consultwithcloud.com`, `api.tokenobs.consultwithcloud.com`, and `ingest.tokenobs.consultwithcloud.com`.
- `front_door_managed_certificate_validation_records` exposes the `_dnsauth` TXT record name, FQDN, zone, TTL, and value required by Front Door managed certificate validation.
- `front_door_custom_domain_cname_records` exposes the CNAME record name, FQDN, zone, TTL, and value that route public product hostnames to the Front Door endpoints.
- If `azure_dns_zone.manage_records` is false or `azure_dns_zone` is null, operators must create the exposed TXT and CNAME records through the delegated DNS workflow before expecting custom domain validation to complete.
- `public_auth_callback_base_urls` is the authoritative public hostname set for browser-visible authentication callbacks and redirects.
- Azure Managed Grafana intentionally has no custom hostname in this stage for the first release.
- Customer-managed certificates and BYOC renewal remain out of scope for first release.

Private endpoint hardening is deferred to a later issue and is not part of the current deployable Terraform path.

Do not run `terraform apply` from local development. Production apply remains a guarded workflow operation.
