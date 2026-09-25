"""One conversion: read the source from S3, produce Markdown (and layout where the format has
geometry), write the outputs back to S3 and reply with their keys."""

from __future__ import annotations

import datetime as dt
import json
import os
import tempfile
import time
from dataclasses import dataclass, field

from . import images, limits, markitdown_adapter, pdf, sniff
from .config import Settings
from .contract import ConvertReply, ConvertRequest, OcrResult
from .formats import Format, resolve
from .layout import LayoutDocument
from .storage import Storage, sha256_file


def convert(request: ConvertRequest, storage: Storage, settings: Settings) -> ConvertReply:
    started = time.monotonic()
    started_at = dt.datetime.now(dt.UTC)
    budget = limits.Budget.start(settings.timeout_seconds)

    fmt = resolve(request.sourceKey, request.mediaType)
    if fmt is None:
        raise limits.unsupported(
            f"ThePlot cannot read {request.mediaType or 'that kind of'} files yet."
        )

    size = storage.content_length(request.sourceKey)
    if size is None:
        raise limits.ConversionError(
            422,
            "source_missing",
            "The uploaded document is no longer in storage.",
        )

    limits.check_source_size(size, settings.max_source_bytes)

    with tempfile.TemporaryDirectory(prefix="pf-convert-") as workspace:
        # Named from the allowlist, never the request, because MarkItDown dispatches on it.
        source = os.path.join(workspace, f"source{fmt.extension}")
        storage.download(request.sourceKey, source)

        if not sniff.matches(source, fmt):
            raise limits.unsupported(
                f"That file is named like {fmt.extension} but its contents are something else."
            )

        source_hash = sha256_file(source)

        existing = _reuse(request, storage, source_hash, started)
        if existing is not None:
            return existing

        outcome = _dispatch(request, fmt, source, settings, budget)

        markdown_bytes = len(outcome.markdown.encode("utf-8"))
        limits.check_output_size(markdown_bytes, settings.max_output_bytes)

        if not outcome.markdown.strip():
            raise limits.ConversionError(
                422,
                "no_text",
                "ThePlot found no readable text in this document.",
            )

        written = storage.put_text(
            request.markdownKey, outcome.markdown, "text/markdown", source_hash, request.uploadId
        )

        layout_key = ""
        if outcome.layout is not None and outcome.layout.lines and request.layoutKey:
            storage.put_json(
                request.layoutKey, outcome.layout.to_json(), source_hash, request.uploadId
            )
            layout_key = request.layoutKey

        warnings = list(outcome.warnings)
        if outcome.ocr.applied and 0 < outcome.ocr.meanConfidence < settings.ocr_min_confidence:
            warnings.append(
                f"OCR confidence averaged {outcome.ocr.meanConfidence:.0f}%, "
                "so the text may be unreliable."
            )

        duration_ms = int((time.monotonic() - started) * 1000)

        if request.reportKey:
            storage.put_json(
                request.reportKey,
                _report(
                    request,
                    fmt,
                    outcome,
                    Facts(
                        source_bytes=size,
                        source_hash=source_hash,
                        markdown_bytes=written,
                        layout_key=layout_key,
                        warnings=warnings,
                        started_at=started_at,
                        duration_ms=duration_ms,
                    ),
                ),
                source_hash,
                request.uploadId,
            )

        return ConvertReply(
            markdownKey=request.markdownKey,
            layoutKey=layout_key,
            markdownBytes=written,
            pages=outcome.pages,
            producer=outcome.producer,
            ocr=outcome.ocr,
            warnings=warnings,
            durationMs=duration_ms,
        )


@dataclass(slots=True)
class Outcome:
    markdown: str
    producer: str
    # None for formats with no geometry, which is not the same as an empty layout.
    layout: LayoutDocument | None = None
    pages: int = 0
    ocr: OcrResult = field(default_factory=OcrResult)
    warnings: list[str] = field(default_factory=list)


@dataclass(slots=True)
class Facts:
    source_bytes: int
    source_hash: str
    markdown_bytes: int
    layout_key: str
    warnings: list[str]
    started_at: dt.datetime
    duration_ms: int


def _dispatch(
    request: ConvertRequest,
    fmt: Format,
    source: str,
    settings: Settings,
    budget: limits.Budget,
) -> Outcome:
    if fmt.handler == "passthrough":
        with open(source, encoding="utf-8", errors="replace") as handle:
            return Outcome(markdown=handle.read(), producer="passthrough")

    if fmt.handler == "pdf":
        return _convert_pdf(request, source, settings, budget)

    if fmt.handler == "image":
        return _convert_image(request, source, settings)

    budget.check("conversion")
    return Outcome(
        markdown=markitdown_adapter.to_markdown(source),
        producer=f"markitdown/{markitdown_adapter.version()}",
    )


