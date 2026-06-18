variable "domain_name" {
  description = "Private DNS zone domain name."
  type        = string
}

variable "parent_id" {
  description = "Resource group ID where the private DNS zone is created."
  type        = string
}

variable "tags" {
  description = "Tags assigned to the private DNS zone."
  type        = map(string)
  default     = {}
}

variable "virtual_network_links" {
  description = "Virtual network links for the private DNS zone."
  type = map(object({
    name                 = optional(string)
    virtual_network_id   = optional(string)
    registration_enabled = optional(bool, false)
    resolution_policy    = optional(string, "Default")
    tags                 = optional(map(string))
  }))
  default = {}
}
