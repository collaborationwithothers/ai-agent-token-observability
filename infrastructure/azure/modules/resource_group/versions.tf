terraform {
  required_version = "~> 1.14.0"

  required_providers {
    azapi = {
      source  = "Azure/azapi"
      version = ">= 2.4.0, < 3.0.0"
    }
  }
}
