# Data Platform Stage

Responsibility: PostgreSQL Flexible Server, product Blob Storage, backup settings, and lifecycle settings.

Backend key: `data_platform.tfstate`

Local validation:

```bash
terraform init -backend=false
terraform validate
```

This stage is a skeleton only. It does not deploy Azure resources yet.
