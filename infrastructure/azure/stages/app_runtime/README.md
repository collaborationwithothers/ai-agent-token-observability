# App Runtime Stage

Responsibility: Container Apps environment, Product API, Product Ingestion Endpoint, Product Dashboard, Container Apps Jobs, managed identities, and app configuration.

Backend key: `app_runtime.tfstate`

Local validation:

```bash
terraform init -backend=false
terraform validate
```

This stage is a skeleton only. It does not deploy Azure resources yet.
