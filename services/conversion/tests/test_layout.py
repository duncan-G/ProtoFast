"""Layout emission and OCR paragraph reconstruction."""

from __future__ import annotations

from conversion.images import to_markdown
from conversion.layout import LayoutDocument, LayoutLine, is_bold, is_italic, reading_order


def line(page: int, y: float, x: float, text: str = "t", height: float = 10.0) -> LayoutLine:
    return LayoutLine(
        page=page,
        text=text,
        x=x,
        y=y,
        width=100.0,
        height=height,
        pageWidth=612.0,
        pageHeight=792.0,
    )


def test_lines_are_emitted_in_reading_order():
    scrambled = [line(2, 100, 72), line(1, 300, 72), line(1, 100, 300), line(1, 100, 72)]

    ordered = reading_order(scrambled)

    assert [(item.page, item.y, item.x) for item in ordered] == [
        (1, 100, 72),
        (1, 100, 300),
        (1, 300, 72),
        (2, 100, 72),
    ]


def test_the_json_shape_is_stable():
    document = LayoutDocument(producer="pdfplumber", lines=[line(1, 90, 72, "Annual Report")])

    payload = document.to_json()

    assert payload["producer"] == "pdfplumber"
    assert set(payload["lines"][0]) == {
        "page",
        "text",
        "x",
        "y",
        "width",
        "height",
        "pageWidth",
        "pageHeight",
        "fontSize",
        "bold",
        "italic",
        "column",
    }


def test_weight_and_slant_are_read_from_the_font_name():
    assert is_bold("ABCDEF+Helvetica-Bold")
    assert not is_bold("ABCDEF+Helvetica")
    assert is_italic("ABCDEF+Times-Oblique")
    assert is_italic("ABCDEF+Times-Italic")
    assert not is_italic(None)


def test_ocr_lines_wrap_into_paragraphs_at_wide_gaps():
    lines = [
        line(1, 100, 72, "The fleet operated for two hundred", height=10),
        line(1, 112, 72, "and eleven days this season.", height=10),
        line(1, 160, 72, "Three instruments were retired.", height=10),
    ]

    markdown = to_markdown(lines)

    assert markdown.startswith(
        "The fleet operated for two hundred and eleven days this season.\n\n"
    )
    assert markdown.rstrip().endswith("Three instruments were retired.")


def test_no_recognised_text_produces_no_markdown():
    assert to_markdown([]) == ""
