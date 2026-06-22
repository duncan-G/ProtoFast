# On-host tooling bundle for the IPv6-only app server.
#
# The instance has no IPv4 route, so it cannot reach github.com (IPv4-only) to
# fetch the Docker Compose v2 plugin or grpc_health_probe. It CAN reach the
# private ECR over the dualstack endpoint, so we ship those two github-only
# binaries inside this image instead. CI (which has IPv4) downloads them and
# pushes the image; deploy.sh pulls it and `docker cp`s the binaries onto the
# host (see deploy/deploy.sh ensure_tools).
#
# The binaries are downloaded in the workflow before the build and COPY'd in, so
# the build itself needs no network and no QEMU emulation. busybox gives the
# image a runnable command so `docker create` works for the cp on the host.
FROM busybox:stable

# Built by the CI deploy workflow into ./context (see .github/workflows/deploy.yml).
COPY docker-compose /docker-compose
COPY grpc_health_probe /grpc_health_probe
