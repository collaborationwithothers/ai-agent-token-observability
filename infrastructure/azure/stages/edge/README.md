# Edge Stage

Responsibility: Azure Front Door Premium, WAF policy, routes, Private Link origins, managed certificates, custom domains, and rate-limit rules where supported.

Backend key: `edge.tfstate`

Local validation:

```bash
terraform init -backend=false
terraform validate
```

This stage is a skeleton only. It does not deploy Azure resources yet.
