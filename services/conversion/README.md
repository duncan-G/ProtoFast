# conversion

Turns an uploaded document into Markdown for ThePlot. For formats with pages (PDFs and images) it
also writes a `.layout.json` with per-line geometry and font information.

It reads the source from the documents bucket and writes its outputs back to the same bucket.
Callers send S3 keys, never the document bytes. It has no database, queue or state.

## API

`POST /convert`

```json
{
  "uploadId": "…",
  "sourceKey": "uploads/<subject>/<uploadId>.pdf",
  "markdownKey": "uploads/<subject>/<uploadId>.md",
  "layoutKey": "uploads/<subject>/<uploadId>.layout.json",
  "reportKey": "uploads/<subject>/<uploadId>.conversion.json",
  "mediaType": "application/pdf",
  "ocr": { "enabled": true, "languages": ["eng"], "maxPages": 200 }
}
```

The reply holds the keys it wrote, plus page count, OCR details and warnings. An error returns
`{ code, message, detail }`. A 4xx status means the document will never convert. A 5xx status
means the caller may retry. Converting the same source again is a no-op: every output is stamped
with the source's SHA-256.

## Readiness and stopping

`GET /healthz` is the spec's `ready: /healthz`. It answers 200 only when:

- the `Host` header, without its port, is `PROTOSTRAP_COMPONENT` (`conversion`). A stopped
  replica's overlay address can be given to another service's replica, so a check meant for
  another name fails. When `PROTOSTRAP_COMPONENT` isn't set (`aspire run`, a plain local run)
  any host passes;
- the replica isn't draining;
- the documents bucket has answered a `HeadBucket` once, with this replica's credentials.
  After that readiness doesn't call S3 again.

Otherwise it answers 503. It is left out of traces and never logged.

