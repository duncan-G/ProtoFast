"""Content sniffing, so an allowed extension is not taken as proof of the bytes under it.

The presigned POST pins the key and the declared ``Content-Type``, but an attacker holding a valid
URL can still write *different bytes* there. MarkItDown's own detection is not trusted to be safe
on a mislabelled file — handing a ZIP to the EPUB path or a PDF to the XLSX path is exactly the
kind of confusion that turns a parser bug into an exploit — so the magic bytes are checked against
the format before anything is dispatched (ingest plan §6, §21).
"""

from __future__ import annotations

from .formats import Format

#: Formats whose containers are recognisable from their first bytes. Text-shaped formats (Markdown,
#: HTML, CSV, JSON, XML) have no magic and are deliberately absent: there is nothing to check, and
#: they are handled by parsers that treat their input as text either way.
_MAGIC: dict[str, tuple[bytes, ...]] = {
    ".pdf": (b"%PDF-",),
    # OOXML and EPUB are ZIP containers.
    ".docx": (b"PK\x03\x04", b"PK\x05\x06"),
    ".pptx": (b"PK\x03\x04", b"PK\x05\x06"),
    ".xlsx": (b"PK\x03\x04", b"PK\x05\x06"),
    ".epub": (b"PK\x03\x04", b"PK\x05\x06"),
    # OLE2 compound file: legacy Excel and Outlook items.
    ".xls": (b"\xd0\xcf\x11\xe0\xa1\xb1\x1a\xe1",),
    ".msg": (b"\xd0\xcf\x11\xe0\xa1\xb1\x1a\xe1",),
    ".png": (b"\x89PNG\r\n\x1a\n",),
    ".jpg": (b"\xff\xd8\xff",),
    ".jpeg": (b"\xff\xd8\xff",),
    ".tif": (b"II*\x00", b"MM\x00*"),
    ".tiff": (b"II*\x00", b"MM\x00*"),
    ".bmp": (b"BM",),
}

_PREFIX_BYTES = 16


def matches(path: str, fmt: Format) -> bool:
    """True when the file's first bytes are consistent with the format claimed for it."""
    expected = _MAGIC.get(fmt.extension)
    if expected is None:
        return True

    with open(path, "rb") as handle:
        prefix = handle.read(_PREFIX_BYTES)

    return any(prefix.startswith(magic) for magic in expected)
