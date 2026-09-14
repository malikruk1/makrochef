variable "name_prefix" {
  type = string
}

variable "vpc_cidr" {
  type = string
}

variable "azs" {
  description = "Availability zone names to spread public subnets across"
  type        = list(string)
}

variable "container_port" {
  type = number
}
