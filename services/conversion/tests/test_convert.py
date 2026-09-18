"""The conversion orchestration (ingest plan §9, §10, §12).

The handlers themselves are stubbed: what these tests are about is the sequencing around them —
the size backstop, the content sniff, the idempotency short-circuit, which keys get written, and
which failures are permanent. The handlers' own behaviour needs a real Tesseract and a real
Ghostscript, which only the image has — the CI guard in ``.github/workflows/deploy-conversion.yml``
is what exercises those.
"""

from __future__ import annotations

import json

import pytest

from conversion import convert as conversion
from conversion import limits, markitdown_adapter
from conversion.config import Settings
from conversion.contract import ConvertRequest
from conversion.layout import LayoutDocument, LayoutLine
from conversion.storage import SOURCE_HASH_METADATA


class FakeStorage:
    """An in-memory stand-in with the same surface the real one exposes."""

    def __init__(self, objects: dict[str, bytes] | None = None) -> None:
        self.objects: dict[str, bytes] = dict(objects or {})
        self.metadata: dict[str, dict[str, str]] = {}
        self.bucket = "test-bucket"

    def content_length(self, key: str) -> int | None:
        return len(self.objects[key]) if key in self.objects else None

    def head(self, key: str) -> dict | None:
        if key not in self.objects:
            return None
        return {"ContentLength": len(self.objects[key]), "Metadata": self.metadata.get(key, {})}

    def download(self, key: str, destination: str) -> str:
        with open(destination, "wb") as handle:
            handle.write(self.objects[key])
        return destination

    def put_text(self, key, text, content_type, source_hash, idempotency_key) -> int:
        body = text.encode("utf-8")
        self.objects[key] = body
        self.metadata[key] = {SOURCE_HASH_METADATA: source_hash}
        return len(body)

    def put_json(self, key, value, source_hash, idempotency_key) -> int:
        return self.put_text(
            key, json.dumps(value), "application/json", source_hash, idempotency_key
        )

    def get_text(self, key: str) -> str:
        return self.objects[key].decode("utf-8")

    def existing_source_hash(self, key: str) -> str | None:
        return self.metadata.get(key, {}).get(SOURCE_HASH_METADATA)


def request(extension: str = ".docx", media_type: str | None = None) -> ConvertRequest:
    types = {
        ".docx": "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".pdf": "application/pdf",
        ".png": "image/png",
        ".md": "text/markdown",
    }
    return ConvertRequest(
        uploadId="upl_1",
        sourceKey=f"uploads/alice/upl_1{extension}",
        markdownKey="uploads/alice/upl_1.md",
        layoutKey="uploads/alice/upl_1.layout.json",
        reportKey="uploads/alice/upl_1.conversion.json",
        mediaType=media_type or types[extension],
        fileName=f"document{extension}",
    )


ZIP_MAGIC = b"PK\x03\x04"
PDF_MAGIC = b"%PDF-1.7\n"


@pytest.fixture
def settings() -> Settings:
    return Settings()


def test_a_document_is_converted_and_every_key_is_written(monkeypatch, settings):
    monkeypatch.setattr(markitdown_adapter, "to_markdown", lambda _: "# Title\n\nBody.\n")
    monkeypatch.setattr(markitdown_adapter, "version", lambda: "0.1.14")

    storage = FakeStorage({"uploads/alice/upl_1.docx": ZIP_MAGIC + b"x" * 1000})

    reply = conversion.convert(request(".docx"), storage, settings)

    assert reply.markdownKey == "uploads/alice/upl_1.md"
    assert storage.objects["uploads/alice/upl_1.md"].decode() == "# Title\n\nBody.\n"
    assert "uploads/alice/upl_1.conversion.json" in storage.objects
    assert reply.markdownBytes == len("# Title\n\nBody.\n")


def test_a_format_with_no_geometry_gets_no_layout_object(monkeypatch, settings):
    monkeypatch.setattr(markitdown_adapter, "to_markdown", lambda _: "Body.\n")

    storage = FakeStorage({"uploads/alice/upl_1.docx": ZIP_MAGIC + b"x" * 1000})

    reply = conversion.convert(request(".docx"), storage, settings)

    # An honest absence the pipeline already handles, rather than a synthetic layout with
    # made-up font sizes for LayoutJoiner to believe (ingest plan §11).
    assert reply.layoutKey == ""
    assert "uploads/alice/upl_1.layout.json" not in storage.objects


