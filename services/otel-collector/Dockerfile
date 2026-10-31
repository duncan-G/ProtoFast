# The browsers' collector (ProtoStrap's telemetry, "Telemetry from browsers"): the edge
# routes /otlp/ on the protofast, theplot and admin hosts here, rate-limited, and it passes
# what browsers send to OTEL_EXPORTER_OTLP_ENDPOINT, which ProtoStrap sets
# (http://telemetry:4318), or to Aspire's dashboard under `aspire run`. Its config is in the
# image: it reads it once, at start.
#
# otel/opentelemetry-collector-contrib 0.161.0, pinned by its multi-platform index's digest,
# which has linux/arm64: ProtoStrap builds the image for Graviton.
FROM otel/opentelemetry-collector-contrib:0.161.0@sha256:fd328de2552466ad78385e1b1289c3f2402b1c45f265b252aab1955b42845ac1
COPY config.yaml /etc/otelcol-contrib/config.yaml
