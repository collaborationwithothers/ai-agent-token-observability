locals {
  grafana_dashboard_artifacts = {
    executive_cost_overview = {
      title = "Executive Cost Overview"
      uid   = "tokenobs-executive-cost-overview"
      path  = "${path.root}/../../../grafana/dashboards/executive-cost-overview.json"
    }
    harness_model_operations = {
      title = "Harness And Model Operations"
      uid   = "tokenobs-harness-model-operations"
      path  = "${path.root}/../../../grafana/dashboards/harness-and-model-operations.json"
    }
    cache_hotspot_trends = {
      title = "Cache And Hotspot Trends"
      uid   = "tokenobs-cache-hotspot-trends"
      path  = "${path.root}/../../../grafana/dashboards/cache-and-hotspot-trends.json"
    }
    ingestion_platform_health = {
      title = "Ingestion And Platform Health"
      uid   = "tokenobs-ingestion-platform-health"
      path  = "${path.root}/../../../grafana/dashboards/ingestion-and-platform-health.json"
    }
  }
}

resource "grafana_folder" "token_observability" {
  title = "Token Observability"
  uid   = "tokenobs"

  depends_on = [
    module.managed_grafana,
  ]
}

resource "grafana_dashboard" "first_release" {
  for_each = local.grafana_dashboard_artifacts

  folder      = grafana_folder.token_observability.uid
  config_json = file(each.value.path)

  lifecycle {
    precondition {
      condition     = jsondecode(file(each.value.path)).uid == each.value.uid
      error_message = "Dashboard artifact UID must match the managed Grafana architecture contract."
    }

    precondition {
      condition     = jsondecode(file(each.value.path)).title == each.value.title
      error_message = "Dashboard artifact title must match the managed Grafana architecture contract."
    }
  }
}
