"""A Final Draft-style font: symbolic TrueType, Mac Roman cmap only, no Encoding or ToUnicode.
Needs pikepdf and fontTools, which only the image has."""

from __future__ import annotations

import io

import pytest

pikepdf = pytest.importorskip("pikepdf")
pytest.importorskip("fontTools")
pytest.importorskip("pdfminer")

from fontTools.fontBuilder import FontBuilder  # noqa: E402
from fontTools.pens.ttGlyphPen import TTGlyphPen  # noqa: E402
from fontTools.ttLib.tables._c_m_a_p import cmap_format_0  # noqa: E402
from pdfminer.high_level import extract_text  # noqa: E402

from conversion import text_layer  # noqa: E402

GLYPHS = {0x49: "I", 0x6D: "m", 0xD2: "quotedblleft", 0xD3: "quotedblright", 0xD5: "quoteright"}


def mac_roman_font() -> bytes:
    names = [".notdef", *GLYPHS.values()]
    pen = TTGlyphPen(None)
    pen.moveTo((0, 0))
    pen.lineTo((0, 500))
    pen.lineTo((500, 500))
    pen.closePath()
    box = pen.glyph()

    builder = FontBuilder(1000, isTTF=True)
    builder.setupGlyphOrder(names)
    builder.setupCharacterMap({})
    builder.setupGlyf(dict.fromkeys(names, box))
    builder.setupHorizontalMetrics(dict.fromkeys(names, (600, 0)))
    builder.setupHorizontalHeader(ascent=800, descent=-200)
    builder.setupNameTable({"familyName": "CourierFinalDraft", "styleName": "Regular"})
    builder.setupOS2()
    builder.setupPost()

    mac = cmap_format_0(0)
    mac.platformID, mac.platEncID, mac.language = 1, 0, 0
    mac.cmap = dict(GLYPHS)
    builder.font["cmap"].tables = [mac]

    out = io.BytesIO()
    builder.save(out)
    return out.getvalue()


def write_pdf(path: str, *, symbolic: bool = True) -> None:
    document = pikepdf.new()
    descriptor = pikepdf.Dictionary(
        Type=pikepdf.Name.FontDescriptor,
        FontName=pikepdf.Name("/CourierFinalDraft"),
        Flags=4 if symbolic else 32,
        FontBBox=[0, -200, 600, 800],
        ItalicAngle=0,
        Ascent=800,
        Descent=-200,
        CapHeight=700,
        StemV=80,
        FontFile2=document.make_stream(mac_roman_font()),
    )
    font = pikepdf.Dictionary(
        Type=pikepdf.Name.Font,
        Subtype=pikepdf.Name.TrueType,
        BaseFont=pikepdf.Name("/CourierFinalDraft"),
        FirstChar=32,
        LastChar=213,
        Widths=[600] * (213 - 32 + 1),
        FontDescriptor=document.make_indirect(descriptor),
    )
    page = document.add_blank_page(page_size=(612, 792))
    page.Resources = pikepdf.Dictionary(Font=pikepdf.Dictionary(F1=document.make_indirect(font)))
    page.Contents = document.make_stream(b"BT /F1 12 Tf 72 700 Td <D249D56DD3> Tj ET")
    document.save(path)


def test_mac_roman_codes_extract_as_the_glyphs_they_draw(tmp_path):
    source = str(tmp_path / "source.pdf")
    write_pdf(source)
    assert "Õ" in extract_text(source)

    repaired = text_layer.repair(source)

    assert repaired != source
    assert extract_text(repaired).strip() == "“I’m”"


def test_a_pdf_that_needs_nothing_is_not_copied(tmp_path):
    source = str(tmp_path / "source.pdf")
    write_pdf(source, symbolic=False)

    assert text_layer.repair(source) == source


def test_a_repaired_pdf_is_left_alone(tmp_path):
    source = str(tmp_path / "source.pdf")
    write_pdf(source)
    repaired = text_layer.repair(source)

    assert text_layer.repair(repaired) == repaired


def test_bytes_that_are_not_a_pdf_are_left_to_the_extractor(tmp_path):
    source = tmp_path / "source.pdf"
    source.write_bytes(b"%PDF-1.7\nnot really")

    assert text_layer.repair(str(source)) == str(source)
