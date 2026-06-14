# AI Services Stage

Responsibility: Azure AI Language, Azure AI Content Safety, Azure OpenAI or Foundry resources, deployment aliases, and private access where feasible.

Backend key: `ai_services.tfstate`

Local validation:

```bash
terraform init -backend=false
terraform validate
```

This stage is a skeleton only. It does not deploy Azure resources yet.
