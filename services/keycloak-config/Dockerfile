# keycloak-config: keycloak's configure task. ProtoStrap runs it once keycloak is ready, and
# again when this task, one of its keys or keycloak's release changes; it makes the three
# realms in realms/ match their files through Keycloak's admin API with keycloak-config-cli,
# and exits 0 only when all of it is applied. README.md says what it reads and why each
# setting below is what it is.
#
# Every base image is pinned by its multi-platform index digest, which includes
# linux/arm64 (`docker buildx imagetools inspect <image>:<tag>` gives a newer one's).

# keycloak-config-cli 6.5.1, built from its source. Its realm files are read into the
# RealmRepresentation of Keycloak's client library (keycloak-client-common-synced), and a
# field that class doesn't know fails the run. Upstream's newest image has the client
# library 26.0.6, which doesn't know 26.7's webAuthnPolicyPasswordlessMediation, which the
# realms set; 26.0.11 and later do. So this builds the same release with the client
# library 26.0.12 (the admin client through upstream's keycloak.client.version, the
# representations through the one line the sed adds: Keycloak's Quarkus BOM pins them
# otherwise, to 26.0.6), and the rest as upstream's own Dockerfile does: server version
# 26.5.5, no tests. The source is the release's tarball, pinned by its checksum. A JAR is
# the same on every platform, so this stage runs on the build machine's own platform
# rather than under QEMU. On a Keycloak upgrade, check that the client library knows the
# realm fields the new version adds (README.md, "Upgrading").
FROM --platform=$BUILDPLATFORM maven:3.9-eclipse-temurin-21@sha256:99e61abcff91a9b1333463bd8451fb18495d6eba9250ac66a338b518f8278320 AS cli
ADD --checksum=sha256:d30f9832f9880a525a17802b74d3bfc826e4fd86eceaa39e7a37f71302b586f4 \
    https://github.com/adorsys/keycloak-config-cli/archive/refs/tags/v6.5.1.tar.gz /keycloak-config-cli.tar.gz
WORKDIR /build
RUN tar -xzf /keycloak-config-cli.tar.gz --strip-components=1 \
    && sed -i '/Override client library versions BEFORE BOM/a <dependency><groupId>org.keycloak</groupId><artifactId>keycloak-client-common-synced</artifactId><version>${keycloak.client.version}</version></dependency>' pom.xml \
    && grep -q '<artifactId>keycloak-client-common-synced</artifactId><version>${keycloak.client.version}</version>' pom.xml
RUN mvn -B -ntp -q package -DskipTests \
    -Dkeycloak.version=26.5.5 -Dkeycloak.client.version=26.0.12 \
    -Dlicense.skipCheckLicense -Dcheckstyle.skip -Dmaven.test.skip=true -Dmaven.site.skip=true \
    -Dmaven.javadoc.skip=true -Dmaven.gitcommitid.skip=true

# The runtime is upstream's 6.5.1 image (a Java 21 runtime, bash, user 65534) with the JAR
# built above in place of its own.
FROM adorsys/keycloak-config-cli:6.5.1-26.5.5@sha256:0955d98c8a341898b7aa177477edf8a1e90569ae50bbe7598141c1270b773274

COPY --from=cli /build/target/keycloak-config-cli.jar /app/keycloak-config-cli.jar
COPY realms /config/realms/
COPY --chmod=0755 entrypoint.sh /opt/keycloak-config/entrypoint.sh

# How the realms are applied. The entrypoint sets the login (KEYCLOAK_GRANTTYPE,
# KEYCLOAK_CLIENTID, KEYCLOAK_CLIENTSECRET, KEYCLOAK_LOGINREALM) and the file locations.
#
# - IMPORT_VARSUBSTITUTION_ENABLED: the realm files' $(env:NAME) and $(env:NAME:-default)
#   are substituted from the environment, before the files are parsed. A variable without a
#   default that isn't set fails the run (IMPORT_VARSUBSTITUTION_UNDEFINEDISERROR's
#   default).
# - IMPORT_CACHE_ENABLED=false: every run applies every realm. With the cache on,
#   keycloak-config-cli skips a realm whose checksum, kept in the realm's attributes, is
#   unchanged. The checksum is of the substituted text, so a new secret or hostname would
#   change it, but it is written only at the end of a run that succeeded: after a run that
#   failed halfway, the rollback's run applies the last good files, whose checksum is the
#   one kept, and would skip the realm, leaving it half changed. It would skip ProtoStrap's
#   run after a Keycloak upgrade too, and never put back a change made in the admin console.
# - IMPORT_REMOTESTATE_ENABLED: keycloak-config-cli keeps, in each realm's attributes, the
#   names of the clients, roles, required actions and components it applied, and deletes
#   one only when it applied it before and it has left the files. What Keycloak creates
#   itself (the built-in clients, roles and flows, the realm keys, each client's service
#   account user) and what someone adds in the admin console are never in that state, so
#   they stay.
# - IMPORT_MANAGED_*: the defaults, written out. Clients, roles, required actions and
#   components are deleted as above (full, with the state). Authentication flows and
#   identity providers have no state: a top-level flow that isn't built in and isn't in the
#   files is deleted, unless a realm binding or an identity provider uses it, and so is an
#   identity provider that isn't in the files, so a flow or provider is added in the files,
#   never in the console. Client scopes and scope mappings are never deleted (no-delete).
#   Groups, client scope mappings, identity provider mappers, message bundles and workflows
#   aren't in the files, and keycloak-config-cli leaves alone a kind the files don't name.
# - Users are never deleted, whatever these say: keycloak-config-cli can't purge users.
#   It creates or updates the users the files list (each realm's account-admin service
#   account user, and DEV_OPERATORS' users) and sets their realm and client roles to the
#   ones listed (IMPORT_USERS_MERGEROLES=false), apart from default-roles-<realm>.
# - KEYCLOAK_AVAILABILITYCHECK_*: wait up to 2 minutes for Keycloak to answer, so a run
#   that starts as Keycloak does doesn't fail.
ENV IMPORT_FILES_LOCATIONS=file:/config/realms/*.json \
    IMPORT_VARSUBSTITUTION_ENABLED=true \
    IMPORT_CACHE_ENABLED=false \
    IMPORT_REMOTESTATE_ENABLED=true \
    IMPORT_MANAGED_CLIENT=full \
    IMPORT_MANAGED_ROLE=full \
    IMPORT_MANAGED_REQUIREDACTION=full \
    IMPORT_MANAGED_COMPONENT=full \
    IMPORT_MANAGED_SUBCOMPONENT=full \
    IMPORT_MANAGED_AUTHENTICATIONFLOW=full \
    IMPORT_MANAGED_IDENTITYPROVIDER=full \
    IMPORT_MANAGED_CLIENTSCOPE=no-delete \
    IMPORT_MANAGED_SCOPEMAPPING=no-delete \
    IMPORT_USERS_MERGEROLES=false \
    KEYCLOAK_AVAILABILITYCHECK_ENABLED=true \
    KEYCLOAK_AVAILABILITYCHECK_TIMEOUT=120s

ENTRYPOINT ["/opt/keycloak-config/entrypoint.sh"]
CMD []
