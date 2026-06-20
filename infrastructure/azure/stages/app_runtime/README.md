# App Runtime Stage

Responsibility: Container Apps environment, Product API, Product Ingestion Endpoint, Product Dashboard, Container Apps Jobs, managed identities, and app configuration.

This stage deploys the long-running runtime services:

- Product Dashboard.
- Product API.
- Product Ingestion Endpoint.

It also models the shared Product Jobs image as distinct Azure Container Apps Jobs through a local wrapper around the Azure Verified Module `Azure/avm-res-app-job/azurerm`.

The app runtime image inputs have no deployable defaults. The guarded ACR image publish workflow emits `app-runtime-images.auto.tfvars.json` with digest-pinned image references for the Product Dashboard, Product API, Product Ingestion Endpoint, shared Product Jobs image, and `container_registry_server`. The Terraform deploy workflow validates and downloads that artifact from the selected successful ACR Image Publish run before planning `app_runtime`, then supplies the foundation `container_registry_id` so runtime identities receive `AcrPull` on the shared Azure Container Registry. Each job uses the same image with explicit `dotnet TokenObservability.Jobs.dll <command>` arguments:

- `normalize-telemetry`.
- `detect-hotspots`.
- `generate-recommendations`.
- `redact-content`.
- `refresh-pricing`.
- `retention-cleanup`.
- `reprocess-session`.
- `tenant-maintenance`.

Jobs use separate Container Apps Job resources and user-assigned managed identities. Job settings support non-secret environment overrides, Key Vault backed secret references, retry limits, timeouts, and independent CPU or memory sizing. The stage does not accept plain secret values for job configuration.

The runtime proof uses manual triggers only. Event or scheduled trigger policies can be added in later issues that own queue and scheduler decisions.

Backend key: `app_runtime.tfstate`

Remote backend example: `backend.azurerm.tf.example`

Local validation:

```bash
scripts/terraform-stage-check.sh app_runtime
scripts/validate-terraform-app-runtime.sh
```

Use `tfswitch 1.14.7` locally when the system `terraform` binary is older than the stage `required_version`.

The guarded deploy helper supplies the stage from upstream non-secret outputs before planning:

- `foundation`: `container_registry_id` and `container_registry_login_server`.
- `network_private_data_plane`: `subnet_ids.container_apps_infrastructure` for upstream contract validation and future hardening.
- `observability_foundation`: `diagnostic_destinations`.
- `data_platform`: PostgreSQL, storage, captured-content, operational-storage, and lifecycle references.
- `ai_services`: AI service, recommendation model deployment, language PII detection, and content safety references.
- A selected ACR Image Publish artifact through `APP_RUNTIME_IMAGES_TFVARS_PATH`.

The stage intentionally uses Container App user-assigned managed identities and secret references rather than hardcoded secret values. Upstream outputs must not carry credentials, raw content, prompts, command output, tool results, logs, or private endpoint implementation details.

The current deployable path does not automatically attach the Container Apps environment to `network_private_data_plane.subnet_ids.container_apps_infrastructure`. Azure treats Container Apps environment infrastructure subnet and platform-managed infrastructure resource group settings as create-time infrastructure choices. Set `container_app_environment_infrastructure_subnet_id` only in a deliberate origin isolation hardening slice that accepts the required Container Apps environment replacement.

Diagnostics use the Azure Container Apps supported split: the environment-level diagnostic setting collects `allLogs` and `AllMetrics`, while Container App and Container Apps Job resource diagnostic settings collect `AllMetrics` only. Do not add `category_group = "allLogs"` to Container App or Container Apps Job diagnostic settings.

Origin evidence:

- `container_app_environment_public_network_access` defaults to `Enabled` for the current deployable path.
- Use the `direct_origin_validation_targets` output as the sanitized stable Container App ingress FQDN input to the edge stage and edge-origin validation runbook.
- Direct-origin blocking is deferred to a later origin isolation hardening slice.

Do not use `terraform apply -auto-approve`. Production applies must use the guarded manual workflow path defined by the Terraform production infrastructure architecture.
