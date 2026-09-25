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

    # LocalStack in development; unset in production so boto3 resolves real S3.
    s3_endpoint: str | None = field(
        default_factory=lambda: os.environ.get("CONVERSION_S3_ENDPOINT") or None
    )

    region: str = field(
        default_factory=lambda: (
            os.environ.get("AWS_REGION") or os.environ.get("AWS_DEFAULT_REGION") or "us-west-2"
        )
    )

    # Backstop for the upload policy's limit (SourceFormats.DefaultMaxBytes).
    max_source_bytes: int = field(
        default_factory=lambda: _int("CONVERSION_MAX_SOURCE_BYTES", 10 * MEBIBYTE)
    )

    max_output_bytes: int = field(
        default_factory=lambda: _int("CONVERSION_MAX_OUTPUT_BYTES", 25 * MEBIBYTE)
    )

    # Keep below the caller's HTTP timeout so this service's error wins the race.
    timeout_seconds: int = field(default_factory=lambda: _int("CONVERSION_TIMEOUT_SECONDS", 480))

    ocr_languages: list[str] = field(
        default_factory=lambda: _list("CONVERSION_OCR_LANGUAGES", ["eng"])
    )

    # Percent. Below it the reply carries a warning rather than failing.
    ocr_min_confidence: float = field(
        default_factory=lambda: float(os.environ.get("CONVERSION_OCR_MIN_CONFIDENCE", "60"))
    )

    max_ocr_pages: int = field(default_factory=lambda: _int("CONVERSION_MAX_OCR_PAGES", 200))

    max_pages: int = field(default_factory=lambda: _int("CONVERSION_MAX_PAGES", 2000))

    port: int = field(default_factory=lambda: _int("CONVERSION_PORT", 8090))


def load() -> Settings:
    return Settings()
