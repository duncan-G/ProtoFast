# GitHub Actions OIDC provider — lets workflows assume AWS roles with no stored
# long-lived keys. The two roles (roles.tf) trust this provider, scoped to this
# repo via the `sub` claim.

data "aws_caller_identity" "current" {}

# Compute the provider's TLS thumbprint dynamically instead of pinning a literal.
data "tls_certificate" "github_actions" {
  url = "https://token.actions.githubusercontent.com/.well-known/openid-configuration"
}

resource "aws_iam_openid_connect_provider" "github_actions" {
  url             = "https://token.actions.githubusercontent.com"
  client_id_list  = ["sts.amazonaws.com"]
  thumbprint_list = [data.tls_certificate.github_actions.certificates[0].sha1_fingerprint]
}
