# Infra

Production AWS + Cloudflare for ProtoFast. Three Terraform roots, applied in order, then GitHub Actions owns the rest.


| Root                                   | When                 | Who                                          | State                    |
| -------------------------------------- | -------------------- | -------------------------------------------- | ------------------------ |
| `[bootstrap/](bootstrap/)`             | Once, first          | Genesis (root / temporary IAM admin)         | Local disk (gitignored)  |
| `[identity-center/](identity-center/)` | Once, then as needed | Genesis first apply; **OrgAdmin** after that | S3 bucket from bootstrap |
| This directory (`infra/`)              | Ongoing              | GitHub Actions (`infra.yml`) via OIDC        | Same S3 bucket           |


## 1. Bootstrap

One-time local apply that creates the S3 state bucket, GitHub OIDC provider, `protofast-infra` / `protofast-deploy` roles, and the permissions boundary. It also writes the GitHub Actions variables and secrets CI needs (OIDC role ARNs, ECR, Cloudflare inputs).

Follow **[bootstrap/README.md](bootstrap/README.md)** (genesis identity tfvars, Cloudflare token scopes, apply). Keep that shell open — identity-center's first apply uses the same keys.

## 2. Identity Center

Creates SSO groups (`Org-Admins`, `Platform-Admins`, `Developers`), permission sets, and account assignments. People are added in the console, not in Terraform. It also owns the one IAM user in the account — the SES SMTP sender (section 4.2) — since the boundary bars every other applier from creating users.

Follow **[identity-center/README.md](identity-center/README.md)**: first apply as genesis, create yourself as OrgAdmin, sign in, then delete the genesis identity.

## 3. Deploy on GitHub Actions

Create a GitHub Environment named `infra` with required reviewers. `infra.yml` declares `environment: infra`; without it the OIDC assume fails.

1. **Actions → infra → Run workflow**, action `apply`. That stands up hosts, tunnel, DNS, and the empty secret shell. Fill the secret (section 4) before deploying anything.
2. Push to `main` (path-filtered) or **Run workflow** on the `deploy-`* workflows. They assume `AWS_DEPLOY_ROLE_ARN` and SSM the matching host.

Typical first-pass order once the app secret has a version: stateful tier (postgres, redis, keycloak), then services, then edge (cloudflared, envoy clients, otel, aspire). `plan` is the infra default; `destroy` tears the workload down (bootstrap and identity-center are untouched).

## 4. Secrets

All runtime values live in one Secrets Manager secret (`protofast/app`). Terraform creates an empty shell — CI can neither read nor write the value. Fill it as **OrgAdmin** after the first `infra.yml` apply (section 3) with [scripts/populate-secrets.sh](../scripts/populate-secrets.sh) or manually in AWS dashboard.

- **OrgAdmin only.** PlatformAdmin has no `secretsmanager:` grant, and the boundary blocks minting the IAM access key SES needs.
- **Additive and idempotent.** Keys you don't pass are preserved; a bare run generates only the two DB passwords if missing. Re-run to add or rotate.
- **Never `terraform init`/`apply` this directory as OrgAdmin** — CI owns that state.
- `deploy.sh` reads these keys on every Host B apply and seeds files / `.env`. Missing DB passwords abort the deploy; missing auth/JWT/SMTP keys leave those services broken or silent.
- If the secret is ever replaced, its values are gone — re-run 4.1 and 4.2.


| Key                                       | How                    | Used by                                            |
| ----------------------------------------- | ---------------------- | -------------------------------------------------- |
| `Infra_KcDbPassword`                      | auto                   | Postgres superuser + Keycloak                      |
| `Auth_DbPassword`                         | auto                   | `auth` DB role                                     |
| `Auth_Keycloak__ClientSecretProtofastWeb` | pass                   | Keycloak realm import + auth BFF (`protofast-web`) |
| `Auth_Keycloak__ClientSecretAdmin`        | pass                   | Keycloak realm import + auth BFF (`admin`)         |
| `Auth_InternalJwt__PrivateKeyPem`         | pass (PEM)             | auth-svc (EC P-256 private)                        |
| `Shared_InternalJwt__PublicKeyPem`        | pass (PEM)             | api + payments (matching public)                   |
| `Auth_InternalJwt__KeyId`                 | pass; default `prod-1` | auth-svc `kid`                                     |
| `Auth_Smtp__Host`                         | 4.2                    | Keycloak SMTP                                      |
| `Auth_Smtp__User`                         | 4.2                    | IAM access key id                                  |
| `Auth_Smtp__Password`                     | 4.2                    | derived SMTP password, not the raw IAM secret      |
| `Auth_Smtp__From`                         | 4.2                    | verified From address                              |


Run 4.1 and 4.2 from one shell:

```sh
export AWS_PROFILE=protofast-orgadmin AWS_REGION=us-west-2
aws sso login
cd "$(git rev-parse --show-toplevel)"
```

### 4.1 DB passwords + auth material

One run covers the two client secrets, a P-256 keypair in the form Aspire uses locally (PKCS#8 private, SPKI public), and the two auto-generated DB passwords. Quote the PEM substitutions so newlines survive.

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

Keycloak's `--import-realm` and auth-svc must see the same client secret values (`PROTOFAST_WEB_CLIENT_SECRET` / `ADMIN_CLIENT_SECRET` in `.env`), so rotate both keys together.

### 4.2 SES SMTP (Keycloak email)

Two prerequisites, either order:

1. `infra.yml` apply (section 3) creates the SES *identity* — DKIM, MAIL FROM, DNS ([ses.tf](ses.tf)).
2. Set `ses_sender_zone` in [identity-center/](identity-center/) tfvars and apply that root as OrgAdmin. It owns the sender **IAM user** and its send policy ([identity-center/ses-sender.tf](identity-center/ses-sender.tf)) because the boundary denies both CI and PlatformAdmin `iam:CreateUser`.

Only the access key is minted by hand, so no secret value reaches Terraform state. `FROM` is read back off the send policy, so what you store always matches what the user is allowed to send as:

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

To rotate: re-run the block (IAM allows two keys per user), then `aws iam delete-access-key --user-name "$USER" --access-key-id <old-id>`.

New SES accounts start in the sandbox (verified recipients only). Request production access in the SES console for this region, wait until the domain, DKIM, and MAIL FROM all show verified, then redeploy keycloak so `deploy.sh` seeds `SMTP_*` into `.env`.