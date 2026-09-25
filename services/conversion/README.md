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

`GET /health` answers without touching S3.

## Formats

The allowlist in `src/conversion/formats.py` mirrors
`services/document_import/src/ProtoFast.DocumentImport.Core/SourceFormats.cs`. The test
`tests/test_format_parity.py` fails if the two drift apart.

| Handler | Formats |
|---|---|
| passthrough | `.md` `.markdown` `.txt` |
| pdf | `.pdf`: pdfplumber reads the text layer and OCRmyPDF adds one when the PDF looks scanned |
| image | `.png` `.jpg` `.tif` `.bmp`: Tesseract OCR |
| markitdown | Office, HTML, CSV/TSV, JSON, XML, EPUB, Outlook `.msg`, notebooks |

A file's magic bytes must match its extension before any parser sees it.

## What it will not do

- **No LLM.** MarkItDown is built without `llm_client`, so it never captions images.
- **No cloud document AI.** Azure Document Intelligence is not configured.
- **No GPU or local model.** OCR is CPU Tesseract. Audio and video are not on the allowlist.

`.github/workflows/deploy-conversion.yml` checks the built image for these rules. It fails the
build if the image contains an `openai`, `anthropic`, `torch` or `azure-ai-*` package, or a CUDA
runtime.

## Configuration

| Variable | Default | |
|---|---|---|
| `CONVERSION_BUCKET` | — | required |
| `CONVERSION_S3_ENDPOINT` | unset | LocalStack in dev only |
| `CONVERSION_MAX_SOURCE_BYTES` | 10 MiB | |
| `CONVERSION_MAX_OUTPUT_BYTES` | 25 MiB | |
| `CONVERSION_TIMEOUT_SECONDS` | 480 | |
| `CONVERSION_OCR_LANGUAGES` | `eng` | only `eng` data is in the image |
| `CONVERSION_OCR_MIN_CONFIDENCE` | 60 | adds a warning below this, instead of failing |
| `CONVERSION_MAX_OCR_PAGES` / `CONVERSION_MAX_PAGES` | 200 / 2000 | |
| `CONVERSION_PORT` | 8090 | |

Traces and logs go to `OTEL_EXPORTER_OTLP_ENDPOINT` through `opentelemetry-instrument`.

## Tests

The tests stub MarkItDown, pdfplumber and pytesseract, so they run without the image:

```bash
python -m pip install pytest pydantic fastapi httpx boto3 ruff && ruff check . && python -m pytest
```

## Running

Locally, `aspire run` builds the image and points it at LocalStack. The first build is large,
mostly because of Tesseract's language data and Ghostscript.

In production it runs as one container on Host B with no published port. The sandbox is a
read-only rootfs, all capabilities dropped, no privilege escalation, a 1 GB memory cap and a tmpfs
`/tmp`. It limits the damage a malicious document can do to the parsers (see
`deploy/docker-compose.host-services.yml`).
