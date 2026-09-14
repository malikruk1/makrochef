# Separate stack/state on purpose (see infra/README.md): `terraform destroy` on the main infra/
# stack (ALB/ECS/RDS) - or swapping its origin from ALB to a Lambda Function URL later - never has
# to touch this distribution, so the public CloudFront domain the demo link points to never
# changes underneath it. The only coupling between the two stacks is var.origin_domain_name,
# passed in explicitly rather than read via a remote-state data source, so this stack's plan never
# depends on the main stack's state existing.

data "aws_cloudfront_cache_policy" "caching_disabled" {
  name = "Managed-CachingDisabled"
}

data "aws_cloudfront_origin_request_policy" "all_viewer_except_host" {
  name = "Managed-AllViewerExceptHostHeader"
}

locals {
  origin_id = "${var.project_name}-${var.environment}-origin"
}

resource "aws_cloudfront_distribution" "this" {
  enabled         = true
  is_ipv6_enabled = true
  price_class     = "PriceClass_100"
  comment         = "${var.project_name}-${var.environment} edge (fronts the ALB today, a Lambda Function URL later)"

  origin {
    origin_id   = local.origin_id
    domain_name = var.origin_domain_name

    custom_origin_config {
      http_port  = 80
      https_port = 443
      # MCP round trips (CandidatePoolBuilder/CoverageProbe) routinely take up to ~40s - the
      # default 30s origin_read_timeout would cut those responses off mid-request. 60 is the
      # highest value CloudFront allows without a support-ticket quota increase.
      origin_read_timeout      = 60
      origin_keepalive_timeout = 5
      origin_protocol_policy   = var.origin_protocol_policy
      origin_ssl_protocols     = ["TLSv1.2"]
    }
  }

  default_cache_behavior {
    target_origin_id = local.origin_id

    # All 7 HTTP methods - the API has real POST/PUT/DELETE-shaped writes (apply/reoptimize/
    # checkout/restrictions), not just GET reads a CDN would normally expect.
    allowed_methods = ["GET", "HEAD", "OPTIONS", "PUT", "POST", "PATCH", "DELETE"]
    cached_methods  = ["GET", "HEAD"]

    viewer_protocol_policy = "redirect-to-https"

    cache_policy_id          = data.aws_cloudfront_cache_policy.caching_disabled.id
    origin_request_policy_id = data.aws_cloudfront_origin_request_policy.all_viewer_except_host.id
  }

  restrictions {
    geo_restriction {
      restriction_type = "none"
    }
  }

  viewer_certificate {
    cloudfront_default_certificate = true
  }
}
