# S3 backend with native lockfiles (use_lockfile, Terraform >= 1.10 — no DynamoDB).
# The bucket is created by infra/bootstrap; set it here after that apply, or pass
# `-backend-config="bucket=<name>"` at `terraform init`.
terraform {
  backend "s3" {
    key          = "infra/terraform.tfstate"
    region       = "us-east-1"
    encrypt      = true
    use_lockfile = true
    # bucket = "protofast-tfstate"   # ← set to the bootstrap output, or pass via -backend-config
  }
}
