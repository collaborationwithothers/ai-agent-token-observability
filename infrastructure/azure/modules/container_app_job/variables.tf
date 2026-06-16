variable "container_app_environment_resource_id" {
  description = "Resource ID of the Azure Container Apps environment that hosts the job."
  type        = string
}

variable "key_vault_secrets" {
  description = "Key Vault backed secrets exposed to the job. Plain secret values are intentionally not supported by this wrapper."
  type = list(object({
    name                = string
    identity            = string
    key_vault_secret_id = string
  }))
  default = []
}

variable "location" {
  description = "Azure region for the Container Apps Job."
  type        = string
}

variable "managed_identities" {
  description = "Managed identity configuration for the Container Apps Job."
  type = object({
    system_assigned            = optional(bool, false)
    user_assigned_resource_ids = optional(set(string), [])
  })
  default = {}
}

variable "name" {
  description = "Container Apps Job name."
  type        = string
}

variable "registries" {
  description = "Container registries used by the Container Apps Job."
  type = list(object({
    identity             = optional(string)
    password_secret_name = optional(string)
    server               = string
    username             = optional(string)
  }))
  default = []
}

variable "replica_retry_limit" {
  description = "Maximum retry count before an execution is considered failed."
  type        = number
  default     = null
}

variable "replica_timeout_in_seconds" {
  description = "Timeout in seconds for a job execution."
  type        = number
  default     = 300
}

variable "resource_group_name" {
  description = "Resource group name for the Container Apps Job."
  type        = string
}

variable "tags" {
  description = "Tags assigned to the Container Apps Job."
  type        = map(string)
  default     = {}
}

variable "template" {
  description = "Container Apps Job template, including the explicit command and arguments."
  type = object({
    max_replicas = optional(number)
    min_replicas = optional(number)
    container = object({
      name    = string
      image   = string
      cpu     = number
      memory  = string
      command = optional(list(string))
      args    = optional(list(string))
      env = optional(list(object({
        name        = string
        secret_name = optional(string)
        value       = optional(string)
      })))
      liveness_probe = optional(list(object({
        port                    = number
        transport               = string
        failure_count_threshold = number
        period                  = number
        header = optional(list(object({
          name  = string
          value = string
        })))
        host             = optional(string)
        initial_delay    = optional(number)
        interval_seconds = optional(number)
        path             = optional(string)
        timeout          = optional(number)
      })))
      readiness_probe = optional(list(object({
        port                    = number
        transport               = string
        failure_count_threshold = number
        header = optional(list(object({
          name  = string
          value = string
        })))
        host                    = optional(string)
        interval_seconds        = optional(number)
        path                    = optional(string)
        success_count_threshold = optional(number)
        timeout                 = optional(number)
      })))
      startup_probe = optional(list(object({
        port                    = number
        transport               = string
        failure_count_threshold = number
        header = optional(list(object({
          name  = string
          value = string
        })))
        host             = optional(string)
        interval_seconds = optional(number)
        path             = optional(string)
        timeout          = optional(number)
      })))
      volume_mounts = optional(list(object({
        name = string
        path = string
      })))
    })
    init_container = optional(list(object({
      name    = string
      image   = string
      cpu     = number
      memory  = string
      command = list(string)
      args    = list(string)
      env = optional(list(object({
        name        = string
        secret_name = optional(string)
        value       = optional(string)
      })))
      volume_mounts = optional(list(object({
        name = string
        path = string
      })))
    })))
    volume = optional(list(object({
      name         = optional(string)
      storage_type = optional(string)
      storage_name = optional(string)
    })))
  })
}

variable "trigger_config" {
  description = "Container Apps Job trigger configuration."
  type = object({
    manual_trigger_config = optional(object({
      parallelism              = optional(number)
      replica_completion_count = optional(number)
    }))
    event_trigger_config = optional(object({
      parallelism              = optional(number)
      replica_completion_count = optional(number)
      scale = optional(object({
        max_executions              = optional(number)
        min_executions              = optional(number)
        polling_interval_in_seconds = optional(number)
        rules = optional(list(object({
          name             = optional(string)
          custom_rule_type = optional(string)
          metadata         = optional(map(string))
          authentication = optional(list(object({
            secret_name       = optional(string)
            trigger_parameter = optional(string)
          })))
        })))
      }))
    }))
    schedule_trigger_config = optional(object({
      cron_expression          = optional(string)
      parallelism              = optional(number)
      replica_completion_count = optional(number)
    }))
  })
  default = {
    manual_trigger_config = {
      parallelism              = 1
      replica_completion_count = 1
    }
  }
}

variable "workload_profile_name" {
  description = "Workload profile name for the Container Apps Job."
  type        = string
  default     = null
}
