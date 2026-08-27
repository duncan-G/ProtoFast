# Keycloak provider JAR (prod + dev)

`protofast-keycloak.jar` is a **committed build artifact**. Both environments
bind-mount this directory at `/opt/keycloak/providers` rather than baking a
custom Keycloak image:

| | mount |
| --- | --- |
| dev | [`apphost/Program.cs`](../../../apphost/Program.cs) → `WithBindMount` |
| prod | [`docker-compose.host-b.yml`](../../docker-compose.host-b.yml), synced from S3 by `deploy.sh` |

That works because neither environment runs `--optimized`: dev is Aspire's
`start-dev` and prod is `kc.sh start --import-realm`, so the server re-runs its
augmentation at boot and picks the JAR up. Registration shows in the log as four
`KC-SERVICES0047 … internal SPI` lines at start-up — one per provider. Their
absence means the mount is wrong.

**This is not optional cargo.** The realm's browser flow names `email-otp` by id
and its registration flow names `protofast-registration-user-creation`, so a
Keycloak that starts without this JAR can run neither sign-in nor sign-up.

## What is in it

| provider | id | what it does |
| --- | --- | --- |
| Authenticator | `email-otp` | Signs a user in with a code mailed to their address — the floor credential of every account |
| Required action | `verify-email-code` | Proves the address at sign-up by code instead of by a clicked link |
| Form action | `protofast-registration-user-creation` | Creates the user from the sign-up form, or resumes an unverified sign-up that already holds the address |
| Identity provider | `apple` | Sign in with Apple, minting its own client secret per token request |

## Rebuilding

Source lives in
[`infra/keycloak/providers/email-otp`](../../../infra/keycloak/providers/email-otp/).
Maven runs inside a container, so nothing needs a JDK:

```bash
infra/keycloak/providers/build.sh
```

Commit the result alongside the source change. A JAR newer than its sources is
invisible to code review; one older than them is a bug nobody can reproduce from
the tree.

The SPI version is pinned to the running server's. `kc.sh` refuses a provider
compiled against a different one and says so at boot — which is the failure we
want. After a Keycloak upgrade, rebuild against the new tag:

```bash
KEYCLOAK_TAG=26.8 infra/keycloak/providers/build.sh
```