def _convert_pdf(
    request: ConvertRequest, source: str, settings: Settings, budget: limits.Budget
) -> Outcome:
    """Probe the text layer, OCR only if it looks scanned, then extract from whichever PDF now
    carries the text."""
    budget.check("reading the PDF")
    text = pdf.extract(source)
    limits.check_page_count(text.pages, settings.max_pages)

    warnings: list[str] = []
    ocr = OcrResult()
    current = source

    if text.looks_scanned and request.ocr.enabled:
        if text.pages > min(request.ocr.maxPages, settings.max_ocr_pages):
            warnings.append(
                f"This document has {text.pages} pages, over the OCR limit, so it was read "
                "without OCR and may be mostly empty."
            )
        else:
            budget.check("OCR")
            outcome = pdf.ocr(current, request.ocr.languages, text.pages, budget)
            current = outcome.output_path

            budget.check("reading the OCR'd PDF")
            text = pdf.extract(current, producer="pdfplumber (after OCR)")

            blank = sum(1 for count in text.chars_per_page if count < pdf.MIN_CHARS_PER_PAGE)
            if blank:
                warnings.append(f"{blank} page(s) produced no text after OCR.")

            ocr = OcrResult(
                applied=True,
                engine=outcome.engine,
                pagesOcred=outcome.pages_ocred,
                # OCRmyPDF reports no confidence, so it is left unknown rather than invented.
                meanConfidence=0.0,
            )
    elif text.looks_scanned:
        warnings.append("This document appears to be scanned, and OCR is disabled.")

    budget.check("conversion")

    return Outcome(
        markdown=markitdown_adapter.to_markdown(current),
        producer=f"markitdown/{markitdown_adapter.version()}+pdfplumber",
        layout=text.layout,
        pages=text.pages,
        ocr=ocr,
        warnings=warnings,
    )


def _convert_image(request: ConvertRequest, source: str, settings: Settings) -> Outcome:
    recognised = images.recognise(source, request.ocr.languages)

    warnings: list[str] = []
    if recognised.words == 0:
        warnings.append("No text was recognised in this image.")

    return Outcome(
        markdown=recognised.markdown,
        producer="tesseract",
        layout=recognised.layout,
        pages=1,
        ocr=OcrResult(
            applied=True,
            engine="tesseract",
            pagesOcred=1,
            meanConfidence=recognised.mean_confidence,
        ),
        warnings=warnings,
    )


def _reuse(
    request: ConvertRequest, storage: Storage, source_hash: str, started: float
) -> ConvertReply | None:
    """The earlier reply, rebuilt from S3, when this exact source was already converted."""
    if storage.existing_source_hash(request.markdownKey) != source_hash:
        return None

    head = storage.head(request.markdownKey) or {}
    layout_key = (
        request.layoutKey
        if request.layoutKey and storage.head(request.layoutKey) is not None
        else ""
    )

    report = _previous_report(request, storage)
    ocr = report.get("ocr", {}) if report else {}

    return ConvertReply(
        markdownKey=request.markdownKey,
        layoutKey=layout_key,
        markdownBytes=int(head.get("ContentLength", 0)),
        pages=int(report.get("pages", 0)) if report else 0,
        producer="cached",
        ocr=OcrResult(
            applied=bool(ocr.get("applied", False)),
            engine=str(ocr.get("engine", "")),
            pagesOcred=int(ocr.get("pagesOcred", 0)),
            meanConfidence=float(ocr.get("meanConfidence", 0.0)),
        ),
        warnings=[],
        durationMs=int((time.monotonic() - started) * 1000),
    )


def _previous_report(request: ConvertRequest, storage: Storage) -> dict | None:
    # Best effort: a missing report is no reason to redo a conversion whose Markdown exists.
    if not request.reportKey:
        return None

    try:
        return json.loads(storage.get_text(request.reportKey))
    except Exception:  # noqa: BLE001
        return None


def _report(request: ConvertRequest, fmt: Format, outcome: Outcome, facts: Facts) -> dict:
    """The ``reportKey`` object. Metadata only: no document text and no file name."""
    return {
        "uploadId": request.uploadId,
        "sourceKey": request.sourceKey,
        "sourceBytes": facts.source_bytes,
        "sourceHash": facts.source_hash,
        "mediaType": fmt.media_type,
        "converter": {
            "name": "markitdown",
            "version": markitdown_adapter.version(),
            "extractor": outcome.producer,
        },
        "markdown": {"key": request.markdownKey, "bytes": facts.markdown_bytes},
        "layout": {
            "key": facts.layout_key,
            "lines": len(outcome.layout.lines) if outcome.layout else 0,
        },
        "pages": outcome.pages,
        "ocr": {
            "applied": outcome.ocr.applied,
            "engine": outcome.ocr.engine,
            "languages": request.ocr.languages,
            "pagesOcred": outcome.ocr.pagesOcred,
            "meanConfidence": outcome.ocr.meanConfidence,
        },
        "warnings": facts.warnings,
        "startedAt": facts.started_at.strftime("%Y-%m-%dT%H:%M:%SZ"),
        "durationMs": facts.duration_ms,
    }
