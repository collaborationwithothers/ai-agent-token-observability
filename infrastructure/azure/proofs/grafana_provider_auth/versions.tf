terraform {
  required_version = "~> 1.14.0"

  required_providers {
    grafana = {
      source  = "grafana/grafana"
      version = "4.39.0"
    }
  }
}

