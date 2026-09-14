output "endpoint" {
  value = aws_db_instance.this.address
}

output "connection_string_ssm_arn" {
  description = "ARN of the SecureString the ECS task's `secrets` block reads ConnectionStrings__Postgres from"
  value       = aws_ssm_parameter.connection_string.arn
}
