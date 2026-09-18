"""MarkItDown, constructed for what it is *not* allowed to do (ingest plan §3, §10).

The arguments that are never passed are the point of this module:

* ``llm_client`` / ``llm_model`` — passing either turns on image captioning, which is an LLM call
  per image, to a third party, on document content.
* ``docintel_endpoint`` — Azure Document Intelligence is remote document AI; the bytes would leave
  the box, and a ``RESTRICTED`` document would leave with them.
* ``enable_plugins`` — third-party plugins are not vetted and are not installed.

That is what keeps conversion inside the sensitivity model: conversion happens entirely on our own
hardware, so it sits *before* the point at which sensitivity starts constraining what may happen to
the text.
"""

from __future__ import annotations

import functools
from typing import Any

from . import limits


@functools.lru_cache(maxsize=1)
def _markitdown() -> Any:
    try:
        from markitdown import MarkItDown  # noqa: PLC0415 - lazy; see images.py
    except ImportError as exc:  # pragma: no cover - the image always has it
        raise limits.failed("The converter image is missing markitdown.") from exc

    return MarkItDown(enable_plugins=False)


def to_markdown(path: str) -> str:
    """Converts a local file, dispatching on its extension.

    The temp file is written with the format's *canonical* extension before this is called, so
    MarkItDown's own dispatch is driven by a value from the allowlist rather than by anything the
    request supplied — and no explicit extension argument is needed, which keeps this off a
    keyword whose name has moved between MarkItDown releases.
    """
    try:
        result = _markitdown().convert(path)
    except Exception as exc:  # noqa: BLE001 - every parser failure is one answer to the caller
        raise limits.ConversionError(
            422,
            "unsupported_format",
            "ThePlot could not read this document. It may be corrupt, password-protected, "
            "or not really the kind of file its name says it is.",
            f"{type(exc).__name__}: {exc}"[:2000],
        ) from exc

    return _text_of(result)


def _text_of(result: Any) -> str:
    """The Markdown, under whichever attribute this MarkItDown version exposes it as.

    ``text_content`` is the long-standing one; ``markdown`` was added later as an alias. Reading
    both is three lines and removes a whole class of "works on my pinned version" failure.
    """
    for attribute in ("markdown", "text_content"):
        value = getattr(result, attribute, None)
        if isinstance(value, str) and value:
            return value

    return str(result or "")


def version() -> str:
    try:
        from importlib.metadata import version as package_version  # noqa: PLC0415

        return package_version("markitdown")
    except Exception:  # noqa: BLE001 - a missing version is cosmetic, not a failure
        return "unknown"
