"""Configuration, from plain ``CONVERSION_*`` environment variables (ingest plan §17.3).

The converter is not a .NET process, so it does not follow the ``Prefix_Section__Key`` convention
— the same deviation the Envoy and OTel containers already make. There are no secrets here: S3
access comes from the instance role over IMDS in production and from LocalStack's throwaway
credentials in development.
"""

from __future__ import annotations

import os
from dataclasses import dataclass, field

MEBIBYTE = 1024 * 1024


def _int(name: str, default: int) -> int:
    raw = os.environ.get(name)
    if not raw:
        return default
    try:
        return int(raw)
    except ValueError:
        return default


def _list(name: str, default: list[str]) -> list[str]:
    raw = os.environ.get(name, "")
    values = [item.strip() for item in raw.split(",") if item.strip()]
    return values or default


@dataclass(frozen=True, slots=True)
class Settings:
    bucket: str = field(default_factory=lambda: os.environ.get("CONVERSION_BUCKET", ""))

    #: LocalStack's gateway in development; unset in production so the SDK resolves real AWS.
    s3_endpoint: str | None = field(
        default_factory=lambda: os.environ.get("CONVERSION_S3_ENDPOINT") or None
    )

    region: str = field(
        default_factory=lambda: os.environ.get("AWS_REGION")
        or os.environ.get("AWS_DEFAULT_REGION")
        or "us-west-2"
    )

    #: Re-checked against S3's ContentLength before any bytes are loaded. The POST policy is the
    #: authoritative limit; this is the backstop for a stale object or a lowered limit (§7.3).
    max_source_bytes: int = field(
        default_factory=lambda: _int("CONVERSION_MAX_SOURCE_BYTES", 10 * MEBIBYTE)
    )

    #: A spreadsheet of a million rows converts to more text than the pipeline can segment.
    max_output_bytes: int = field(
        default_factory=lambda: _int("CONVERSION_MAX_OUTPUT_BYTES", 25 * MEBIBYTE)
    )

    #: The whole-conversion budget, below the caller's HTTP timeout so our error wins the race.
    timeout_seconds: int = field(default_factory=lambda: _int("CONVERSION_TIMEOUT_SECONDS", 480))

    ocr_languages: list[str] = field(
        default_factory=lambda: _list("CONVERSION_OCR_LANGUAGES", ["eng"])
    )

    #: Below this, the run gets a warning event rather than a failure: the user may well want a
    #: thin result from a bad scan, but they should be told why it is thin (§12.3).
    ocr_min_confidence: float = field(
        default_factory=lambda: float(
            os.environ.get("CONVERSION_OCR_MIN_CONFIDENCE", "60")
        )
    )

    #: A 10 MiB bilevel scan can carry several hundred pages, and 300 pages of OCR would hold a
    #: worker slot for a quarter of an hour (§25).
    max_ocr_pages: int = field(default_factory=lambda: _int("CONVERSION_MAX_OCR_PAGES", 200))

    max_pages: int = field(default_factory=lambda: _int("CONVERSION_MAX_PAGES", 2000))

    port: int = field(default_factory=lambda: _int("CONVERSION_PORT", 8090))


def load() -> Settings:
    return Settings()
