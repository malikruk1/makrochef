# Backend config for the dev AWS account - same S3 bucket as ../backend.dev.hcl, different key,
# so this stack's state is fully independent from the main infra/ stack's.
# Pass via: terraform init -backend-config=backend.dev.hcl
bucket       = "makrochef-tfstate-<ACCOUNT_ID>"
key          = "makrochef/edge.tfstate"
region       = "eu-central-1"
encrypt      = true
use_lockfile = true
profile      = "<AWS_PROFILE>"
