resource "aws_cloudwatch_log_group" "app" {
  name              = "/ecs/${var.name_prefix}"
  retention_in_days = var.log_retention_days
}

resource "aws_ecs_cluster" "this" {
  name = "${var.name_prefix}-cluster"

  setting {
    name  = "containerInsights"
    value = "disabled" # extra CloudWatch cost with no real value for a single-task demo service
  }
}

# ---- secrets the container needs, not baked into the task definition as plain text ----

resource "aws_ssm_parameter" "token_encryption_key" {
  name  = "/${var.name_prefix}/token_encryption_key"
  type  = "SecureString"
  value = var.token_encryption_key
}

resource "aws_ssm_parameter" "anthropic_api_key" {
  name = "/${var.name_prefix}/anthropic_api_key"
  type = "SecureString"
  # SSM SecureString values can't be empty; a single space keeps AnthropicClient's own
  # string.IsNullOrWhiteSpace check treating it exactly like "unset" (deterministic fallback).
  value = var.anthropic_api_key != "" ? var.anthropic_api_key : " "
}

# ---- IAM ----

data "aws_iam_policy_document" "ecs_assume" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["ecs-tasks.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "execution" {
  name               = "${var.name_prefix}-ecs-execution"
  assume_role_policy = data.aws_iam_policy_document.ecs_assume.json
}

resource "aws_iam_role_policy_attachment" "execution_managed" {
  role       = aws_iam_role.execution.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

# The managed policy above covers ECR pull + basic CloudWatch Logs - it does NOT cover reading the
# SSM SecureStrings the task definition's `secrets` block references, which the execution role
# (not the task role) fetches at container start.
data "aws_iam_policy_document" "execution_ssm" {
  statement {
    actions = ["ssm:GetParameters"]
    resources = [
      var.db_connection_string_ssm_arn,
      aws_ssm_parameter.token_encryption_key.arn,
      aws_ssm_parameter.anthropic_api_key.arn,
    ]
  }
}

resource "aws_iam_role_policy" "execution_ssm" {
  name   = "${var.name_prefix}-ecs-execution-ssm"
  role   = aws_iam_role.execution.id
  policy = data.aws_iam_policy_document.execution_ssm.json
}

resource "aws_iam_role" "task" {
  name               = "${var.name_prefix}-ecs-task"
  assume_role_policy = data.aws_iam_policy_document.ecs_assume.json
}

# ---- task definition ----

resource "aws_ecs_task_definition" "this" {
  family                   = "${var.name_prefix}-api"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = var.task_cpu
  memory                   = var.task_memory
  execution_role_arn       = aws_iam_role.execution.arn
  task_role_arn            = aws_iam_role.task.arn

  container_definitions = jsonencode([
    {
      name      = "api"
      image     = var.container_image
      essential = true

      portMappings = [{
        containerPort = var.container_port
        protocol      = "tcp"
      }]

      environment = [
        { name = "ASPNETCORE_URLS", value = "http://0.0.0.0:${var.container_port}" },
        { name = "ASPNETCORE_ENVIRONMENT", value = "Production" },
        { name = "MCP_BASE_URI", value = var.mcp_base_uri },
        { name = "DEV_USER_ID", value = var.dev_user_id },
      ]

      # ConnectionStrings__Postgres feeds the running web app (builder.Configuration.GetConnectionString
      # ("Postgres")); POSTGRES_CONNECTION_STRING feeds the `auth`/`diag`/`probe` CLI commands when
      # this task definition is run with an overridden command (see infra/README.md) - both need the
      # same value, so both point at the same SSM parameter.
      secrets = [
        { name = "ConnectionStrings__Postgres", valueFrom = var.db_connection_string_ssm_arn },
        { name = "POSTGRES_CONNECTION_STRING", valueFrom = var.db_connection_string_ssm_arn },
        { name = "TOKEN_ENCRYPTION_KEY", valueFrom = aws_ssm_parameter.token_encryption_key.arn },
        { name = "ANTHROPIC_API_KEY", valueFrom = aws_ssm_parameter.anthropic_api_key.arn },
      ]

      logConfiguration = {
        logDriver = "awslogs"
        options = {
          "awslogs-group"         = aws_cloudwatch_log_group.app.name
          "awslogs-region"        = data.aws_region.current.region
          "awslogs-stream-prefix" = "api"
        }
      }
    }
  ])
}

data "aws_region" "current" {}

# ---- load balancer ----

resource "aws_lb" "this" {
  name               = "${var.name_prefix}-alb"
  internal           = false
  load_balancer_type = "application"
  security_groups    = [var.alb_security_group_id]
  subnets            = var.subnet_ids
}

resource "aws_lb_target_group" "this" {
  name        = "${var.name_prefix}-tg"
  port        = var.container_port
  protocol    = "HTTP"
  vpc_id      = var.vpc_id
  target_type = "ip" # required for awsvpc network mode (Fargate) - there's no EC2 instance to register

  health_check {
    path                = "/health"
    healthy_threshold   = 2
    unhealthy_threshold = 3
    interval            = 15
    timeout             = 5
  }
}

resource "aws_lb_listener" "http" {
  load_balancer_arn = aws_lb.this.arn
  port              = 80
  protocol          = "HTTP"

  default_action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.this.arn
  }
}

# ---- service ----

resource "aws_ecs_service" "this" {
  name            = "${var.name_prefix}-api"
  cluster         = aws_ecs_cluster.this.id
  task_definition = aws_ecs_task_definition.this.arn
  desired_count   = var.desired_count
  launch_type     = "FARGATE"

  network_configuration {
    subnets          = var.subnet_ids
    security_groups  = [var.ecs_security_group_id]
    assign_public_ip = true # no NAT Gateway (modules/networking) - the task needs its own public IP to reach MCP Silpo and pull its ECR image
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.this.arn
    container_name   = "api"
    container_port   = var.container_port
  }

  depends_on = [aws_lb_listener.http]
}