On SIGTERM it answers 503 at once and keeps serving for `DRAIN_SECONDS` (10 under ProtoStrap,
0 locally), so callers notice. Then it stops accepting, lets conversions in progress finish
for up to `CONVERSION_TIMEOUT_SECONDS` + 120 s (600 s, the caller's own timeout), and exits 0.
A second SIGTERM stops at once.

A conversion checks its budget (`CONVERSION_TIMEOUT_SECONDS`) between steps and gives OCRmyPDF
what is left of it, so it can run past the budget by the step it is in (pdfplumber,
MarkItDown, Tesseract on an image, the S3 writes). The spec's `stop` must cover the drain and
that: `DRAIN_SECONDS` + `CONVERSION_TIMEOUT_SECONDS` + 120 s, 610 s with the defaults. On a
Spot interruption ProtoStrap cuts `stop` to 90 s, and a longer conversion is cut off: the
caller sees the connection drop and retries it.

The server keeps idle connections for 65 s, longer than the edge's 60 s.

## Formats

The allowlist in `src/conversion/formats.py` mirrors
`services/document_import/src/ProtoFast.DocumentImport.Core/SourceFormats.cs`. The test
`tests/test_format_parity.py` fails if the two drift apart.

| Handler | Formats |
|---|---|
| passthrough | `.md` `.markdown` `.txt` |
| pdf | `.pdf`: pdfplumber reads the text layer and OCRmyPDF adds one when the PDF looks scanned. Symbolic TrueType fonts with no ToUnicode map (Final Draft's Courier) get one first, so `’` is not read as `Õ` |
| image | `.png` `.jpg` `.tif` `.bmp`: Tesseract OCR |
| markitdown | Office, HTML, CSV/TSV, JSON, XML, EPUB, Outlook `.msg`, notebooks |

A file's magic bytes must match its extension before any parser sees it.

## What it will not do

- **No LLM.** MarkItDown is built without `llm_client`, so it never captions images.
- **No cloud document AI.** Azure Document Intelligence is not configured.
- **No GPU or local model.** OCR is CPU Tesseract. Audio and video are not on the allowlist.

`requirements.txt` pins every package the image installs, and the Dockerfile installs exactly
those (`--no-deps`, then `pip check`). `tests/test_image.py` fails if one is an `openai`,
`anthropic`, `torch`, `transformers`, `azure-ai-*`, `onnxruntime-gpu` or `nvidia-*` package, or
one of MarkItDown's audio or YouTube dependencies. The image build fails if Tesseract, OCRmyPDF or
Ghostscript doesn't run.

## Configuration

| Variable | Set by | Default | |
|---|---|---|---|
| `BUCKET_DOCUMENTS` | ProtoStrap (`uses: [documents]`), the AppHost locally | — | Required: it won't start without it |
| `AWS_REGION` | ProtoStrap, the AppHost | boto3's own | The S3 client's region. botocore doesn't read it itself |
| `AWS_ENDPOINT_URL_S3` | The AppHost (LocalStack) | unset | boto3 reads it; when set, path-style addressing |
| `AWS_CONTAINER_CREDENTIALS_RELATIVE_URI` | ProtoStrap | — | The role's credentials, found by boto3 |
| `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY` | The AppHost (`test`) | — | LocalStack only |
| `PROTOSTRAP_COMPONENT` | ProtoStrap | unset | Readiness's host test; each log line |
| `PROTOSTRAP_REPLICA`, `PROTOSTRAP_RELEASE` | ProtoStrap | host name, `local` | Each log line; `PROTOSTRAP_REPLICA` set means under ProtoStrap |
| `DRAIN_SECONDS` | unset (ProtoStrap doesn't set it) | 10 under ProtoStrap, else 0 | How long to keep serving after SIGTERM |
| `OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_PROTOCOL`, `OTEL_SERVICE_NAME`, `OTEL_RESOURCE_ATTRIBUTES` | ProtoStrap, Aspire | — | Traces and metrics, through `opentelemetry-instrument`. `http/protobuf` or `grpc`: both exporters are installed |
| `CONVERSION_MAX_SOURCE_BYTES` | | 10 MiB | Backstop for the upload policy's limit |
| `CONVERSION_MAX_OUTPUT_BYTES` | | 25 MiB | |
| `CONVERSION_TIMEOUT_SECONDS` | | 480 | Below the caller's 600 s, so this service's error wins |
| `CONVERSION_OCR_LANGUAGES` | | `eng` | Only `eng` data is in the image |
| `CONVERSION_OCR_MIN_CONFIDENCE` | | 60 | Adds a warning below this, instead of failing |
| `CONVERSION_MAX_OCR_PAGES` / `CONVERSION_MAX_PAGES` | | 200 / 2000 | |
| `CONVERSION_PORT` | | 8090 | |

`CONVERSION_BUCKET` and `CONVERSION_S3_ENDPOINT` are gone: `BUCKET_DOCUMENTS` and
`AWS_ENDPOINT_URL_S3` replace them. It reads no keys from the environment's secret.

The image sets, for telemetry:

- `OTEL_PYTHON_FASTAPI_EXCLUDED_URLS=healthz`: readiness isn't traced.
- `OTEL_LOGS_EXPORTER=none` and `OTEL_PYTHON_LOG_AUTO_INSTRUMENTATION=false`: logs leave by one
  path, JSON lines on stdout (`src/conversion/logs.py`), never over OTLP too.
- `OTEL_PYTHON_LOG_CORRELATION=true`: each line written during a request has its `trace_id`
  and `span_id`.

Each line names the component, replica and release. Logs carry keys, sizes and durations,
never document text or file names.

## Tests

The tests stub MarkItDown, pdfplumber and pytesseract, so they don't need the image's binaries.
`tests/test_format_parity.py` reads document-import's source, so run them in the repository:

```bash
uv venv .venv && uv pip install -r requirements.txt -e '.[dev]'
.venv/bin/ruff check . && .venv/bin/python -m pytest
```

`requirements.txt` says how to change a dependency: edit `pyproject.toml`'s pins, then resolve
it again for linux/arm64.

## Running

Locally, `aspire run` builds the image and points it at LocalStack. The first build is large,
mostly because of Tesseract's language data and Ghostscript.

Under ProtoStrap it is the `conversion` service on port 8090, with no routes: document-import
calls it at `http://conversion:8090`. One process per replica; ProtoStrap scales replicas.

The old sandbox (a read-only root filesystem with a 512 MB tmpfs `/tmp`, all capabilities
dropped, no privilege escalation, a 1 GB memory cap) isn't something ProtoStrap can set. What
the image does instead:

- It runs as an unprivileged user (uid 1654), so it has no capabilities in practice, and it
  can write only `/tmp`, `/var/tmp` and `/run/lock`: the code and Python packages are root's.
- No file in the image is setuid or setgid, so a process that takes over a parser can't gain
  privileges through one.
- The spec's memory request is a hard limit, as the old cap was.

What is no longer covered: `/tmp` is the container's writable layer on the VM's disk rather
than a 512 MB tmpfs, so a document that makes a parser write a lot fills the VM's disk instead
of failing at 512 MB; and capabilities stay in the bounding set, so a kernel exploit has more
to work with.
