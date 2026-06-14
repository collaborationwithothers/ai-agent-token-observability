# Network Private Data Plane Stage

Responsibility: VNet, subnets, private DNS zones, private endpoints, and network security boundaries.

Backend key: `network_private_data_plane.tfstate`

Local validation:

```bash
terraform init -backend=false
terraform validate
```

This stage is a skeleton only. It does not deploy Azure resources yet.