def test_a_pdf_emits_its_geometry(monkeypatch, settings):
    monkeypatch.setattr(markitdown_adapter, "to_markdown", lambda _: "# Annual Report\n\nBody.\n")
    monkeypatch.setattr(
        conversion.pdf,
        "extract",
        lambda path, producer="pdfplumber": conversion.pdf.PdfText(
            pages=2,
            layout=LayoutDocument(
                producer=producer,
                lines=[
                    LayoutLine(
                        page=1,
                        text="Annual Report",
                        x=72,
                        y=90,
                        width=260,
                        height=22,
                        pageWidth=612,
                        pageHeight=792,
                        fontSize=22,
                        bold=True,
                    )
                ],
            ),
            chars_per_page=[900, 900],
        ),
    )

    storage = FakeStorage({"uploads/alice/upl_1.pdf": PDF_MAGIC + b"x" * 1000})

    reply = conversion.convert(request(".pdf"), storage, settings)

    assert reply.layoutKey == "uploads/alice/upl_1.layout.json"
    assert reply.pages == 2
    assert b"Annual Report" in storage.objects["uploads/alice/upl_1.layout.json"]


def test_a_scanned_pdf_is_ocred_and_says_so(monkeypatch, settings):
    calls: list[str] = []

    def fake_extract(path, producer="pdfplumber"):
        # Blank before OCR, populated after: the same probe, run twice, is what the decision is.
        scanned = "ocr" not in path
        return conversion.pdf.PdfText(
            pages=3,
            layout=LayoutDocument(producer=producer, lines=[]),
            chars_per_page=[0, 0, 0] if scanned else [800, 800, 800],
        )

    def fake_ocr(path, languages, pages, budget):
        calls.append(path)
        return conversion.pdf.OcrOutcome(
            applied=True, pages_ocred=pages, output_path=f"{path}.ocr.pdf"
        )

    monkeypatch.setattr(conversion.pdf, "extract", fake_extract)
    monkeypatch.setattr(conversion.pdf, "ocr", fake_ocr)
    monkeypatch.setattr(markitdown_adapter, "to_markdown", lambda _: "Recognised text.\n")

    storage = FakeStorage({"uploads/alice/upl_1.pdf": PDF_MAGIC + b"x" * 1000})

    reply = conversion.convert(request(".pdf"), storage, settings)

    assert calls, "a PDF with no text layer must reach OCR"
    assert reply.ocr.applied
    assert reply.ocr.engine == "tesseract"
    assert reply.ocr.pagesOcred == 3


def test_a_pdf_with_a_text_layer_is_never_ocred(monkeypatch, settings):
    monkeypatch.setattr(
        conversion.pdf,
        "extract",
        lambda path, producer="pdfplumber": conversion.pdf.PdfText(
            pages=2,
            layout=LayoutDocument(producer=producer, lines=[]),
            chars_per_page=[1200, 1100],
        ),
    )
    monkeypatch.setattr(
        conversion.pdf,
        "ocr",
        lambda *a, **k: pytest.fail("a document with a text layer must not be OCR'd"),
    )
    monkeypatch.setattr(markitdown_adapter, "to_markdown", lambda _: "Body.\n")

    storage = FakeStorage({"uploads/alice/upl_1.pdf": PDF_MAGIC + b"x" * 1000})

    assert not conversion.convert(request(".pdf"), storage, settings).ocr.applied


def test_a_scan_over_the_ocr_page_cap_is_read_without_ocr_and_warns(monkeypatch, settings):
    monkeypatch.setattr(
        conversion.pdf,
        "extract",
        lambda path, producer="pdfplumber": conversion.pdf.PdfText(
            pages=500,
            layout=LayoutDocument(producer=producer, lines=[]),
            chars_per_page=[0] * 500,
        ),
    )
    monkeypatch.setattr(
        conversion.pdf, "ocr", lambda *a, **k: pytest.fail("over the cap, OCR must not run")
    )
    monkeypatch.setattr(markitdown_adapter, "to_markdown", lambda _: "Body.\n")

    storage = FakeStorage({"uploads/alice/upl_1.pdf": PDF_MAGIC + b"x" * 1000})

    reply = conversion.convert(request(".pdf"), storage, settings)

    assert any("over the OCR limit" in warning for warning in reply.warnings)


def test_converting_the_same_source_twice_does_the_work_once(monkeypatch, settings):
    converted = []

    def once(_):
        converted.append(1)
        return "# Title\n\nBody.\n"

    monkeypatch.setattr(markitdown_adapter, "to_markdown", once)

    storage = FakeStorage({"uploads/alice/upl_1.docx": ZIP_MAGIC + b"x" * 1000})

    conversion.convert(request(".docx"), storage, settings)
    reply = conversion.convert(request(".docx"), storage, settings)

    # The hash stamped on the object is the evidence, not a row saying a conversion finished
    # (ingest plan C10, N5).
    assert len(converted) == 1
    assert reply.producer == "cached"
    assert reply.markdownKey == "uploads/alice/upl_1.md"
    assert reply.markdownBytes == len("# Title\n\nBody.\n")


