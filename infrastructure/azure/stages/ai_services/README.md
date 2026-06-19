# AI Services Stage

Responsibility: Azure AI Language, Azure AI Content Safety, Azure OpenAI or Foundry resources, deployment aliases, and private access where feasible.

Backend key: `ai_services.tfstate`

Remote backend example: `backend.azurerm.tf.example`

Local validation:

```bash
terraform init -backend=false
terraform validate
terraform plan -input=false -lock=false \
  -var="environment=dv" \
  -var="azure_region=eastus2" \
  -var="customer_organization_slug=internal" \
  -var="terraform_workspace_name=dv_eastus2_internal" \
  -var="resource_instance=core" \
  -var='tags={environment="dv",region="eastus2",product="token-observability",owner="platform",data_classification="internal",managed_by="terraform"}'
```

## Resource Boundaries

This stage owns the first-release Azure AI Services account in `rg-ai`. The account is modeled as `kind = "AIServices"` through the local `cognitive_services_account` wrapper, which calls the Azure Verified Module `Azure/avm-res-cognitiveservices-account/azurerm` version `0.11.0`.

The stage is infrastructure only. It does not implement product redaction logic, recommendation generation, recommendation review, prompt templates, evidence packets, or adapter behavior.

## Language PII And Content Safety

Azure AI Language PII detection is exposed as a non-secret endpoint contract for the inline redaction pipeline owned by the product implementation issues. The product must still run deterministic secret recognition before Azure AI Language and must not treat Azure AI Language as a general secret scanner.

Azure AI Content Safety is exposed for policy classification, Prompt Shields, protected material checks, and groundedness checks where policy requires them. Content Safety is not the redaction engine and must not be the only gate that decides whether content is safe to store.

## Model Alias Policy

Azure OpenAI or Foundry model deployments are keyed by product deployment aliases. Product logic must use aliases such as `recommendation-writer-primary`, not hardcoded model names.

`recommendation-writer-primary` is required only when `llm_assisted_recommendations_enabled = true`. `recommendation-writer-fallback` is optional and should be configured only when the Recommendation Model Policy allows fallback.

Exact model family, SKU, version, and capacity are supplied through `model_deployments` after regional capacity and structured-output support are validated. This stage records safe deployment references; it does not select a product model in application code.

## Identity And Access

The Azure AI Services account has system-assigned managed identity enabled and local authentication disabled. Runtime managed identity principal IDs can be supplied through `runtime_managed_identity_principal_ids`; the stage grants those principals `Azure AI User` at the account scope.

Do not output or store API keys, raw prompts, model request payloads, captured content, tool results, command output, secrets, or tenant-private data from this stage.

## Public And Private Access

Public network access remains enabled by default so the stage is deployable before exact private endpoint, subnet, and DNS requirements are supplied. Network ACLs, private endpoints, private DNS zone groups, and Foundry agent network injection are explicit inputs.

When private access is configured, keep private endpoint and DNS ownership explicit in the supplied variables. Do not infer subnet IDs, DNS zone IDs, or private endpoint names from unrelated local machine state.

## Downstream Outputs

The stage outputs only non-secret contracts:

- Azure AI Services account resource ID, name, endpoint, and managed identity principal ID.
- Model deployment aliases and deployment resource IDs.
- RAI policy IDs where configured.
- Language PII and Content Safety endpoint contracts.
- Private endpoint IDs when private endpoints are supplied.
- Diagnostic destination references.

## Provider And AVM Choices

New Azure resources follow the repository order: AVM through a local wrapper when suitable, AzureRM where no suitable AVM exists, and AzAPI only for provider gaps. The current wrapper uses the Cognitive Services AVM because it supports AI service accounts, cognitive deployments, diagnostics, private endpoints, and RAI policy resources.

Validation must use Terraform-native checks. Do not add .NET, xUnit, or C# tests for Terraform behavior.
