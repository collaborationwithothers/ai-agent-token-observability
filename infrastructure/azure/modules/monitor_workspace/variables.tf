variable "location" {
  description = "Azure region for the Azure Monitor workspace."
  type        = string
}

variable "name" {
  description = "Azure Monitor workspace name."
  type        = string
}

variable "public_network_access_enabled" {
  description = "Whether public network access is enabled for the Azure Monitor workspace."
  type        = bool
  default     = false
}

variable "resource_group_name" {
  description = "Resource group name for the Azure Monitor workspace."
  type        = string
}

variable "tags" {
  description = "Tags assigned to the Azure Monitor workspace."
  type        = map(string)
  default     = {}
}
