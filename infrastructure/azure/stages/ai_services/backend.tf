# Backend is intentionally inactive in the skeleton so local validation can run
# terraform init -backend=false, terraform validate, and terraform plan without
# requiring Azure remote state access.
#
# Production workflows must initialize the azurerm backend from the manually
# created Azure Blob remote state foundation. See backend.azurerm.tf.example.
