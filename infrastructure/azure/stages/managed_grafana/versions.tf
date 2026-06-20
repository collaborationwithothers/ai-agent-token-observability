terraform {
  required_version = "~> 1.14.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = ">= 4.77.0, < 5.0.0"
    }

    grafana = {
      source  = "grafana/grafana"
      version = "4.39.0"
    }
  }
}
