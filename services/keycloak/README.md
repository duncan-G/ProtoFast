# Keycloak

ProtoFast's identity provider: Keycloak 26.7 with ProtoFast's own providers (sign-in by an
emailed code, sign-up, Sign in with Apple), an email sender that sends through Amazon SES
with the component's own role, and the `protofast`, `theplot` and `offtheloop` login and email
themes, built for PostgreSQL and OpenTelemetry. The realms (`protofast`, `theplot`,
`offtheloop`, `operators`) aren't here: keycloak-config ([`../keycloak-config`](../keycloak-config))
applies them through the admin API once Keycloak is ready.

| Path                          | What it is                                                          |
| ----------------------------- | ------------------------------------------------------------------- |
| `Dockerfile`                  | Builds and tests both providers, then builds Keycloak with them     |
| `entrypoint.sh`               | Turns ProtoStrap's and the AppHost's variables into Keycloak's      |
| `providers/email-otp/`        | ProtoFast's providers, `protofast-keycloak.jar`: a Maven project, Java 21 |
| `providers/ses-email-sender/` | The email sender `ses`: a Maven project, Java 21                     |
| `themes/`                     | The `protofast`, `theplot` and `offtheloop` themes, login and email  |
| `test/entrypoint_test.sh`     | The entrypoint's tests                                              |

## What's in the image

- **Keycloak 26.7.5**, `quay.io/keycloak/keycloak:26.7` pinned by its multi-platform index
  digest.
- **`/opt/keycloak/providers/protofast-keycloak.jar`**, from `providers/email-otp`:

  | Provider          | Id                                     | What it does                                                         |
  | ----------------- | -------------------------------------- | -------------------------------------------------------------------- |
  | Authenticator     | `email-otp`                            | Signs a user in with a code mailed to their address, every account's floor credential |
  | Required action   | `verify-email-code`                    | Proves the address at sign-up by a code instead of a clicked link    |
  | Form action       | `protofast-registration-user-creation` | Creates the user from the sign-up form, or resumes an unverified sign-up that already holds the address |
  | Identity provider | `apple`                                | Sign in with Apple, minting its own client secret per token request  |

  **The realms' browser flow names `email-otp` by id and the registration flow names
  `protofast-registration-user-creation`, so a Keycloak without this JAR can run neither
  sign-in nor sign-up.** The email-code pages (`login-email-otp.ftl`, `verify-email-otp.ftl`,
  `otp-resend.ftl`) are theme resources inside the JAR, so they travel with the
  authenticator; they import `template.ftl` by name and come out wearing the realm's theme.
  Their copy is in the JAR's own message bundle, under `pfOtp*` keys.
