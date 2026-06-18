# Terraform Wrapper Modules

Stages call local wrapper modules from this directory. Wrapper modules may call Azure Verified Modules (AVM), AzureRM resources, or AzAPI resources according to the AVM-first selection order documented in `../README.md`.

Current data platform wrappers:

- `postgresql_flexible_server` wraps `Azure/avm-res-dbforpostgresql-flexibleserver/azurerm` version `0.2.2`.
- `storage_account` wraps `Azure/avm-res-storage-storageaccount/azurerm` version `0.7.2`.

Provider exceptions must be documented in the owning stage README before adding direct AzureRM or AzAPI resources.
