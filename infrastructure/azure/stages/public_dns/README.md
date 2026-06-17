# Public DNS Stage

Responsibility: retained Azure DNS zone ownership for the delegated `tokenobs.consultwithcloud.com` product subdomain and delegation evidence for the Cloudflare apex zone.

This stage creates only the Azure DNS public zone. It does not manage `consultwithcloud.com`, Cloudflare credentials, Cloudflare apex records, or unrelated shared DNS resources.

Backend key: `public_dns.tfstate`

Remote backend example: `backend.azurerm.tf.example`

Local validation:

```bash
cd infrastructure/azure
tfswitch 1.14.7
terraform fmt -recursive
cd stages/public_dns
terraform init -backend=false
terraform validate
terraform plan -input=false -lock=false \
  -var="environment=pd" \
  -var="azure_region=eastus2" \
  -var="customer_organization_slug=internal" \
  -var="terraform_workspace_name=pd_eastus2_internal" \
  -var="resource_instance=core" \
  -var='tags={environment="pd",region="eastus2",product="token-observability",owner="platform",data_classification="internal",managed_by="terraform"}'
```

Guarded plan workflow path:

1. Plan and apply the `public_dns` stage from the single owner workspace `pd_eastus2_internal` before the `edge` stage needs managed certificate validation records.
2. Copy `product_dns_zone` from this stage output into the edge stage `azure_dns_zone` input.
3. Create the Cloudflare parent-zone NS delegation manually or through an approved Cloudflare workflow outside this repo, using `cloudflare_delegation_ns_records`.
4. Keep this stage out of disposable environment deletion. The DNS zone has `prevent_destroy = true` and is not offered by the deletion workflow.

Edge DNS record workflow:

- The `edge` stage creates `_dnsauth` TXT validation records and `app`, `api`, and `ingest` CNAME records inside this delegated Azure DNS zone when `azure_dns_zone.manage_records=true`.
- `front_door_managed_certificate_validation_records` exposes the TXT records required by Azure Front Door managed certificate validation.
- `front_door_custom_domain_cname_records` exposes the CNAME records that route public product hostnames to Front Door endpoints.
