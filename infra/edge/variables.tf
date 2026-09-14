variable "aws_region" {
  description = "AWS region for the provider (CloudFront itself is global; this only affects where the distribution resource's API calls are made from)"
  type        = string
  default     = "eu-central-1"
}

variable "aws_profile" {
  description = "AWS named profile used for credentials"
  type        = string
  default     = null
}

variable "project_name" {
  description = "Project prefix used for tagging (kept separate from the main stack's state on purpose)"
  type        = string
  default     = "makrochef"
}

variable "environment" {
  description = "Deployment environment (dev, staging, prod)"
  type        = string
  default     = "dev"
}

variable "origin_domain_name" {
  description = "DNS name of the origin CloudFront forwards to - the main stack's ALB DNS name today (`terraform output alb_dns_name` in infra/), a Lambda Function URL host later. This stack never reads the main stack's state directly, so this is passed in explicitly rather than wired via a remote-state data source - keeps the two stacks destroyable independently without one's plan depending on the other's state existing."
  type        = string
}

variable "origin_protocol_policy" {
  description = "How CloudFront talks to the origin. \"http-only\" matches today's ALB (HTTP :80 only, no ACM cert on it); switch to \"https-only\" if the origin ever gets its own TLS listener."
  type        = string
  default     = "http-only"
}
