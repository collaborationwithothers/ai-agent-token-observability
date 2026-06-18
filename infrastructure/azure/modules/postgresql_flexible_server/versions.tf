terraform {
  required_version = "~> 1.14.0"

  required_providers {
    azapi = {
      source  = "Azure/azapi"
      version = ">= 2.8.0, < 3.0.0"
    }

    azurerm = {
      source  = "hashicorp/azurerm"
      version = ">= 4.37.0, < 5.0.0"
    }
  }
}
