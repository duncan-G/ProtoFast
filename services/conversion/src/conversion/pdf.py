"""PDF: the text-layer probe, the OCR decision, and layout extraction (ingest plan §11, §12).

pdfplumber rather than PyMuPDF, deliberately. PyMuPDF is the better geometry library and it is
AGPL — linking it into a product image would put the platform's licensing in play. pdfplumber is
MIT, sits on the same ``pdfminer.six`` parser MarkItDown itself uses for PDFs, and is sufficient:
per-character font name, size and bounding box is everything ``LayoutJoiner`` reads.
"""

from __future__ import annotations

import os
from dataclasses import dataclass

from . import limits
from .layout import LayoutDocument, LayoutLine, is_bold, is_italic, reading_order

#: A page yielding fewer than this many characters has no usable text layer. Generous on purpose:
#: a scanned page with a page number stamped on it still yields a handful of characters.
MIN_CHARS_PER_PAGE = 100

#: If at least this fraction of pages look blank, the document is treated as scanned. Not "all
#: pages", because a scanned book routinely has a born-digital cover or colophon.
SCANNED_PAGE_FRACTION = 0.30


@dataclass(slots=True)
class PdfText:
    pages: int
    layout: LayoutDocument
    chars_per_page: list[int]

    @property
    def looks_scanned(self) -> bool:
        if not self.chars_per_page:
            return False
        blank = sum(1 for count in self.chars_per_page if count < MIN_CHARS_PER_PAGE)
        return blank / len(self.chars_per_page) >= SCANNED_PAGE_FRACTION


def extract(path: str, producer: str = "pdfplumber") -> PdfText:
    """Reads every line's text and geometry out of a PDF's own text layer.

    Lines are grouped from characters rather than taken from ``extract_text_lines`` so the font
    name and size travel with them — those are what distinguish a heading from a bold sentence,
    and a line-level API throws them away.
    """
    # Lazy for the same reason as in images.py: the unit suite stubs this handler and runs
    # without pdfplumber installed.
    import pdfplumber  # noqa: PLC0415

    lines: list[LayoutLine] = []
    chars_per_page: list[int] = []

    with pdfplumber.open(path) as pdf:
        for index, page in enumerate(pdf.pages, start=1):
            chars = page.chars
            chars_per_page.append(len(chars))
            lines.extend(_lines_of_page(chars, index, float(page.width), float(page.height)))

        pages = len(pdf.pages)

    return PdfText(
        pages=pages,
        layout=LayoutDocument(producer=producer, lines=reading_order(lines)),
        chars_per_page=chars_per_page,
    )


def _lines_of_page(
    chars: list[dict], page_number: int, page_width: float, page_height: float
) -> list[LayoutLine]:
    """Groups a page's characters into lines by their baseline.

    pdfplumber reports characters, not lines, and a PDF has no concept of a line at all — so the
    grouping key is the rounded top coordinate, with a tolerance that absorbs the sub-point
    variation between a capital and a comma on the same baseline.
    """
    if not chars:
        return []

    tolerance = 2.0
    buckets: dict[float, list[dict]] = {}

    for char in chars:
        top = round(float(char.get("top", 0.0)) / tolerance) * tolerance
        buckets.setdefault(top, []).append(char)

    lines: list[LayoutLine] = []

    for top, bucket in buckets.items():
        ordered = sorted(bucket, key=lambda c: float(c.get("x0", 0.0)))
        text = "".join(str(c.get("text", "")) for c in ordered).strip()
        if not text:
            continue

        x0 = min(float(c.get("x0", 0.0)) for c in ordered)
        x1 = max(float(c.get("x1", 0.0)) for c in ordered)
        bottom = max(float(c.get("bottom", top)) for c in ordered)

        # The dominant font of the line, not the first character's: a line starting with a bold
        # drop cap is not a bold line, and a heading with one italic word is still a heading.
        font_name, font_size = _dominant_font(ordered)

        lines.append(
            LayoutLine(
                page=page_number,
                text=text,
                x=x0,
                y=float(top),
                width=max(0.0, x1 - x0),
                height=max(0.0, bottom - float(top)),
                pageWidth=page_width,
                pageHeight=page_height,
                fontSize=font_size,
                bold=is_bold(font_name),
                italic=is_italic(font_name),
            )
        )

    return lines


def _dominant_font(chars: list[dict]) -> tuple[str | None, float]:
    counts: dict[tuple[str, float], int] = {}

    for char in chars:
        name = str(char.get("fontname", ""))
        size = round(float(char.get("size", 0.0)), 1)
        counts[(name, size)] = counts.get((name, size), 0) + 1

    if not counts:
        return None, 0.0

    (name, size), _ = max(counts.items(), key=lambda item: item[1])
    return name or None, size


@dataclass(slots=True)
class OcrOutcome:
    applied: bool
    pages_ocred: int
    output_path: str
    engine: str = "tesseract"


def ocr(path: str, languages: list[str], pages: int, budget: limits.Budget) -> OcrOutcome:
    """Writes a real text layer into the PDF with OCRmyPDF, CPU Tesseract only.

    ``--skip-text`` leaves pages that already have a text layer untouched, so a mixed document is
    OCR'd only where it is blank — which is both faster and safer than re-recognising text the
    publisher already gave us.
    """
    destination = f"{path}.ocr.pdf"

    limits.run(
        [
            "ocrmypdf",
            "--skip-text",
            "--output-type",
            "pdf",
            "--optimize",
            "0",
            "--quiet",
            "--language",
            "+".join(languages),
            path,
            destination,
        ],
        timeout=budget.remaining,
        step="OCR",
    )

    if not os.path.exists(destination):
        raise limits.failed("OCR produced no output.")

    return OcrOutcome(applied=True, pages_ocred=pages, output_path=destination)
