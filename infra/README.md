# Infra

Production AWS + Cloudflare infrastructure for ProtoFast. There are three Terraform roots: the first two are applied once by a human, then GitHub Actions owns this directory.

| Root                                 | When                 | Applied by                                       | State                          |
| ------------------------------------ | -------------------- | ------------------------------------------------ | ------------------------------ |
| [bootstrap/](bootstrap/)             | Once, first          | Genesis identity (root or a temporary IAM admin) | Local disk (gitignored)        |
| [identity-center/](identity-center/) | Once, then as needed | Genesis for the first apply, **OrgAdmin** after  | S3 bucket created by bootstrap |
| `infra/` (this directory)            | Ongoing              | GitHub Actions (`infra.yml`) via OIDC            | Same S3 bucket                 |

## 1. Bootstrap

A one-time local apply that creates everything CI depends on: the S3 state bucket, the GitHub OIDC provider, the `protofast-infra` and `protofast-deploy` roles, and the permissions boundary. It also writes the GitHub Actions variables and secrets CI needs (OIDC role ARNs, ECR, Cloudflare inputs).

Follow [bootstrap/README.md](bootstrap/README.md) for the genesis-identity tfvars, Cloudflare token scopes, and the apply. Keep that shell open — Identity Center's first apply uses the same credentials.

## 2. Identity Center

Creates the SSO groups (`Org-Admins`, `Platform-Admins`, `Developers`), their permission sets, and account assignments. People are added to groups in the console, not in Terraform. This root also owns the only IAM user in the account — the SES SMTP sender (section 4.2) — because the permissions boundary blocks every other applier from creating users.

Follow [identity-center/README.md](identity-center/README.md): apply first as genesis, create yourself as OrgAdmin, sign in, then delete the genesis identity.

## 3. Deploy on GitHub Actions

Create a GitHub Environment named `infra` with required reviewers. `infra.yml` declares `environment: infra`, and the OIDC role assumption fails without it.

1. Run **Actions → infra → Run workflow** with action `apply`. This stands up the hosts, Cloudflare tunnel, DNS, and an empty Secrets Manager secret. Fill the secret (section 4) before deploying anything.
2. Deploy services by pushing to `main` (workflows are path-filtered) or with **Run workflow** on any `deploy-*` workflow. They assume `AWS_DEPLOY_ROLE_ARN` and reach the matching host over SSM.

Once the secret is filled, a sensible first-pass order is: the stateful tier (postgres, redis, keycloak), then services, then the edge (cloudflared, envoy clients, otel, aspire).

For `infra.yml`, `plan` is the default action. `destroy` tears down only the workload — bootstrap and identity-center are untouched.

## 4. Secrets

All runtime values live in a single Secrets Manager secret, `protofast/app`. Terraform creates an empty shell; CI can neither read nor write the value. After the first `infra.yml` apply, fill it as **OrgAdmin** with [scripts/populate-secrets.sh](../scripts/populate-secrets.sh) (or manually in the console).

Ground rules:

- **OrgAdmin only.** PlatformAdmin has no `secretsmanager:` permissions, and the boundary blocks creating the IAM access key SES needs.
- **The script is additive and idempotent.** Keys you don't pass are preserved; a bare run only generates the two DB passwords if they're missing. Re-run it any time to add or rotate keys.
- **Never `terraform init`/`apply` this directory as OrgAdmin.** CI owns this state.
- `deploy.sh` reads these keys on every Host B apply and seeds config files and `.env`. Missing DB passwords abort the deploy; missing auth, JWT, or SMTP keys leave those services broken or silent.
- If the secret is ever deleted and recreated, its values are gone — re-run 4.1 and 4.2.

