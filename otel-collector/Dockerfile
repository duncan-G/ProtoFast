FROM ghcr.io/open-telemetry/opentelemetry-collector-releases/opentelemetry-collector-contrib:0.123.0

# Copy config into image (used when bind mount is not available in production).
COPY config.yaml /etc/otelcol-contrib/config.yaml
