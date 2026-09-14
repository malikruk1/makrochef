# MakroChef backend — Terraform stack

Provisions the AWS resources needed to run MakroChef publicly, in the same style as
[krok-backend](https://github.com/strayFabler/krok-backend)'s `infra/` (modules + S3 remote state +
per-environment tfvars/backend config), adapted to what this app actually needs:

- **VPC** — 2 public subnets across 2 AZs, no NAT Gateway (the ECS task gets its own public IP
  instead — see `modules/networking`'s notes on why that tradeoff is fine for a hackathon budget).
- **ECS Fargate** — runs the repo-root `Dockerfile` as one task behind an ALB. Chosen over Lambda
  (krok's approach) because MakroChef's real MCP calls routinely take 20-40s
  (`CandidatePoolBuilder`/`CoverageProbe`), which risks API Gateway's ~29-30s hard integration
  timeout — a long-running container has no such ceiling.
- **RDS Postgres** — single `db.t4g.micro` instance (EF Core's own `Database.MigrateAsync()` at
  startup applies migrations automatically, same as the Docker Compose local setup).
- **ECR** — image repository; **not** built/pushed by Terraform itself, see "Deploy" below.
- **SSM Parameter Store (SecureString)** — `TOKEN_ENCRYPTION_KEY`, `ANTHROPIC_API_KEY`, and the full
  Postgres connection string are never plain environment variables in the task definition, only
  `secrets` entries the ECS execution role fetches at container start.

**Deliberately skipped** (unlike krok): Cognito and a custom domain (Route53 + ACM). MakroChef has
no multi-user auth of its own — it's scoped to one MCP-authenticated Silpo account
(`DEV_USER_ID`) — and a plain ALB DNS name is enough for a hackathon demo link.

## Layout

```
infra/
  provider.tf              AWS/random providers, S3 backend declaration
  variables.tf              All inputs (region, sizing, names, env, secrets)
  data.tf                   caller_identity, region, azs, name_prefix locals
  main.tf                   Wires the four modules together
  outputs.tf                app_url, ecr_repository_url, ecs_*, db_endpoint
  backend.dev.hcl           Backend config for the dev account (fill in placeholders)
  dev.tfvars.example        Copy to dev.tfvars (gitignored) and fill in secrets
  backend/                  One-time bootstrap of the S3 state bucket
  modules/
    networking/             VPC, public subnets, security groups
    database/                RDS Postgres + connection-string SSM parameter
    ecr/                     Image repository
    ecs/                     Cluster, task definition, service, ALB, IAM, logs
```

## One-time: bootstrap remote state

Run once per AWS account:

```bash
cd infra/backend
terraform init
terraform apply -var aws_profile=<your-profile>
```

Note the `bucket_name` output — plug the real account id into `backend.dev.hcl`'s `bucket` field
(the bucket name is `makrochef-tfstate-<account-id>`).

## Deploy the stack

```bash
cd infra
cp dev.tfvars.example dev.tfvars   # fill in token_encryption_key (openssl rand -base64 32) and aws_profile
terraform init -backend-config=backend.dev.hcl
terraform plan  -var-file=dev.tfvars
terraform apply -var-file=dev.tfvars
```

The first `apply` creates an **empty** ECR repository — the ECS service will fail to start tasks
until a real image exists at `<ecr_repository_url>:latest`. Build and push it from the repo root:

```bash
aws ecr get-login-password --region eu-central-1 --profile <your-profile> \
  | docker login --username AWS --password-stdin <ecr_repository_url_without_tag>

docker build -t <ecr_repository_url>:latest ..   # from infra/, so the root Dockerfile is the context's parent
docker push <ecr_repository_url>:latest
```

(`terraform output ecr_repository_url` gets you the exact URL.) Force a fresh deployment of the new
image without a config change:

```bash
aws ecs update-service --cluster $(terraform output -raw ecs_cluster_name) \
  --service $(terraform output -raw ecs_service_name) --force-new-deployment \
  --profile <your-profile>
```

## Authenticate the one guest account

The app is scoped to a single MCP-authenticated Silpo account (`DEV_USER_ID`) — someone has to run
the interactive phone/SMS OAuth flow once, against the *deployed* database, before `/api/basket`
and friends can do anything. The task definition's container normally just runs the web server, so
override its command for a one-off run:

```bash
aws ecs run-task \
  --cluster $(terraform output -raw ecs_cluster_name) \
  --task-definition $(terraform output -raw ecs_task_definition_family) \
  --launch-type FARGATE \
  --network-configuration "awsvpcConfiguration={subnets=[<public-subnet-id>],securityGroups=[<ecs-sg-id>],assignPublicIp=ENABLED}" \
  --overrides '{"containerOverrides":[{"name":"api","command":["dotnet","MakroChef.Api.dll","--","auth"]}]}' \
  --profile <your-profile>
```

Then watch its CloudWatch Logs stream (`/ecs/makrochef-dev`) for the `/authorize` link, open it,
and complete the phone/SMS login — same flow as the local `dotnet run -- auth`.

## Outputs

| Output | Meaning |
|---|---|
| `app_url` | Full URL to the app's `index.html` behind the ALB |
| `alb_dns_name` | Bare ALB DNS name |
| `ecr_repository_url` | Push images here |
| `ecs_cluster_name` / `ecs_service_name` | For `aws ecs update-service` / `run-task` |
| `ecs_task_definition_family` | For the one-off `auth` task above |
| `db_endpoint` | RDS address (not publicly reachable - only from the ECS security group) |

## Notes / honest limitations

- **`desired_count` defaults to 1.** The app assumes a single MCP-authenticated guest
  (`DEV_USER_ID`) and a single `EfMcpTokenStore` row — running more than one task would have them
  compete over the same OAuth token refresh, not serve more users.
- **No auto-scaling, no HTTPS listener.** Out of scope for a hackathon demo link; would need an ACM
  cert + a real domain (the exact thing this stack deliberately skips) to add an HTTPS listener.
  A load balancer log or `curl -I` audit tool would flag this HTTP-only setup for a project handling
  real user data in a stricter deployment - not the case here (single hackathon demo account).
- **No NAT Gateway.** The ECS task sits in a public subnet with its own public IP instead — cheaper,
  and fine since the task itself (not some private backend it fronts) is the thing that needs
  internet egress to reach `mcp.silpo.ua`.