| Key                                       | Set in                  | Used by                                            |
| ----------------------------------------- | ----------------------- | -------------------------------------------------- |
| `Infra_KcDbPassword`                      | auto-generated          | Postgres superuser + Keycloak                      |
| `Auth_DbPassword`                         | auto-generated          | `auth` DB role                                     |
| `Auth_Keycloak__ClientSecretProtofastWeb` | 4.1                     | Keycloak realm import + auth BFF (`protofast-web`) |
| `Auth_Keycloak__ClientSecretAdmin`        | 4.1                     | Keycloak realm import + auth BFF (`admin`)         |
| `Auth_InternalJwt__PrivateKeyPem`         | 4.1 (PEM)               | auth-svc (EC P-256 private key)                    |
| `Shared_InternalJwt__PublicKeyPem`        | 4.1 (PEM)               | api + payments (matching public key)               |
| `Auth_InternalJwt__KeyId`                 | 4.1 (default `prod-1`)  | auth-svc `kid` header                              |
| `Auth_Smtp__Host`                         | 4.2                     | Keycloak SMTP host                                 |
| `Auth_Smtp__User`                         | 4.2                     | IAM access key id                                  |
| `Auth_Smtp__Password`                     | 4.2                     | Derived SMTP password (not the raw IAM secret)     |
| `Auth_Smtp__From`                         | 4.2                     | Verified From address                              |

Both 4.1 and 4.2 assume an OrgAdmin shell:

```sh
export AWS_PROFILE=protofast-orgadmin AWS_REGION=us-west-2
aws sso login
cd "$(git rev-parse --show-toplevel)"
```

### 4.1 DB passwords + auth material

A single run sets the two Keycloak client secrets and the internal-JWT keypair (P-256, PKCS#8 private / SPKI public — the same form Aspire uses locally), and auto-generates the two DB passwords. Quote the PEM substitutions so the newlines survive.

```sh
openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 -out jwt-private.pem
openssl pkey -in jwt-private.pem -pubout -out jwt-public.pem

scripts/populate-secrets.sh \
  Auth_Keycloak__ClientSecretProtofastWeb="$(openssl rand -hex 32)" \
  Auth_Keycloak__ClientSecretAdmin="$(openssl rand -hex 32)" \
  Auth_InternalJwt__PrivateKeyPem="$(cat jwt-private.pem)" \
  Shared_InternalJwt__PublicKeyPem="$(cat jwt-public.pem)" \
  Auth_InternalJwt__KeyId=prod-1

shred -u jwt-private.pem jwt-public.pem
```

Keycloak's `--import-realm` and auth-svc must agree on the client secret values (`PROTOFAST_WEB_CLIENT_SECRET` / `ADMIN_CLIENT_SECRET` in `.env`), so always rotate both keys together.

### 4.2 SES SMTP (Keycloak email)

Two prerequisites, in either order:

1. An `infra.yml` apply (section 3), which creates the SES *identity* — DKIM, MAIL FROM, and DNS records ([ses.tf](ses.tf)).
2. Set `ses_sender_zone` in the [identity-center/](identity-center/) tfvars and apply that root as OrgAdmin. It creates the sender **IAM user** and its send policy ([identity-center/ses-sender.tf](identity-center/ses-sender.tf)); the user lives there because the boundary denies `iam:CreateUser` to both CI and PlatformAdmin.

Only the access key is created by hand, so no secret value ever reaches Terraform state. The From address is read back from the send policy, so the stored value always matches what the user is allowed to send as:

```sh
USER=protofast-ses-smtp
FROM="$(aws iam get-user-policy --user-name "$USER" --policy-name ses-send \
  --query 'PolicyDocument.Statement[0].Condition.StringEquals."ses:FromAddress"' \
  --output text 2>/dev/null)"
if [ -z "$FROM" ] || [ "$FROM" = None ]; then
  echo "No $USER/ses-send policy — apply identity-center with ses_sender_zone set." >&2
else
  read -r ACCESS_KEY_ID SECRET_ACCESS_KEY <<<"$(aws iam create-access-key \
    --user-name "$USER" --query 'AccessKey.[AccessKeyId,SecretAccessKey]' --output text)"

  scripts/populate-secrets.sh \
    Auth_Smtp__Host="email-smtp.${AWS_REGION}.amazonaws.com" \
    Auth_Smtp__From="$FROM" \
    Auth_Smtp__User="$ACCESS_KEY_ID" \
    Auth_Smtp__Password="$(scripts/ses-smtp-password.sh "$SECRET_ACCESS_KEY")"
fi
```

To rotate, re-run the block (IAM allows two keys per user), then delete the old key:

```sh
aws iam delete-access-key --user-name protofast-ses-smtp --access-key-id <old-id>
```

New SES accounts start in the sandbox and can only send to verified recipients. Request production access in the SES console for this region, wait until the domain, DKIM, and MAIL FROM all show verified, then redeploy keycloak so `deploy.sh` seeds `SMTP_*` into `.env`.
