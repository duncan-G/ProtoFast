# otel-collector

ProtoFast's own OpenTelemetry collector, for browsers only. ProtoStrap runs the collector
every component sends to, and it isn't reachable from the internet, so the edge routes
`/otlp/` on the `protofast`, `theplot` and `admin` hosts here, rate-limited, with the path as
it is. This collector passes what it accepts to `OTEL_EXPORTER_OTLP_ENDPOINT`: ProtoStrap's
collector (`http://telemetry:4318`) in an account, Aspire's dashboard under `aspire run`.

- **Traces and logs only,** OTLP over HTTP on port 4318, at `/otlp/v1/traces` and
  `/otlp/v1/logs`: what `clients/*/src/lib/telemetry.browser.ts` and admin-kit's
  `telemetry/browser.ts` send, on the page's own origin. Any other path answers 404.
- **Only from ProtoFast's browser clients:** anyone can send to this route, so spans and log
  records whose `service.name` isn't `protofast-client`, `theplot-client`, `admin-client` or
  `admin-theplot-client` are dropped. A client that changes its name must change it here too.
- **Up to 512 KiB a request,** and a memory limiter at 80% of the replica's memory, which is
  the spec's request and a hard limit.
- **Idle connections are kept 75 s,** longer than the edge's 60 s; the collector's default is
  60 s.
- **Readiness** is the health check extension on port 13133 (`ready: 13133/`). It doesn't test
  the `Host`, and on SIGTERM the collector stops at once rather than draining: a browser's
  batch sent during a deploy may be lost.
- **Under `aspire run`** it exports to the dashboard, with the dashboard's key in
  `ASPIRE_OTLP_API_KEY` (a collector's config can't read `OTEL_EXPORTER_OTLP_HEADERS`).
- The config is in the image, pinned by digest: a change is a new image. Check one with

  ```bash
  docker build -t otel-collector services/otel-collector
  docker run --rm -e OTEL_EXPORTER_OTLP_ENDPOINT=http://telemetry:4318 otel-collector \
    validate --config=/etc/otelcol-contrib/config.yaml
  ```
