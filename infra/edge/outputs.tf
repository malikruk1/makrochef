output "app_url" {
  description = "Public HTTPS URL for the app - this is the stable link to hand out (survives a `terraform destroy` of the main infra/ stack, or an origin swap to a Lambda Function URL)"
  value       = "https://${aws_cloudfront_distribution.this.domain_name}/web/app/index.html"
}

output "distribution_id" {
  description = "For `aws cloudfront create-invalidation` after a deploy, or updating origin_domain_name in place"
  value       = aws_cloudfront_distribution.this.id
}

output "distribution_domain_name" {
  value = aws_cloudfront_distribution.this.domain_name
}
