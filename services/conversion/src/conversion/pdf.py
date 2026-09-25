"""PDF: text-layer probe, OCR, and layout extraction.

pdfplumber (MIT) rather than PyMuPDF, which is AGPL.
"""

from __future__ import annotations

import os
from dataclasses import dataclass

from . import limits
from .layout import LayoutDocument, LayoutLine, is_bold, is_italic, reading_order

# Fewer characters than this means the page has no usable text layer.
MIN_CHARS_PER_PAGE = 100

# Not "every page": scanned books often have a born-digital cover.
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
    """Lines are built from characters, not ``extract_text_lines``, to keep font name and size."""
    import pdfplumber  # noqa: PLC0415 - lazy so tests run without it

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
    if not chars:
        return []

    # Points. Absorbs the baseline jitter between glyphs on the same line.
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

        # The most common font on the line, so a bold drop cap does not make the line bold.
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
    """Writes a text layer into the PDF with OCRmyPDF. ``--skip-text`` leaves pages that already
    have one untouched."""
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
