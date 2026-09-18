# conversion

The document-conversion sidecar for ThePlot's ingest (`docs/theplot-ingest-conversion-plan.md`).

A user drops any document on the allowlist — PDF, Word, PowerPoint, Excel, HTML, EPUB, Outlook
mail, a notebook, a scan — and this service turns it into the Markdown, and where the format
carries geometry the `.layout.json`, that phase 0 of the segmentation pipeline already knows how to
read. Downstream of phase 0 nothing knows conversion happened.

## What it is not

Three things it deliberately cannot do, because they are what keep it inside the platform's
sensitivity model:

- **No LLM.** MarkItDown is constructed without `llm_client`, so its image-captioning path is off.
- **No third-party document AI.** Azure Document Intelligence is supported by MarkItDown and is
  deliberately not configured; it would ship document bytes off the box.
- **No GPU, no local model.** OCR is CPU Tesseract. Audio and video are not on the allowlist at
  all, because transcription is either a remote API or a local model.

Conversion therefore never sends a byte of a `RESTRICTED` document anywhere, which is what lets it
sit *before* the point at which sensitivity starts constraining what may happen to the text.

`.github/workflows/deploy-conversion.yml` asserts all three against the built image — no
`openai`/`anthropic`/`torch`/`azure-ai-*` package, no CUDA runtime. That assertion, not this
README, is what will keep it true in a year.

## Shape

```
src/conversion/
  main.py                 FastAPI: POST /convert, GET /health
  contract.py             the wire shapes, camelCase to match the .NET caller
  formats.py              media type → handler; mirrors SourceFormats.cs (asserted in tests)
  convert.py              orchestration: read, sniff, dispatch, guard, write
  pdf.py                  text-layer probe, OCR decision, pdfplumber layout
  images.py               Tesseract OCR and layout from the OCR boxes
  layout.py               RawLayoutLine emission (the schema LayoutInput.cs defines)
  markitdown_adapter.py   MarkItDown, constructed for what it may not do
  sniff.py                magic-byte check, so an extension is not taken as proof
  storage.py              boto3 S3 get/put, hashes, metadata stamps
  limits.py               size, page, timeout and subprocess guards
  config.py               CONVERSION_* environment variables
```

It owns no database, no queue and no state. The bytes never cross the wire between it and the
segmentation worker: it reads the source from S3 and writes the Markdown, the layout and the report
back to S3 itself, and the worker exchanges only keys with it.

## Running the tests

The heavyweight dependencies (MarkItDown, pdfplumber, pytesseract) are imported lazily and stubbed
by the tests, so the suite runs without them:

```bash
python -m pip install pytest pydantic fastapi httpx boto3 && python -m pytest
```

`tests/test_format_parity.py` reads `SourceFormats.cs` and asserts the two allowlists agree row for
row. Add a format to one and that test fails until you add it to the other — which is the point,
because a format `api` accepts and the converter refuses looks to a user like an upload that
succeeded and a run that failed for no reason.

## Locally

`aspire run` builds and starts it; see `docs/02-local-development.md`. The first build is large
(Tesseract's language data and Ghostscript dominate) — that is the cost of nobody having to install
Tesseract, Ghostscript and ExifTool on their own machine.

## In production

One container on Host B, no published port: only the segmentation worker on the same host dials it.
It runs with a read-only rootfs, every capability dropped, no privilege escalation, a 1 GB memory
cap and a tmpfs for `/tmp` — which is the mitigation for the malicious-document surface (XXE,
zip-slip via OOXML, PDF JS, font exploits) that any document parser has. See
`deploy/docker-compose.host-services.yml`.
