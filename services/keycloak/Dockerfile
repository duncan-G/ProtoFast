# ProtoFast's Keycloak: Keycloak 26.7 with ProtoFast's providers (email codes, sign-up,
# Apple) and the SES email sender, its login and email themes, built for PostgreSQL, health
# checks and OpenTelemetry, behind ProtoStrap's edge. README.md says what is build time and
# run time. The realms aren't here: keycloak-config (../keycloak-config) applies them.
#
# Every base image is pinned by its multi-platform index digest, which includes
# linux/arm64. To move to a newer one, `docker buildx imagetools inspect <image>:<tag>`
# gives the index's digest; each provider's keycloak.version (providers/*/pom.xml) must
# then match the server's version (/opt/keycloak/version.txt).

# The providers, built and tested by Maven. A JAR is the same on every platform, so these
# stages run on the build machine's own platform rather than under QEMU.
FROM --platform=$BUILDPLATFORM maven:3.9-eclipse-temurin-21@sha256:99e61abcff91a9b1333463bd8451fb18495d6eba9250ac66a338b518f8278320 AS email-otp
WORKDIR /build
COPY providers/email-otp/pom.xml ./
RUN mvn -B -ntp -q dependency:go-offline
COPY providers/email-otp/src ./src
RUN mvn -B -ntp package

FROM --platform=$BUILDPLATFORM maven:3.9-eclipse-temurin-21@sha256:99e61abcff91a9b1333463bd8451fb18495d6eba9250ac66a338b518f8278320 AS ses-email-sender
WORKDIR /build
COPY providers/ses-email-sender/pom.xml ./
RUN mvn -B -ntp -q dependency:go-offline
COPY providers/ses-email-sender/src ./src
RUN mvn -B -ntp package

# Keycloak 26.7 (26.7.5).
FROM quay.io/keycloak/keycloak:26.7@sha256:37dbaf6f0722c9ec246335f36e1ef8b2e6cb960f7c27e0d8c615121a3d475a85

COPY --from=email-otp --chown=1000:0 /build/target/protofast-keycloak.jar /opt/keycloak/providers/
COPY --from=ses-email-sender --chown=1000:0 /build/target/ses-email-sender.jar /opt/keycloak/providers/
COPY themes /opt/keycloak/themes/

# Build-time options, fixed by `kc.sh build` so `start --optimized` starts at once. They
# stay set, so `start-dev`, which builds again as it starts, keeps them unless the
# container overrides one: the AppHost sets KC_SPI_EMAIL_SENDER__PROVIDER=default.
# Tracing and log export are build-time options in 26.7, so they are on in every
# container: the entrypoint points them at OTEL_EXPORTER_OTLP_ENDPOINT, or turns the export
# off at run time when there is none (`start --optimized` exits with status 2 when a
# build-time option differs, so it can't turn them off). The sampler keeps a caller's
# decision, so the edge's requests and auth's back-channel calls, which arrive with a
# traceparent, join their trace, and samples the traces Keycloak starts itself at
# KC_TRACING_SAMPLER_RATIO, a run-time option, which the entrypoint sets to 0.0: those are
# its background tasks, such as JGROUPS_PING writes.
ENV KC_DB=postgres \
    KC_HEALTH_ENABLED=true \
    KC_FEATURES=opentelemetry-logs \
    KC_TRACING_ENABLED=true \
    KC_TRACING_SAMPLER_TYPE=parentbased_traceidratio \
    KC_TELEMETRY_LOGS_ENABLED=true \
    KC_SPI_EMAIL_SENDER__PROVIDER=ses
RUN /opt/keycloak/bin/kc.sh build

# Run-time options: plain HTTP on 8080 behind the edge, which terminates TLS and sends
# X-Forwarded-*. KC_HOSTNAME (the entrypoint sets it from ORIGIN_AUTH) is the issuer on
# both paths: back-channel calls to http://keycloak:8080 get the public URL too, so a token
# a browser's sign-in minted refreshes on the back channel (docs/05-identity.md, "Why two
# Keycloak authorities"). Theme files are sent no-cache: their URLs carry a version Keycloak
# keeps in its database, which moves when Keycloak upgrades, never when a theme changes, so
# a cached copy outlives a deploy and a new page runs old JS. Infinispan's cache spans, from
# background session tasks, are left out. Health is on the management port, 9000:
# /health/ready. On SIGTERM, readiness fails at once and Keycloak keeps serving for
# ProtoStrap's 10 s drain, then has up to 10 s (KC_SHUTDOWN_TIMEOUT's default) to finish
# requests, so the component's stop must be over 20 s.
ENV KC_HTTP_ENABLED=true \
    KC_PROXY_HEADERS=xforwarded \
    KC_HOSTNAME_BACKCHANNEL_DYNAMIC=false \
    KC_SPI_THEME__STATIC_MAX_AGE=-1 \
    KC_TRACING_INFINISPAN_ENABLED=false \
    KC_SHUTDOWN_DELAY=10s

COPY --chmod=0755 entrypoint.sh /opt/keycloak/bin/protofast-entrypoint.sh

EXPOSE 8080 9000
ENTRYPOINT ["/opt/keycloak/bin/protofast-entrypoint.sh"]
CMD ["start", "--optimized"]