- **`/opt/keycloak/providers/ses-email-sender.jar`**: the provider and the AWS SDK for Java's
  `sesv2` with `url-connection-client`, in one JAR. It leaves out what Keycloak already ships
  (SLF4J's API, Reactive Streams) and every Apache and Netty HTTP client.
- **`/opt/keycloak/themes/protofast`, `/opt/keycloak/themes/theplot`** (see [Themes](#themes)).
- **`kc.sh build`'s result**, so `start --optimized`, the default command, starts at once.
- **`/opt/keycloak/bin/protofast-entrypoint.sh`**, the entrypoint, which ends by running
  `kc.sh` with the container's arguments.

The Maven stages run on the build machine's own platform (`--platform=$BUILDPLATFORM`),
since a JAR is the same everywhere; only `kc.sh build` runs under QEMU in the deploy
workflow's arm64 build. Maven's tests run in every build (the SES sender has 31; the
email-otp providers have none), and a failing test fails it. Both JARs are reproducible
(`project.build.outputTimestamp`), so an unchanged provider doesn't rebuild the layers
after it.

## Build time and run time

`kc.sh build` fixes the build-time options in the image. They stay set as variables too, so
`start-dev`, which builds again each time it starts, keeps them unless the container sets
another value.

| Option                       | Variable                                  | When   | Value in the image |
| ---------------------------- | ----------------------------------------- | ------ | ------------------ |
| Database vendor              | `KC_DB`                                   | Build  | `postgres`         |
| Health endpoints             | `KC_HEALTH_ENABLED`                       | Build  | `true`             |
| OpenTelemetry log export     | `KC_FEATURES`, `KC_TELEMETRY_LOGS_ENABLED` | Build | `opentelemetry-logs`, `true` |
| Tracing                      | `KC_TRACING_ENABLED`                      | Build  | `true`             |
| Trace sampler                | `KC_TRACING_SAMPLER_TYPE`                 | Build  | `parentbased_traceidratio` |
| Email sender                 | `KC_SPI_EMAIL_SENDER__PROVIDER`           | Build  | `ses`              |
| Plain HTTP on 8080           | `KC_HTTP_ENABLED`                         | Run    | `true`             |
| The edge's headers           | `KC_PROXY_HEADERS`                        | Run    | `xforwarded`       |
| Back-channel URLs            | `KC_HOSTNAME_BACKCHANNEL_DYNAMIC`         | Run    | `false`: the issuer is `KC_HOSTNAME` on both paths |
| Theme files' browser cache   | `KC_SPI_THEME__STATIC_MAX_AGE`            | Run    | `-1`: `no-cache`   |
| Infinispan's cache spans     | `KC_TRACING_INFINISPAN_ENABLED`           | Run    | `false`            |
| Drain on SIGTERM             | `KC_SHUTDOWN_DELAY`                       | Run    | `10s`              |
| Browsers' URL, the issuer    | `KC_HOSTNAME`                             | Run    | `ORIGIN_AUTH`, by the entrypoint |
| Database connection          | `KC_DB_URL_HOST`, `_PORT`, `_DATABASE`, `KC_DB_USERNAME`, `KC_DB_PASSWORD` | Run | From `ConnectionStrings__keycloak`, by the entrypoint |
| Where telemetry goes         | `KC_TRACING_ENDPOINT`, `KC_TRACING_PROTOCOL`, `KC_TELEMETRY_LOGS_ENDPOINT`, `KC_TELEMETRY_LOGS_PROTOCOL`, `KC_TELEMETRY_SERVICE_NAME`, `KC_TELEMETRY_RESOURCE_ATTRIBUTES`, `KC_TELEMETRY_HEADER_*` | Run | From the `OTEL_` variables, by the entrypoint |
| Trace sampling ratio         | `KC_TRACING_SAMPLER_RATIO`                | Run    | `0.0`, by the entrypoint: Keycloak's own traces are its background tasks |
| Bootstrap admin client       | `KC_BOOTSTRAP_ADMIN_CLIENT_ID`, `_SECRET` | Run, first start only | None |

- **`start --optimized` refuses a different build-time value.** It exits with status 2 and
  "The following build time options have values that differ from what is persisted". So a
  deployed component never sets `KC_DB`, `KC_HEALTH_ENABLED`, `KC_FEATURES`,
  `KC_TRACING_ENABLED`, `KC_TRACING_SAMPLER_TYPE`, `KC_TELEMETRY_LOGS_ENABLED` or
  `KC_SPI_EMAIL_SENDER__PROVIDER`; changing one is a change to this Dockerfile.
- **The SPI option's form matters.** `KC_SPI_EMAIL_SENDER__PROVIDER` (two underscores before
  `PROVIDER`) is `--spi-email-sender--provider`, a build-time option. The older
  `KC_SPI_EMAIL_SENDER_PROVIDER` is taken as a run-time option and ignored by the build.
- **The issuer is `KC_HOSTNAME` on every path.** Tokens are stamped with the issuer of the
  browser's sign-in; if auth's back-channel calls to `http://keycloak:8080` got another one,
  the refresh grant would fail with "Invalid token issuer" and every session would die at its
  first refresh (docs/05-identity.md, "Why two Keycloak authorities"). So
  `KC_HOSTNAME_BACKCHANNEL_DYNAMIC` stays `false`, and `/realms/<realm>/.well-known/openid-configuration`
  says `"issuer": "<ORIGIN_AUTH>/realms/<realm>"` whichever host it is asked on.
- **Health** is on the management port, `http://<host>:9000/health/ready`. On SIGTERM it
  answers 503 at once while 8080 keeps serving for 10 s, then Keycloak has up to 10 s
  (`KC_SHUTDOWN_TIMEOUT`'s default) to finish, so the component's `stop` must be over 20 s.

## What it reads

| Variable                            | From                                                                 | Becomes |
| ----------------------------------- | -------------------------------------------------------------------- | ------- |
| `ORIGIN_AUTH`                       | ProtoStrap and the AppHost: `https://auth.protofast.dev`, `http://auth.dev.localhost:20000` locally | `KC_HOSTNAME`, unless set |
| `ConnectionStrings__keycloak`       | The secret's `Shared_ConnectionStrings__keycloak`; Aspire's reference to the `keycloak` database locally | The database variables, each unless set |
| `KC_BOOTSTRAP_ADMIN_CLIENT_ID`      | The spec's `env`: `protofast-admin`                                    | Itself  |
| `KC_BOOTSTRAP_ADMIN_CLIENT_SECRET`  | The secret's `Keycloak_KC_BOOTSTRAP_ADMIN_CLIENT_SECRET`; a parameter locally | Itself  |
| `KC_BOOTSTRAP_ADMIN_USERNAME`, `_PASSWORD` | Locally only: the AppHost, for a database whose master realm is new | Themselves |
| `OTEL_EXPORTER_OTLP_ENDPOINT`       | ProtoStrap (`http://telemetry:4318`). Unset locally: the JVM doesn't trust the dashboard's certificate, so Keycloak exports nothing | `KC_TRACING_ENDPOINT` and `KC_TELEMETRY_LOGS_ENDPOINT`, each unless set; without it, `KC_TELEMETRY_LOGS_LEVEL=off` |
| `OTEL_EXPORTER_OTLP_PROTOCOL`       | ProtoStrap (`http/protobuf`)                                         | `KC_TRACING_PROTOCOL` and `KC_TELEMETRY_LOGS_PROTOCOL`: `grpc` or `http/protobuf` (unset is `http/protobuf`; `http/json`, which Keycloak can't send, becomes `http/protobuf`) |
| `OTEL_SERVICE_NAME`                 | ProtoStrap (`keycloak`)                                              | `KC_TELEMETRY_SERVICE_NAME` |
| `OTEL_RESOURCE_ATTRIBUTES`          | ProtoStrap                                                           | `KC_TELEMETRY_RESOURCE_ATTRIBUTES` |
| `OTEL_EXPORTER_OTLP_HEADERS`        | Nothing sets it now; for an endpoint that wants headers, such as the dashboard's `x-otlp-api-key` | `KC_TELEMETRY_HEADER_<NAME>`, such as `KC_TELEMETRY_HEADER_X_OTLP_API_KEY`, percent-decoded |
| `PROTOSTRAP_REPLICA`                | ProtoStrap                                                           | With an endpoint, `KC_LOG_CONSOLE_LEVEL=off`, unless set; `KC_CACHE_EMBEDDED_NETWORK_BIND_ADDRESS`, the address the replica's name resolves to on the overlay, unless set |
| `AWS_REGION`, `AWS_CONTAINER_CREDENTIALS_RELATIVE_URI` | ProtoStrap's agent                                 | The SES client's region and credentials |
| Any other `KC_` variable            | Anyone                                                               | Keycloak's option, as usual |

**Secret files.** Under ProtoStrap the component has `secret_files: true`
(`SecretFiles()` in the AppHost), since Keycloak can't read Secrets Manager. Each key in its
`secrets` is a file, `/run/secrets/<KEY>`, and the entrypoint exports each regular file there
as the variable of its name, such as `KC_BOOTSTRAP_ADMIN_CLIENT_SECRET` or
`ConnectionStrings__keycloak`. It follows the agent's links to `..data/<KEY>`, skips
`previous/` and names that aren't variable names, and never replaces a variable that is
already set. Then it reads the connection string, which may come from either.

**The connection string** is ADO.NET's form, as Npgsql and Aspire write it:
`Host=postgres;Port=5432;Username=keycloak;Password=...;Database=keycloak`. Keys are
case-insensitive and may hold spaces; `Server` is `Host`; `User ID`, `UserId`, `User Name`,
`UserName`, `User` and `UID` are `Username`; `PWD` is `Password`; `DB` is `Database`. A value
may be quoted with `"` or `'` (the quote doubled inside), to hold `;`. Other keys, such as
`SSL Mode`, are ignored. A string it can't read stops the container with a message that
doesn't repeat it.

**Telemetry.** Tracing and log export are build-time options, so they are on in every
container, and the entrypoint decides where they go. With `OTEL_EXPORTER_OTLP_ENDPOINT`,
traces and logs go there. For `http/protobuf` Keycloak adds `/v1/traces` and `/v1/logs` to
the endpoint itself, so it gets the endpoint as OpenTelemetry's variable gives it
(`http://telemetry:4318`, with a trailing slash dropped); for `grpc` it uses it as it is.
Traces follow a caller's sampling decision (the edge's requests and auth's back-channel
calls carry a `traceparent`, so they join the sign-in's trace). The traces Keycloak starts
itself aren't sampled: they are its background tasks, such as loading offline sessions and
writing `JGROUPS_PING`, each a root trace of its own. Under ProtoStrap the
console logger is turned off once logs go by OTLP, so each line reaches Loki once;
`docker logs` then shows only what Keycloak prints before its logging starts. Without an
endpoint, log export is turned off too.

The entrypoint prints no value, ever.

## Sending mail

**In an account** the image's sender is `ses`, the only one Keycloak loads (the admin API's
server info lists `emailSender: [ses]`). For every message Keycloak sends, it calls SES's v2
`SendEmail` with the SDK's default region and credentials, which ProtoStrap's agent serves,
and logs `Sent an email with Amazon SES, message id <id>`, never a recipient or a body. So:

- The component has `email` in its `uses` (`.WithReference(email)`), or SES answers
  `AccessDenied`.
- The realm's email settings (`smtpServer`, which keycloak-config applies) supply `from`,
  `no-reply@<HOST_PROTOFAST>`, which must be in one of the account's email domains, and
  `fromDisplayName`. SMTP's settings (`host`, `port`, `auth`, `ssl`, `starttls`,
  `envelopeFrom`) mean nothing to SES and are ignored: SES signs with the domain's DKIM and
  bounces to `bounce.<domain>` itself.
- SES takes only ASCII before an address's `@`, so `allowutf8` changes nothing. Domains are
  sent in Punycode and display names in MIME encoded-words.
- A failure is an `EmailException` naming SES's error code, HTTP status and request id, with
  any address in SES's message replaced by `<address>`. The SDK's exception isn't kept as its
  cause, since Keycloak logs the cause too.
- The SES client is built on the first send, so Keycloak starts without an AWS region; a send
  without one fails with "Unable to load region".

**Locally**, `aspire run` runs this image with `start-dev` and
`KC_SPI_EMAIL_SENDER__PROVIDER=default`. `start-dev` builds again as it starts, so Keycloak's
own SMTP sender is the one loaded (server info: `emailSender: [default]`), and mail goes to
smtp4dev at the host and port in the realm's `smtpServer`, which keycloak-config sets from
`SMTP_HOST` and `SMTP_PORT` in run mode. Both the argument and the variable must be set **in
run mode only**: in the spec they'd reach `start --optimized`, which refuses the different
email sender and exits.

## Themes

`protofast`, `theplot` and `offtheloop` each ship two theme types, which the realms select (`loginTheme`,
`emailTheme`; the `operators` realm uses `protofast`):

| Type    | What it covers                                                          |
| ------- | ----------------------------------------------------------------------- |
| `login` | Sign-in, sign-up, the passkey pages, verify-email and error pages, and the email-code forms from the JAR |
| `email` | Every message the realm sends                                           |

- They render the clients' design systems (Nocturne for `protofast`,
  `clients/protofast/src/styles/nocturne.css`; Ember for `theplot`; the Quiet design's tokens
  in `clients/offtheloop/src/styles/loop.css` for `offtheloop`), so mail and sign-in
  pages read as the same product.
- The email themes have `parent=base`, and base's own templates `<#import "template.ftl">`
  by name, which resolves through the theme chain: the messages these themes don't override
  (organization invites, identity-provider links, the `event-*` notices) still render inside
  their shell. All styling is inline: an email can't use CSS custom properties or a
  stylesheet. Copy lives in `email/messages/messages_en.properties` as plain text, and the
  same keys feed the HTML part and the `text/plain` alternative.
- The admin console's **Test connection** button, under Realm settings → Email, sends
  `email-test.ftl` through the realm's email theme: the one message you can trigger on demand.
- Theme files are served at `/resources/<version>/...`, where the version is one Keycloak
  draws at random and keeps in its database: it moves when Keycloak upgrades, never when a
  theme changes. So they are sent `no-cache` (`KC_SPI_THEME__STATIC_MAX_AGE=-1`), and
  Cloudflare doesn't cache the auth host: a copy cached anywhere outlives a deploy, and the
  new login page, which is `no-store`, would run the old JS.
- **Locally**, the AppHost bind-mounts `themes/` read-only over `/opt/keycloak/themes`, and
  `start-dev` caches no theme, so an edit shows on the next refresh. In an account the
  themes are the image's: a change is a new image.

## The SPI must match the server

`EmailSenderProvider`, `Authenticator`, `RequiredActionProvider`, `FormAction` and the
identity provider SPI are internal to Keycloak (`kc.sh build` warns `KC-SERVICES0047 ... is
implementing the internal SPI`, once per provider: five lines, one each for `email-otp`,
`verify-email-code`, `protofast-registration-user-creation`, `ses` and `apple`; fewer means
a JAR is missing), and they change between releases. Both JARs are compiled against the
server's own version (`keycloak.version` in `providers/*/pom.xml`). On every Keycloak
upgrade:

1. Take the new index digest (`docker buildx imagetools inspect quay.io/keycloak/keycloak:<tag>`)
   and the version it holds (`/opt/keycloak/version.txt`), and set both: the digest in the
   `Dockerfile`, the version in both `keycloak.version`s. Check `jboss.logging.version` (in
   `providers/ses-email-sender/pom.xml`) against `/opt/keycloak/lib/lib/boot`.
2. Read the new `org.keycloak.email.EmailSenderProvider`, the authenticator, required action
   and form action SPIs and `AbstractOAuth2IdentityProvider` for changed methods; a changed
   abstract method fails the build, a new default one doesn't.
3. Check keycloak-config's client library knows any realm field the new version adds
   ([`../keycloak-config/README.md`](../keycloak-config/README.md#upgrading)).
4. Build the image, start it with `start --optimized`, run keycloak-config against it, sign
   in with an emailed code and send a message. A provider built against another version can
   load and still fail at its first use.

When the AWS SDK moves, check `mvn dependency:tree` for a new default HTTP client (2.57 brings
`apache5-client` at runtime) and exclude it like the others.

## Under ProtoStrap

- **Readiness** is `9000/health/ready`, Keycloak's own: it doesn't check the request's
  `Host` against `PROTOSTRAP_COMPONENT`, as the contract asks, since Keycloak can't be told to.
- **The bootstrap admin client** is created once, when Keycloak first makes the master realm.
  A new `Keycloak_KC_BOOTSTRAP_ADMIN_CLIENT_SECRET` written later doesn't change the client's
  secret in Keycloak's database: the old one keeps working and the new one is refused, so
  keycloak-config fails to log in until the client's secret is changed to match. A database
  whose master realm already has an admin user (one made before this image, with
  `KC_BOOTSTRAP_ADMIN_USERNAME`) gets no bootstrap client at all; it needs the client created
  once, by hand.
- **More than one replica** form an Infinispan cluster, found through the database
  (JDBC_PING) and joined on port 7800 at each replica's overlay address. One replica is
  enough for that: a release starts the new replica beside the old one, often on another
  VM, so the two cluster until the old one stops, and its sessions and cache invalidations
  reach the new one. Keycloak supports that only between patch releases of one version: a
  minor upgrade needs the old replica stopped first.

## Tests

```bash
# The SES sender's unit tests run in the image's build:
docker build -t protofast-keycloak:test services/keycloak

# The entrypoint's, from services/keycloak (bash 4.4 or later):
bash test/entrypoint_test.sh
```
