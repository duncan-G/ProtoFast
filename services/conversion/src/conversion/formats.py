"""The accepted source formats. Mirrors ``SourceFormats.cs``; tests/test_format_parity.py keeps
the two in step."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Literal

Handler = Literal["passthrough", "pdf", "image", "markitdown"]


@dataclass(frozen=True, slots=True)
class Format:
    extension: str
    media_type: str
    handler: Handler
    produces_layout: bool
    ocr_capable: bool

    @property
    def requires_conversion(self) -> bool:
        return self.handler != "passthrough"


FORMATS: tuple[Format, ...] = (
    Format(".md", "text/markdown", "passthrough", False, False),
    Format(".markdown", "text/markdown", "passthrough", False, False),
    Format(".txt", "text/plain", "passthrough", False, False),
    Format(".pdf", "application/pdf", "pdf", True, True),
    Format(
        ".docx",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "markitdown",
        False,
        False,
    ),
    Format(
        ".pptx",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "markitdown",
        False,
        False,
    ),
    Format(
        ".xlsx",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "markitdown",
        False,
        False,
    ),
    Format(".xls", "application/vnd.ms-excel", "markitdown", False, False),
    Format(".html", "text/html", "markitdown", False, False),
    Format(".htm", "text/html", "markitdown", False, False),
    Format(".csv", "text/csv", "markitdown", False, False),
    Format(".tsv", "text/tab-separated-values", "markitdown", False, False),
    Format(".json", "application/json", "markitdown", False, False),
    Format(".xml", "application/xml", "markitdown", False, False),
    Format(".epub", "application/epub+zip", "markitdown", False, False),
    Format(".msg", "application/vnd.ms-outlook", "markitdown", False, False),
    Format(".ipynb", "application/x-ipynb+json", "markitdown", False, False),
    Format(".png", "image/png", "image", True, True),
    Format(".jpg", "image/jpeg", "image", True, True),
    Format(".jpeg", "image/jpeg", "image", True, True),
    Format(".tif", "image/tiff", "image", True, True),
    Format(".tiff", "image/tiff", "image", True, True),
    Format(".bmp", "image/bmp", "image", True, True),
)

BY_EXTENSION: dict[str, Format] = {f.extension: f for f in FORMATS}


def extension_of(key: str) -> str | None:
    """Lowercased extension of the key's last path segment, or None."""
    name = key.rsplit("/", 1)[-1]
    dot = name.rfind(".")
    if dot < 0 or dot == len(name) - 1:
        return None
    return name[dot:].lower()


def resolve(source_key: str, media_type: str) -> Format | None:
    """The format for this key, or None when the key's extension and the media type disagree.

    ``media_type`` is the canonical type the api recorded, not the browser's, so aliases are not
    accepted here.
    """
    extension = extension_of(source_key)
    if extension is None:
        return None

    fmt = BY_EXTENSION.get(extension)
    if fmt is None:
        return None

    declared = (media_type or "").split(";", 1)[0].strip().lower()
    if declared and declared != fmt.media_type:
        return None

    return fmt
