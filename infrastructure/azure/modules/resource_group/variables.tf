variable "location" {
  description = "Azure region for the resource group."
  type        = string
}

variable "name" {
  description = "Resource group name."
  type        = string
}

variable "tags" {
  description = "Tags assigned to the resource group."
  type        = map(string)
  default     = {}
}
