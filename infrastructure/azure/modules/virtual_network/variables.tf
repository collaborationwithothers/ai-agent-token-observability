variable "address_space" {
  description = "Address space assigned to the virtual network."
  type        = list(string)
}

variable "location" {
  description = "Azure region for the virtual network."
  type        = string
}

variable "name" {
  description = "Virtual network name."
  type        = string
}

variable "parent_id" {
  description = "Resource group ID where the virtual network is created."
  type        = string
}

variable "subnets" {
  description = "Subnet definitions keyed by stable downstream contract key."
  type = map(object({
    address_prefix                                = optional(string)
    address_prefixes                              = optional(list(string))
    name                                          = string
    network_security_group                        = optional(object({ id = string }))
    private_link_service_network_policies_enabled = optional(bool, true)
    service_endpoints_with_location               = optional(list(object({ service = string, locations = optional(list(string), ["*"]) })))
    delegations                                   = optional(list(object({ name = string, service_delegation = object({ name = string }) })))
    default_outbound_access_enabled               = optional(bool, false)
  }))
  default = {}
}

variable "tags" {
  description = "Tags assigned to the virtual network."
  type        = map(string)
  default     = {}
}
