#!/usr/bin/env bash
# Build the ProtoFast Keycloak provider JAR and stage it where both environments
# read it: deploy/keycloak/providers/.
#
# Maven runs inside a container, so nothing needs a JDK — not a developer machine,
# not a CI image. The only requirement is Docker.
#
# The built JAR is COMMITTED. Both environments bind-mount a directory of provider
# JARs rather than baking a custom Keycloak image, so the artifact has to travel
# with the deploy bundle the same way the realm JSON and the themes do:
#
#   dev   apphost/Program.cs      -> /opt/keycloak/providers
#   prod  deploy/docker-compose.host-b.yml (synced from S3 by deploy.sh)
#
# Re-run this after ANY change under email-otp/, and commit the result alongside
# the source. A JAR that is newer than its sources is invisible to code review and
# a JAR that is older is a bug nobody can reproduce from the tree.
#
# Usage:
#   infra/keycloak/providers/build.sh              # build against the pinned tag
#   KEYCLOAK_TAG=26.7 infra/keycloak/providers/build.sh
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "${HERE}/../../.." && pwd)"
MODULE="${HERE}/email-otp"
STAGE="${REPO}/deploy/keycloak/providers"

# The SPI version must equal the SERVER version, or Keycloak refuses the provider at
# start-up. Default matches the image tag in both environments; override to build
# against a Keycloak you are trialling.
KEYCLOAK_TAG="${KEYCLOAK_TAG:-26.7}"
# Image tags are two-part ("26.7"); Maven coordinates are three-part ("26.7.0").
case "$KEYCLOAK_TAG" in
  *.*.*) KEYCLOAK_VERSION="$KEYCLOAK_TAG" ;;
  *)     KEYCLOAK_VERSION="${KEYCLOAK_TAG}.0" ;;
esac

# A named volume rather than a bind mount for ~/.m2: the host may not share an
# arbitrary path with the Docker VM, and the cache is disposable either way.
docker volume create protofast-m2 >/dev/null

echo "building protofast-keycloak.jar against Keycloak ${KEYCLOAK_VERSION}"
docker run --rm \
  -v protofast-m2:/root/.m2 \
  -v "${MODULE}":/src \
  -w /src \
  maven:3-eclipse-temurin-21 \
  mvn -B -q -Dkeycloak.version="${KEYCLOAK_VERSION}" clean package

mkdir -p "$STAGE"
cp "${MODULE}/target/protofast-keycloak.jar" "${STAGE}/protofast-keycloak.jar"
echo "staged ${STAGE#"${REPO}/"}/protofast-keycloak.jar"
