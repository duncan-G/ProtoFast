# conversion

Turns an uploaded document into Markdown for ThePlot. For formats with pages (PDFs and images) it
also writes a `.layout.json` with per-line geometry and font information.

It is a queue worker. document-import sends each upload that needs converting to the
`document-conversion` queue, and conversion sends document-import's own message back on
`document-upload` with the outcome in it. It reads the source from the documents bucket and
writes its outputs back to the same bucket: messages carry S3 keys, never the document bytes.
It has no database, port or state, and runs only while conversions are waiting (0–4 replicas).

## Messages

What it takes from `document-conversion`:

```json
{
  "uploadId": "…",
  "sourceKey": "uploads/<subject>/<uploadId>.pdf",
  "markdownKey": "uploads/<subject>/<uploadId>.md",
  "layoutKey": "uploads/<subject>/<uploadId>.layout.json",
  "reportKey": "uploads/<subject>/<uploadId>.conversion.json",
  "mediaType": "application/pdf",
  "ocr": { "enabled": true, "languages": ["eng"], "maxPages": 200 },
  "importRequest": { "uploadId": "…", "userId": "…", "storageKey": "…" }
}
```

What it sends to `document-upload`: `importRequest` as it came, with `conversion` added:

| Outcome | `conversion` | |
|---|---|---|
| Converted | `{ "markdownKey": "…" }` | |
| A `ConversionError` (unsupported, too large, timed out, no text…) | `{ "error": "<code>" }` | A verdict on the document, sent at once |
| Anything else (S3, a bug) | nothing, yet | Left on the queue and delivered again |
| Anything else, on the queue's last delivery | `{ "error": "conversion_failed" }` | Sent rather than dead-lettered, so the desk hears |

A message it can't read is never sent on or deleted: it goes to the dead-letter queue.
Converting the same source again is a no-op: every output is stamped with the source's
SHA-256. The trace travels in the `traceparent` and `tracestate` message attributes, as
ProtoFast's .NET queues send them.

## Stopping

On SIGTERM it stops taking messages. A conversion in progress is cut short and its message
goes back on the queue at once (visibility 0) for another replica, so a stop never waits for
one; sending an outcome and deleting its message are never cut, so document-import never gets
it twice. A replica waiting on a long poll (20 s) stops when it returns, inside the spec's 30 s
`stop`.

A conversion checks its budget (`CONVERSION_TIMEOUT_SECONDS`, 480 s) between steps and gives
OCRmyPDF what is left of it, so it can run past the budget by the step it is in. The queue's
visibility (15 min) covers that, so a slow conversion isn't delivered twice.

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
| `QUEUE_DOCUMENT_CONVERSION_URL` | ProtoStrap (its scale signal), the AppHost | — | Required: the queue it drains |
| `QUEUE_DOCUMENT_UPLOAD_URL` | ProtoStrap (`uses: [document-upload]`), the AppHost | — | Required: where outcomes go |
| `AWS_REGION` | ProtoStrap, the AppHost | boto3's own | The clients' region. botocore doesn't read it itself |
| `AWS_ENDPOINT_URL_S3`, `AWS_ENDPOINT_URL_SQS` | The AppHost (LocalStack) | unset | boto3 reads them; with S3's set, path-style addressing |
| `AWS_CONTAINER_CREDENTIALS_RELATIVE_URI` | ProtoStrap | — | The role's credentials, found by boto3 |
| `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY` | The AppHost (`test`) | — | LocalStack only |
| `PROTOSTRAP_COMPONENT`, `PROTOSTRAP_REPLICA`, `PROTOSTRAP_RELEASE` | ProtoStrap | `conversion`, host name, `local` | Each log line |
| `OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_PROTOCOL`, `OTEL_SERVICE_NAME`, `OTEL_RESOURCE_ATTRIBUTES` | ProtoStrap, Aspire | — | Traces and metrics, through `opentelemetry-instrument`. `http/protobuf` or `grpc`: both exporters are installed |
| `CONVERSION_MAX_SOURCE_BYTES` | | 10 MiB | Backstop for the upload policy's limit |
| `CONVERSION_MAX_OUTPUT_BYTES` | | 25 MiB | |
| `CONVERSION_TIMEOUT_SECONDS` | | 480 | Inside the queue's 15-minute visibility |
| `CONVERSION_OCR_LANGUAGES` | | `eng` | Only `eng` data is in the image |
| `CONVERSION_OCR_MIN_CONFIDENCE` | | 60 | Adds a warning below this, instead of failing |
| `CONVERSION_MAX_OCR_PAGES` / `CONVERSION_MAX_PAGES` | | 200 / 2000 | |

It reads no keys from the environment's secret.

The image sets, for telemetry:

- `OTEL_LOGS_EXPORTER=none` and `OTEL_PYTHON_LOG_AUTO_INSTRUMENTATION=false`: logs leave by one
  path, JSON lines on stdout (`src/conversion/logs.py`), never over OTLP too.
- `OTEL_PYTHON_LOG_CORRELATION=true`: each line written during a conversion has its
  `trace_id` and `span_id`.

Empty long polls aren't traced; each message gets a `document-conversion process` span under
the trace document-import sent it with.

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

Under ProtoStrap it is the `conversion` worker in the compute pool, scaling from zero on the
`document-conversion` queue. One conversion at a time per replica; ProtoStrap scales replicas.

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
