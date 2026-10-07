"""Unicode for symbolic TrueType fonts that have no ToUnicode map.

Final Draft and other Mac-born apps embed fonts whose only cmap is Mac Roman, with no /Encoding.
Viewers draw the right glyphs, but pdfminer reads the codes as WinAnsi, so ’ comes out as Õ.
"""

from __future__ import annotations

import io

# FontDescriptor /Flags bit 3.
_SYMBOLIC = 1 << 2

# Simple fonts have single-byte codes; a (3,0) cmap puts them in the private-use row U+F000.
_MAX_CODE = 0xFF
_MICROSOFT_SYMBOL_ROW = 0xF000

# bfchar blocks hold at most 100 entries.
_BFCHAR_LIMIT = 100


def repair(path: str) -> str:
    """Returns a copy of the PDF with a ToUnicode map on every such font, or ``path`` when no
    font needs one."""
    import pikepdf  # noqa: PLC0415 - lazy so tests run without it

    destination = f"{path}.text.pdf"

    try:
        with pikepdf.open(path) as document:
            repaired = 0
            for font in [obj for obj in document.objects if _needs_unicode(obj)]:
                unicode = _unicode_of(font)
                if unicode:
                    font.ToUnicode = document.make_stream(_to_unicode_cmap(unicode))
                    repaired += 1

            if not repaired:
                return path

            document.save(destination)
    except pikepdf.PdfError:
        # pdfplumber reports an unreadable PDF better than this best-effort pass can.
        return path

    return destination


def _needs_unicode(obj) -> bool:
    import pikepdf  # noqa: PLC0415

    if not isinstance(obj, pikepdf.Dictionary):
        return False
    if obj.get("/Type") != "/Font" or obj.get("/Subtype") != "/TrueType":
        return False
    if "/ToUnicode" in obj or "/Encoding" in obj:
        return False

    descriptor = obj.get("/FontDescriptor")
    return (
        descriptor is not None
        and "/FontFile2" in descriptor
        and int(descriptor.get("/Flags", 0)) & _SYMBOLIC != 0
    )


def _unicode_of(font) -> dict[int, str]:
    """Code to text, through the cmap a viewer would use: (3,0) first, else (1,0)."""
    from fontTools import agl  # noqa: PLC0415
    from fontTools.ttLib import TTFont  # noqa: PLC0415

    try:
        program = TTFont(io.BytesIO(font.FontDescriptor.FontFile2.read_bytes()))
        tables = {(t.platformID, t.platEncID): t.cmap for t in program["cmap"].tables}
    except Exception:  # noqa: BLE001 - a font fontTools cannot parse keeps its codes
        return {}

    symbol = {
        code - _MICROSOFT_SYMBOL_ROW: glyph
        for code, glyph in tables.get((3, 0), {}).items()
        if _MICROSOFT_SYMBOL_ROW <= code <= _MICROSOFT_SYMBOL_ROW + _MAX_CODE
    }
    mac = {code: glyph for code, glyph in tables.get((1, 0), {}).items() if code <= _MAX_CODE}

    unicode: dict[int, str] = {}
    for code, glyph in (symbol or mac).items():
        # Subsets often drop glyph names; a Mac Roman code still says what the glyph is.
        text = agl.toUnicode(glyph) or (bytes([code]).decode("mac_roman") if not symbol else "")
        if text:
            unicode[code] = text

    return unicode


def _to_unicode_cmap(unicode: dict[int, str]) -> bytes:
    entries = [
        f"<{code:02X}> <{text.encode('utf-16-be').hex().upper()}>"
        for code, text in sorted(unicode.items())
    ]

    blocks = []
    for start in range(0, len(entries), _BFCHAR_LIMIT):
        chunk = entries[start : start + _BFCHAR_LIMIT]
        blocks.append(f"{len(chunk)} beginbfchar\n" + "\n".join(chunk) + "\nendbfchar")

    return "\n".join(
        [
            "/CIDInit /ProcSet findresource begin",
            "12 dict begin",
            "begincmap",
            "/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def",
            "/CMapName /Adobe-Identity-UCS def",
            "/CMapType 2 def",
            "1 begincodespacerange",
            "<00> <FF>",
            "endcodespacerange",
            *blocks,
            "endcmap",
            "CMapName currentdict /CMap defineresource pop",
            "end",
            "end",
        ]
    ).encode("ascii")
