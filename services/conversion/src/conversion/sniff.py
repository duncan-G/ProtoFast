"""Magic-byte checks, because a presigned POST pins the key and content type but not the bytes."""

from __future__ import annotations

from .formats import Format

# Text formats have no magic and are absent on purpose.
_MAGIC: dict[str, tuple[bytes, ...]] = {
    ".pdf": (b"%PDF-",),
    # OOXML and EPUB are ZIP containers.
    ".docx": (b"PK\x03\x04", b"PK\x05\x06"),
    ".pptx": (b"PK\x03\x04", b"PK\x05\x06"),
    ".xlsx": (b"PK\x03\x04", b"PK\x05\x06"),
    ".epub": (b"PK\x03\x04", b"PK\x05\x06"),
    # OLE2 compound file.
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
    expected = _MAGIC.get(fmt.extension)
    if expected is None:
        return True

    with open(path, "rb") as handle:
        prefix = handle.read(_PREFIX_BYTES)

    return any(prefix.startswith(magic) for magic in expected)
