variable "location" {
  description = "Azure region for the Network Security Group."
  type        = string
}

variable "name" {
  description = "Network Security Group name."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group name for the Network Security Group."
  type        = string
}

variable "security_rules" {
  description = "Security rules keyed by stable rule identifier."
  type = map(object({
    access                       = string
    description                  = optional(string)
    destination_address_prefix   = optional(string)
    destination_address_prefixes = optional(set(string))
    destination_port_range       = optional(string)
    destination_port_ranges      = optional(set(string))
    direction                    = string
    name                         = string
    priority                     = number
    protocol                     = string
    source_address_prefix        = optional(string)
    source_address_prefixes      = optional(set(string))
    source_port_range            = optional(string)
    source_port_ranges           = optional(set(string))
  }))
  default = {}
}

variable "tags" {
  description = "Tags assigned to the Network Security Group."
  type        = map(string)
  default     = {}
}
