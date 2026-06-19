# Data Platform Stage

Responsibility: Product Metadata Store, product Blob Storage foundations, backup settings, lifecycle settings, public allowlisted access, diagnostics, and non-secret downstream contracts.

Backend key: `data_platform.tfstate`

Remote backend example: `backend.azurerm.tf.example`

## Persistence Boundaries

PostgreSQL Flexible Server is the Product Metadata Store. It stores tenant-aware product metadata, normalized telemetry metadata, content references, redaction state, policy references, pricing basis, recommendations, and audit records.

Blob Storage stores only policy-approved redacted content blobs, approved bounded excerpts, review artifacts that passed policy gates, and operational validation artifacts. Raw prompts, raw tool outputs, raw command output, tool results, code content, secrets, raw Terraform state, connection strings, storage keys, SAS URLs, credentials, captured content payloads, and tenant-private data are not emitted by this stage.

Content Capture Mode remains disabled by default at the product policy layer. This stage only provisions the storage foundation used after policy approval and successful pre-storage redaction.

## Backup And Restore Assumptions

PostgreSQL backup retention defaults to 35 days. Geo-redundant backup is disabled by default because support and cost vary by selected region and SKU; enable it only after the environment-specific platform decision is made.

PostgreSQL restore drills are not run by this stage. The non-secret `postgresql_restore_contract` output gives operations issues the server resource ID, database names, retention setting, and restore boundary that restore drills need.

Blob restore-readiness is configured through blob versioning, change feed, soft delete, container soft delete, and point-in-time restore settings where supported by the storage account configuration.

## Lifecycle Assumptions

Captured content and approved bounded excerpts default to 30 days of lifecycle retention. Operational validation artifacts default to 180 days. PostgreSQL metadata retention is implemented by application migrations and jobs, not by this Terraform stage.

The storage lifecycle policy targets the captured-content containers and the operational lifecycle-validation prefix. The unnamed broader operational storage layout remains intentionally small until operations issues define additional artifacts.

## Downstream Outputs

Outputs expose only non-secret values:

- PostgreSQL server resource ID, name, FQDN, database names, and restore contract.
- Storage account resource ID, name, Blob FQDN, container names, content storage contract, operational storage contract, and lifecycle settings.
- Resource group IDs and expected workspace name.

App runtime and jobs must use managed identity and public service FQDNs constrained by firewall and network allowlists. Runtime managed identity principal IDs are supplied by downstream runtime issues.

## Provider And AVM Choices

The stage calls only local wrapper modules. The local PostgreSQL wrapper uses `Azure/avm-res-dbforpostgresql-flexibleserver/azurerm` version `0.2.2`. The local Storage Account wrapper uses `Azure/avm-res-storage-storageaccount/azurerm` version `0.7.2`.

The Storage Account AVM manages child containers, diagnostics, network rules, and lifecycle management through AzAPI-backed AVM internals. The stage does not use direct AzureRM container resources and does not enable shared key access.

Private endpoint hardening is deferred to a later issue and is not part of the current deployable Terraform path.

No direct AzureRM or AzAPI resource exception is added by this stage beyond existing local wrappers. If a future provider gap appears, document it here before adding the exception.

## Local Validation

```bash
terraform init -backend=false
terraform validate
terraform plan -input=false -lock=false \
  -var="environment=dv" \
  -var="azure_region=eastus2" \
  -var="customer_organization_slug=internal" \
  -var="terraform_workspace_name=dv_eastus2_internal" \
  -var="resource_instance=core" \
  -var='tags={environment="dv",region="eastus2",product="token-observability",owner="platform",data_classification="internal",managed_by="terraform"}' \
  -var='postgresql_ad_administrators={deployment={tenant_id="00000000-0000-0000-0000-000000000000",object_id="00000000-0000-0000-0000-000000000000",principal_name="deployment-identity",principal_type="ServicePrincipal"}}' \
  -var='diagnostic_destinations={data_platform={log_analytics_workspace_resource_id="/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-example/providers/Microsoft.OperationalInsights/workspaces/log-example",destination_type="Dedicated"}}'
```

Use `scripts/terraform-stage-check.sh data_platform` and `scripts/validate-terraform-data-platform.sh` for backend-free validation and structural guardrails.

Do not add .NET, xUnit, or C# tests for Terraform behavior.
