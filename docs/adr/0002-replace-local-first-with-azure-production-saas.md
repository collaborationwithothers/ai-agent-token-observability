# Replace Local-First With Azure Production SaaS

Status: accepted

The project will replace the Local-First MVP direction with an Azure Production MVP and a Multi-Tenant SaaS Target State. The Azure Production MVP is a tenant-aware Single-Enterprise Release hosted on Azure, Codex CLI is the first production harness, local execution is developer convenience only, and Terraform with Azure Blob Storage remote state is the infrastructure path. The target state is a vendor-operated multi-tenant SaaS platform using Customer Organizations, federated customer identity, product role mappings, Azure Container Apps, Azure Managed Grafana, a product ingestion endpoint, separated observability and product stores, pre-storage content redaction, non-punitive optimization, and guarded public-repository deployment workflows.

This supersedes ADR 0001 because the product is no longer proving a local-only application shape before Azure deployment. Keeping the old local-first decision would cause the PRD, architecture docs, GitHub issues, and implementation plan to optimize for the wrong release boundary.
