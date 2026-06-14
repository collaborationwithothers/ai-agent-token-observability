# Terraform Wrapper Modules

Production resource modules will live here.

Stages must call local wrapper modules from this directory. Wrapper modules may call Azure Verified Modules (AVM), AzureRM resources, or AzAPI resources according to the AVM-first selection order documented in `../README.md`.

This issue does not add resource modules because it creates validation-only stage skeletons.
