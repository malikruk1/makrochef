module "networking" {
  source = "./modules/networking"

  name_prefix    = local.name_prefix
  vpc_cidr       = var.vpc_cidr
  azs            = local.azs
  container_port = var.container_port
}

module "database" {
  source = "./modules/database"

  name_prefix          = local.name_prefix
  subnet_ids           = module.networking.public_subnet_ids
  security_group_id    = module.networking.rds_security_group_id
  instance_class       = var.db_instance_class
  allocated_storage_gb = var.db_allocated_storage_gb
  db_name              = var.db_name
  username             = var.db_username
  deletion_protection  = var.db_deletion_protection
  skip_final_snapshot  = var.db_skip_final_snapshot
}

module "ecr" {
  source = "./modules/ecr"

  name_prefix = local.name_prefix
}

module "ecs" {
  source = "./modules/ecs"

  name_prefix                  = local.name_prefix
  vpc_id                       = module.networking.vpc_id
  subnet_ids                   = module.networking.public_subnet_ids
  alb_security_group_id        = module.networking.alb_security_group_id
  ecs_security_group_id        = module.networking.ecs_security_group_id
  container_image              = coalesce(var.container_image, "${module.ecr.repository_url}:latest")
  container_port               = var.container_port
  task_cpu                     = var.task_cpu
  task_memory                  = var.task_memory
  desired_count                = var.desired_count
  log_retention_days           = var.log_retention_days
  db_connection_string_ssm_arn = module.database.connection_string_ssm_arn
  token_encryption_key         = var.token_encryption_key
  anthropic_api_key            = var.anthropic_api_key
  mcp_base_uri                 = var.mcp_base_uri
  dev_user_id                  = var.dev_user_id
}
