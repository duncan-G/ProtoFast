"""MarkItDown with no ``llm_client`` and no ``docintel_endpoint``: both would send document content
to a third party."""

from __future__ import annotations

import functools
from typing import Any

from . import limits


@functools.lru_cache(maxsize=1)
def _markitdown() -> Any:
    try:
        from markitdown import MarkItDown  # noqa: PLC0415 - lazy so tests run without it
    except ImportError as exc:  # pragma: no cover
        raise limits.failed("The converter image is missing markitdown.") from exc

    return MarkItDown(enable_plugins=False)


def to_markdown(path: str) -> str:
    """MarkItDown dispatches on the file's extension, which convert.py sets from the allowlist."""
    try:
        result = _markitdown().convert(path)
    except Exception as exc:  # noqa: BLE001
        raise limits.ConversionError(
            422,
            "unsupported_format",
            "ThePlot could not read this document. It may be corrupt, password-protected, "
            "or not really the kind of file its name says it is.",
            f"{type(exc).__name__}: {exc}"[:2000],
        ) from exc

    return _text_of(result)


def _text_of(result: Any) -> str:
    # `markdown` is the newer name for `text_content`; read whichever this version has.
    for attribute in ("markdown", "text_content"):
        value = getattr(result, attribute, None)
        if isinstance(value, str) and value:
            return value

    return str(result or "")


def version() -> str:
    try:
        from importlib.metadata import version as package_version  # noqa: PLC0415

        return package_version("markitdown")
    except Exception:  # noqa: BLE001
        return "unknown"
