"""``formats.py`` and ``SourceFormats.cs`` are one allowlist with two implementations.

This is the test the ingest plan's §24 asks for: it reads both files and asserts they agree, row
for row, so the two cannot drift. Without it, a format added in C# would be accepted by
``CreateUpload`` and then refused by the converter — which would look to a user like an upload
that succeeded and a run that failed for no reason.
"""

from __future__ import annotations

import re
from pathlib import Path

import pytest

from conversion.formats import FORMATS

SOURCE_FORMATS_CS = (
    Path(__file__).resolve().parents[2]
    / "segmentation"
    / "src"
    / "ProtoFast.Segmentation.Core"
    / "Ingest"
    / "SourceFormats.cs"
)

# new(".pdf", "application/pdf", "PDF", true, true, [...])
_ROW = re.compile(
    r"""new\(\s*
        "(?P<extension>\.[a-z0-9]+)"\s*,\s*
        "(?P<media_type>[^"]+)"\s*,\s*
        "(?P<label>[^"]*)"\s*,\s*
        (?P<layout>true|false)\s*,\s*
        (?P<ocr>true|false)\s*,""",
    re.VERBOSE | re.DOTALL,
)


def csharp_rows() -> dict[str, tuple[str, bool, bool]]:
    text = SOURCE_FORMATS_CS.read_text(encoding="utf-8")
    rows = {
        match["extension"]: (
            match["media_type"],
            match["layout"] == "true",
            match["ocr"] == "true",
        )
        for match in _ROW.finditer(text)
    }

    # A regex that silently matched nothing would make every assertion below vacuously true.
    assert len(rows) >= 20, f"parsed only {len(rows)} rows from {SOURCE_FORMATS_CS}"
    return rows


@pytest.mark.skipif(not SOURCE_FORMATS_CS.exists(), reason="the C# allowlist is not in this tree")
def test_the_two_allowlists_hold_the_same_extensions():
    assert {f.extension for f in FORMATS} == set(csharp_rows())


@pytest.mark.skipif(not SOURCE_FORMATS_CS.exists(), reason="the C# allowlist is not in this tree")
def test_every_row_agrees_on_media_type_layout_and_ocr():
    rows = csharp_rows()

    for fmt in FORMATS:
        media_type, produces_layout, ocr_capable = rows[fmt.extension]
        assert fmt.media_type == media_type, fmt.extension
        assert fmt.produces_layout == produces_layout, fmt.extension
        assert fmt.ocr_capable == ocr_capable, fmt.extension


@pytest.mark.skipif(not SOURCE_FORMATS_CS.exists(), reason="the C# allowlist is not in this tree")
def test_the_passthrough_set_is_the_same_on_both_sides():
    text = SOURCE_FORMATS_CS.read_text(encoding="utf-8")

    # The declaration, not the reference to it earlier in the file.
    body = re.search(
        r"PassthroughExtensions\s*=\s*new HashSet<string>\([^)]*\)\s*\{([^}]*)\}", text
    )
    assert body is not None, "the C# passthrough set is no longer a HashSet initialiser"

    declared = set(re.findall(r'"(\.[a-z]+)"', body.group(1)))

    assert declared == {f.extension for f in FORMATS if not f.requires_conversion}


def test_neither_side_accepts_an_archive_or_a_media_file():
    # Deferred with their guards named in the plan's §27: a 10 MiB archive can expand to gigabytes,
    # and transcription is either a remote API or a local model.
    extensions = {f.extension for f in FORMATS}
    assert extensions.isdisjoint({".zip", ".mp3", ".mp4", ".wav", ".m4a", ".doc"})
