variable "aws_region" {
  description = "AWS region for all resources"
  type        = string
  default     = "eu-central-1"
}

variable "aws_profile" {
  description = "AWS named profile used for credentials"
  type        = string
  default     = null
}

variable "project_name" {
  description = "Project prefix used for resource names and tagging"
  type        = string
  default     = "makrochef"
}

variable "environment" {
  description = "Deployment environment (dev, staging, prod)"
  type        = string
  default     = "dev"
}

# ---- networking ----

variable "vpc_cidr" {
  description = "CIDR block for the VPC"
  type        = string
  default     = "10.42.0.0/16"
}

variable "az_count" {
  description = "Number of availability zones to spread public subnets across"
  type        = number
  default     = 2
}

# ---- container image ----

variable "container_image" {
  description = "Full image URI the ECS task pulls (e.g. <ecr_repo_url>:latest). Push the image with the repo's own Dockerfile before the first `terraform apply` that references a real tag - the ECR module creates the repo but does not build/push into it."
  type        = string
  default     = null
}

variable "container_port" {
  description = "Port the API listens on inside the container (matches the Dockerfile's ASPNETCORE_URLS)"
  type        = number
  default     = 8080
}

# ---- ECS sizing ----

variable "task_cpu" {
  description = "Fargate task vCPU units (256 = 0.25 vCPU). CP-SAT solves are short but the candidate-pool build is I/O bound, not CPU bound, so the smallest size is usually enough."
  type        = number
  default     = 512
}

variable "task_memory" {
  description = "Fargate task memory in MB"
  type        = number
  default     = 1024
}

variable "desired_count" {
  description = "Number of running ECS tasks. Keep at 1 for a hackathon demo - the app is single-dev-user, not built for concurrent instances sharing one MCP token."
  type        = number
  default     = 1
}

variable "log_retention_days" {
  description = "CloudWatch Logs retention for the ECS task's log group"
  type        = number
  default     = 14
}

# ---- database ----

variable "db_instance_class" {
  description = "RDS instance class"
  type        = string
  default     = "db.t4g.micro"
}

variable "db_allocated_storage_gb" {
  description = "RDS allocated storage in GB"
  type        = number
  default     = 20
}

variable "db_name" {
  description = "Postgres database name (matches EF Core's connection string)"
  type        = string
  default     = "makrochef"
}

variable "db_username" {
  description = "Postgres master username"
  type        = string
  default     = "postgres"
}

variable "db_deletion_protection" {
  description = "Enable deletion protection on the RDS instance"
  type        = bool
  default     = false
}

variable "db_skip_final_snapshot" {
  description = "Skip the final snapshot on destroy - true is fine for an ephemeral hackathon demo, false for anything meant to survive `terraform destroy`"
  type        = bool
  default     = true
}

# ---- app config (SSM SecureString - never plain tfvars, never echoed to state as a plain output) ----

variable "token_encryption_key" {
  description = "AES key MakroChef.Mcp.OAuth.TokenEncryptor uses to encrypt the stored MCP OAuth token at rest. Generate one with: openssl rand -base64 32"
  type        = string
  sensitive   = true
}

variable "anthropic_api_key" {
  description = "Optional Claude API key for real (non-fallback) swap explanations and restriction translation. Leave empty to run on the deterministic fallback - the app is fully functional either way."
  type        = string
  sensitive   = true
  default     = ""
}

variable "mcp_base_uri" {
  description = "MCP Silpo endpoint"
  type        = string
  default     = "https://mcp.silpo.ua/mcp"
}

variable "dev_user_id" {
  description = "Single dev-user id this whole app is scoped to (TASKS.md: no multi-account support)"
  type        = string
  default     = "00000000-0000-0000-0000-000000000001"
}
