output "app_url" {
  description = "Public URL of the deployed app (no custom domain - plain ALB DNS name, matches this session's decision to skip Route53/ACM)"
  value       = "http://${module.ecs.alb_dns_name}/web/app/index.html"
}

output "alb_dns_name" {
  value = module.ecs.alb_dns_name
}

output "ecr_repository_url" {
  description = "Push the image built from the repo root Dockerfile here before the first real deploy - `docker push <this>:latest`"
  value       = module.ecr.repository_url
}

output "ecs_cluster_name" {
  value = module.ecs.cluster_name
}

output "ecs_service_name" {
  value = module.ecs.service_name
}

output "ecs_task_definition_family" {
  description = "Used for the one-off `dotnet ... -- auth` task (see infra/README.md)"
  value       = module.ecs.task_definition_family
}

output "db_endpoint" {
  value = module.database.endpoint
}
