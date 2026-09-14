resource "random_password" "master" {
  length  = 24
  special = false # Npgsql connection strings choke on ';'/'=' in the password without escaping - keep it alphanumeric
}

resource "aws_db_subnet_group" "this" {
  name       = "${var.name_prefix}-db"
  subnet_ids = var.subnet_ids

  tags = { Name = "${var.name_prefix}-db-subnet-group" }
}

resource "aws_db_instance" "this" {
  identifier     = "${var.name_prefix}-db"
  engine         = "postgres"
  engine_version = "16"

  instance_class         = var.instance_class
  allocated_storage      = var.allocated_storage_gb
  storage_type           = "gp3"
  db_name                = var.db_name
  username               = var.username
  password               = random_password.master.result
  port                   = 5432
  db_subnet_group_name   = aws_db_subnet_group.this.name
  vpc_security_group_ids = [var.security_group_id]

  # Hackathon-simple, not production: single instance, no read replica, public subnet (but locked
  # to the ECS security group only - see modules/networking's aws_security_group.rds).
  multi_az                  = false
  publicly_accessible       = false
  deletion_protection       = var.deletion_protection
  skip_final_snapshot       = var.skip_final_snapshot
  final_snapshot_identifier = var.skip_final_snapshot ? null : "${var.name_prefix}-db-final"
  backup_retention_period   = 1

  tags = { Name = "${var.name_prefix}-db" }
}

# Full Npgsql connection string as one SecureString, matching how the app's own
# ConnectionStrings__Postgres env var / appsettings key already expects it - the ECS task
# definition wires this straight in via a `secrets` entry, never a plain environment variable.
resource "aws_ssm_parameter" "connection_string" {
  name  = "/${var.name_prefix}/db_connection_string"
  type  = "SecureString"
  value = "Host=${aws_db_instance.this.address};Port=5432;Database=${var.db_name};Username=${var.username};Password=${random_password.master.result}"

  tags = { Name = "${var.name_prefix}-db-connection-string" }
}