def test_a_reused_conversion_reports_the_first_runs_page_count(monkeypatch, settings):
    monkeypatch.setattr(markitdown_adapter, "to_markdown", lambda _: "Recognised text.\n")
    monkeypatch.setattr(
        conversion.pdf,
        "extract",
        lambda path, producer="pdfplumber": conversion.pdf.PdfText(
            pages=42,
            layout=LayoutDocument(producer=producer, lines=[]),
            chars_per_page=[900] * 42,
        ),
    )

    storage = FakeStorage({"uploads/alice/upl_1.pdf": PDF_MAGIC + b"x" * 1000})

    conversion.convert(request(".pdf"), storage, settings)
    reply = conversion.convert(request(".pdf"), storage, settings)

    # Read back from the earlier run's report, so the run event says "42 pages" both times rather
    # than "0 pages" on the redelivery.
    assert reply.producer == "cached"
    assert reply.pages == 42


def test_a_source_over_the_backstop_is_refused_before_it_is_downloaded(settings):
    storage = FakeStorage({"uploads/alice/upl_1.docx": ZIP_MAGIC + b"x" * (11 * 1024 * 1024)})

    with pytest.raises(limits.ConversionError) as caught:
        conversion.convert(request(".docx"), storage, settings)

    assert caught.value.status == 413


def test_a_missing_source_is_a_permanent_refusal(settings):
    with pytest.raises(limits.ConversionError) as caught:
        conversion.convert(request(".docx"), FakeStorage(), settings)

    assert caught.value.status == 422
    assert caught.value.code == "source_missing"


def test_bytes_that_contradict_the_extension_are_refused(settings):
    # A valid presigned POST pins the key and the declared type, but not the bytes written under
    # them. This is the check that catches that (ingest plan §6, §21).
    storage = FakeStorage({"uploads/alice/upl_1.pdf": b"not a pdf at all"})

    with pytest.raises(limits.ConversionError) as caught:
        conversion.convert(request(".pdf"), storage, settings)

    assert caught.value.status == 422


def test_a_pair_the_api_would_not_have_signed_is_refused(settings):
    storage = FakeStorage({"uploads/alice/upl_1.pdf": PDF_MAGIC})

    with pytest.raises(limits.ConversionError) as caught:
        conversion.convert(request(".pdf", media_type="audio/mpeg"), storage, settings)

    assert caught.value.status == 422
    assert caught.value.code == "unsupported_format"


def test_a_document_that_converts_to_nothing_is_refused(monkeypatch, settings):
    monkeypatch.setattr(markitdown_adapter, "to_markdown", lambda _: "   \n\n  ")

    storage = FakeStorage({"uploads/alice/upl_1.docx": ZIP_MAGIC + b"x" * 1000})

    with pytest.raises(limits.ConversionError) as caught:
        conversion.convert(request(".docx"), storage, settings)

    assert caught.value.code == "no_text"


def test_output_over_the_cap_is_refused_rather_than_written(monkeypatch, settings):
    monkeypatch.setattr(markitdown_adapter, "to_markdown", lambda _: "x" * 2048)

    storage = FakeStorage({"uploads/alice/upl_1.xlsx": ZIP_MAGIC + b"x" * 1000})
    small = Settings(max_output_bytes=1024)

    payload = request(".docx")
    payload = payload.model_copy(
        update={
            "sourceKey": "uploads/alice/upl_1.xlsx",
            "mediaType": "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        }
    )

    with pytest.raises(limits.ConversionError) as caught:
        conversion.convert(payload, storage, small)

    assert caught.value.code == "output_too_large"
    assert "uploads/alice/upl_1.md" not in storage.objects


def test_markdown_handed_to_the_converter_is_passed_through(settings):
    storage = FakeStorage({"uploads/alice/upl_1.md": b"# Already markdown\n"})

    payload = request(".md").model_copy(update={"markdownKey": "uploads/alice/out.md"})
    reply = conversion.convert(payload, storage, settings)

    # The worker skips this service entirely for Markdown; answering correctly anyway is cheaper
    # than answering with an error.
    assert reply.producer == "passthrough"
    assert storage.objects["uploads/alice/out.md"].decode() == "# Already markdown\n"
