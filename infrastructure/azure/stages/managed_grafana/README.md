# Managed Grafana Stage

Responsibility: Azure Managed Grafana workspace, data source wiring, repo-versioned dashboard JSON deployment, Grafana folders, and Grafana role integration.

Backend key: `managed_grafana.tfstate`

Local validation:

```bash
terraform init -backend=false
terraform validate
```

This stage is a skeleton only. It does not deploy Azure resources yet.
