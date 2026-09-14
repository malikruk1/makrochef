# Backend config for the dev AWS account.
# Bucket is created by `infra/backend/` (run there once with admin creds in the target account).
# State locking uses S3 native lockfile (Terraform 1.10+) - no DynamoDB lock table needed.
# Pass via: terraform init -backend-config=backend.dev.hcl
#
# Replace <ACCOUNT_ID> with the real account id (infra/backend's output "bucket_name" has it)
# and <AWS_PROFILE> with the named profile you configured in ~/.aws/credentials, or delete the
# profile line entirely to fall back to the default credential chain (env vars, instance role, ...).
bucket       = "makrochef-tfstate-655960185938"
key          = "makrochef/terraform.tfstate"
region       = "eu-central-1"
encrypt      = true
use_lockfile = true
profile      = "krok-dev"
