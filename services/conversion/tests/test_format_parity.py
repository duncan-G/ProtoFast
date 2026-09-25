"""``formats.py`` and ``SourceFormats.cs`` must agree row for row, or the api would accept uploads
this service then refuses."""

from __future__ import annotations

import re
from pathlib import Path

from conversion.formats import FORMATS

SOURCE_FORMATS_CS = (
    Path(__file__).resolve().parents[2]
    / "document_import"
    / "src"
    / "ProtoFast.DocumentImport.Core"
    / "SourceFormats.cs"
)

# new(".pdf", "application/pdf", "PDF", true, true, true, [...])
_ROW = re.compile(
    r"""new\(\s*
        "(?P<extension>\.[a-z0-9]+)"\s*,\s*
        "(?P<media_type>[^"]+)"\s*,\s*
        "(?P<label>[^"]*)"\s*,\s*
        (?P<layout>true|false)\s*,\s*
        (?P<ocr>true|false)\s*,\s*
        (?P<conversion>true|false)\s*,""",
    re.VERBOSE | re.DOTALL,
)


def csharp_rows() -> dict[str, tuple[str, bool, bool, bool]]:
    text = SOURCE_FORMATS_CS.read_text(encoding="utf-8")
    rows = {
        match["extension"]: (
            match["media_type"],
            match["layout"] == "true",
            match["ocr"] == "true",
            match["conversion"] == "true",
        )
        for match in _ROW.finditer(text)
    }

    # Guards against a regex that silently matches nothing.
    assert len(rows) >= 20, f"parsed only {len(rows)} rows from {SOURCE_FORMATS_CS}"
    return rows


def test_the_two_allowlists_hold_the_same_extensions():
    assert {f.extension for f in FORMATS} == set(csharp_rows())


def test_every_row_agrees():
    rows = csharp_rows()

    for fmt in FORMATS:
        media_type, produces_layout, ocr_capable, requires_conversion = rows[fmt.extension]
        assert fmt.media_type == media_type, fmt.extension
        assert fmt.produces_layout == produces_layout, fmt.extension
        assert fmt.ocr_capable == ocr_capable, fmt.extension
        assert fmt.requires_conversion == requires_conversion, fmt.extension


def test_neither_side_accepts_an_archive_or_a_media_file():
    # Archives can expand to gigabytes, and transcription would need a remote API or a model.
    extensions = {f.extension for f in FORMATS}
    assert extensions.isdisjoint({".zip", ".mp3", ".mp4", ".wav", ".m4a", ".doc"})
